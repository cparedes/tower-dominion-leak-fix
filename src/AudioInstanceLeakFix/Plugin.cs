using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Nvizzio.Core.Audio;
using UnityEngine;

namespace AudioInstanceLeakFix;

// AudioEventBase caps simultaneous instances per clip with a static counter
// (audioClipInstancesPlaying: clip name -> Tuple(max, playing)). Two bugs make the counter drift:
//  1. Register increments the key of targetSource.clip.name at play time; Unregister decrements whatever clip the
//     source holds at stop time, so a clip change in between moves the count to another key (negative counters).
//  2. Unregister only happens in FinishEvent (Stop or the end-of-clip coroutine). AudioEventBase has no
//     OnDisable/OnDestroy, so deactivating or destroying the object mid-sound kills the coroutine and the instance
//     is never unregistered. UI sounds on panels/buttons that hide on click leak this way until the counter reaches
//     the cap, after which the sound never plays again (e.g. tile reroll).
//
// This plugin records which clip each event registered, makes Unregister decrement that clip at most once (never
// below zero), and every second releases registrations whose event was destroyed, deactivated, or stopped playing.
[BepInPlugin(Guid, "Audio Instance Leak Fix", "1.0.0")]
public class Plugin : BasePlugin
{
    public const string Guid = "community.towerdominion.audioinstanceleakfix";

    internal static ManualLogSource Logger;
    internal static ConfigEntry<float> SweepSeconds;
    internal static ConfigEntry<bool> LogReleases;
    internal static ConfigEntry<float> SilentGraceSeconds;

    public override void Load()
    {
        Logger = Log;
        SweepSeconds = Config.Bind("General", "SweepSeconds", 1f, "Seconds between sweeps for orphaned sound instances.");
        SilentGraceSeconds = Config.Bind("General", "SilentGraceSeconds", 5f,
            "Release a registration whose event still claims to be playing after its AudioSource has been silent this long " +
            "(covers objects that were deactivated and re-activated between sweeps; leaves room for delayed starts).");
        LogReleases = Config.Bind("Debug", "LogReleases", true, "Log orphaned sound instances released by the sweep.");
        new Harmony(Guid).PatchAll(typeof(AudioPatches));
        AddComponent<Sweeper>();
    }
}

internal static class Registry
{
    // One entry per outstanding registration: the event (weak, by pointer) and the clip key it incremented.
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

public class Sweeper : MonoBehaviour
{
    private float next;

    public Sweeper(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + Plugin.SweepSeconds.Value;

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

        if (released.Count > 0 && Plugin.LogReleases.Value)
            Plugin.Logger.LogInfo($"Released {string.Join(", ", released)} orphaned sound instance(s) (total {Registry.Released})");
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
            return Time.unscaledTime - r.SilentSince > Plugin.SilentGraceSeconds.Value;
        }
        catch
        {
            return true;                                                // proxy no longer valid
        }
    }
}
