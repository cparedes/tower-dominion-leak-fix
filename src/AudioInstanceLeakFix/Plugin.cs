using System;
using System.Collections.Generic;
using HarmonyLib;
using TowerDominionFixes;
using UnityEngine;
#if MELONLOADER
using Il2CppNvizzio.Core.Audio;
#else
using Nvizzio.Core.Audio;
#endif

#if MELONLOADER
[assembly: MelonLoader.MelonInfo(typeof(AudioInstanceLeakFix.Mod), AudioInstanceLeakFix.Info.Name, AudioInstanceLeakFix.Info.Version, "Tower Dominion community")]
[assembly: MelonLoader.MelonGame("Parallel45", "tower-dominion")]
[assembly: MelonLoader.HarmonyDontPatchAll]
#endif

namespace AudioInstanceLeakFix;

#if BEPINEX
[BepInEx.BepInPlugin(Info.Guid, Info.Name, Info.Version)]
public class Plugin : TowerDominionFixes.BepInExEntry<Fix, Driver> { }
public class Driver : UnityEngine.MonoBehaviour
{
    public Driver(System.IntPtr ptr) : base(ptr) { }
    private void Update() => Plugin.Fix.Tick();
}
#else
public class Mod : TowerDominionFixes.MelonEntry<Fix> { }
#endif

internal static class Info
{
    public const string Guid = "community.towerdominion.audioinstanceleakfix";
    public const string Name = "Audio Instance Leak Fix";
    public const string Version = "1.4.0";
}

// AudioEventBase caps simultaneous instances per clip with a static counter
// (audioClipInstancesPlaying: clip name -> Tuple(max, playing)). Two bugs make the counter drift:
//  1. Register increments the key of targetSource.clip.name at play time; Unregister decrements whatever clip the
//     source holds at stop time, so a clip change in between moves the count to another key (negative counters).
//  2. Unregister only happens in FinishEvent (Stop or the end-of-clip coroutine). AudioEventBase has no
//     OnDisable/OnDestroy, so deactivating or destroying the object mid-sound kills the coroutine and the instance
//     is never unregistered. UI sounds on panels/buttons that hide on click leak this way until the counter reaches
//     the cap, after which the sound never plays again (e.g. tile reroll).
//
// This fix records which clip each event registered, makes Unregister decrement that clip at most once (never
// below zero), and every second releases registrations whose event was destroyed, deactivated, or stopped playing.
public class Fix : IFix
{
    public string Id => Info.Guid;
    public bool NeedsTick => true;

    internal static Setting<float> SweepSeconds;
    internal static Setting<float> SilentGraceSeconds;
    internal static Setting<bool> LogReleases;
    private float next;

    public void Init(HarmonyLib.Harmony harmony)
    {
        SweepSeconds = Host.Bind("General", "SweepSeconds", 1f, "Seconds between sweeps for orphaned sound instances.");
        SilentGraceSeconds = Host.Bind("General", "SilentGraceSeconds", 5f,
            "Release a registration whose event still claims to be playing after its AudioSource has been silent this long " +
            "(covers objects that were deactivated and re-activated between sweeps; leaves room for delayed starts).");
        LogReleases = Host.Bind("Debug", "LogReleases", true, "Log orphaned sound instances released by the sweep.");
        harmony.PatchAll(typeof(AudioPatches));
    }

    public void Tick()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + SweepSeconds.Value;

        var released = new Dictionary<string, int>();
        for (var i = Registry.Outstanding.Count - 1; i >= 0; i--)
        {
            var r = Registry.Outstanding[i];
            if (!IsOrphaned(r)) continue;
            Registry.Adjust(r.ClipKey, -1);
            Registry.Outstanding.RemoveAt(i);
            released[r.ClipKey] = released.GetValueOrDefault(r.ClipKey) + 1;
            Registry.Released++;
        }

        if (released.Count > 0 && LogReleases.Value)
            Host.Info($"Released {string.Join(", ", released)} orphaned sound instance(s) (total {Registry.Released})");
    }

    private static bool IsOrphaned(Registry.Registration r)
    {
        try
        {
            var e = r.Event;
            if (e == null) return true;                                 // destroyed (Unity null check)
            if (!e.gameObject.activeInHierarchy) return true;           // its finish coroutine can't run any more
            var source = e.targetSource;
            if (source == null) return true;
            if (e.isPaused || source.isPlaying) { r.SilentSince = -1f; return false; }
            if (!e.isPlaying) return true;                              // stopped without FinishEvent

            // Event says playing but the source is silent: normal briefly (end-of-clip wait, delayed start);
            // persistent silence means its finish coroutine is gone.
            if (r.SilentSince < 0f) r.SilentSince = Time.unscaledTime;
            return Time.unscaledTime - r.SilentSince > SilentGraceSeconds.Value;
        }
        catch
        {
            return true;                                                // proxy no longer valid
        }
    }
}

internal static class Registry
{
    // One entry per outstanding registration: the event (by pointer) and the clip key it incremented.
    internal sealed class Registration
    {
        public IntPtr EventPtr;
        public AudioEventBase Event;
        public string ClipKey;
        public float SilentSince = -1f; // first sweep that saw the event "playing" with a silent source
    }

    internal static readonly List<Registration> Outstanding = new();
    internal static long Released;

    internal static void Adjust(string key, int delta)
    {
        var counters = AudioEventBase.audioClipInstancesPlaying;
        if (key == null || counters == null || !counters.ContainsKey(key))
            return;
        var current = counters[key];
        var playing = Math.Max(0, current.Item2 + delta);
        counters[key] = new Il2CppSystem.Tuple<int, int>(current.Item1, playing);
    }

    internal static string ClipKeyOf(AudioEventBase e)
    {
        var source = e.targetSource;
        if (source == null) return null;
        var clip = source.clip;
        return clip == null ? null : clip.name;
    }
}

[HarmonyPatch]
internal static class AudioPatches
{
    // Mirror the game's increment and remember which clip key it used.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(AudioEventBase), "RegisterSoundInstancePlaying")]
    private static void AfterRegister(AudioEventBase __instance)
    {
        var key = Registry.ClipKeyOf(__instance);
        if (key != null)
            Registry.Outstanding.Add(new Registry.Registration { EventPtr = __instance.Pointer, Event = __instance, ClipKey = key });
    }

    // Replace the game's decrement: release this event's oldest outstanding registration, using the clip key
    // recorded at register time. Unregistering an event with nothing outstanding is a no-op.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(AudioEventBase), "UnregisterSoundInstancePlaying")]
    private static bool InsteadOfUnregister(AudioEventBase __instance)
    {
        var index = Registry.Outstanding.FindIndex(r => r.EventPtr == __instance.Pointer);
        if (index >= 0)
        {
            Registry.Adjust(Registry.Outstanding[index].ClipKey, -1);
            Registry.Outstanding.RemoveAt(index);
        }
        return false;
    }
}
