using System;
using System.Collections.Generic;
using HarmonyLib;
using TowerDominionFixes;
using UnityEngine;
#if MELONLOADER
using Il2CppNvizzio.Core.Messaging;
using Il2CppNvizzio.Game.Saves.Unlockables;
using Il2CppNvizzio.Game.Services;
#else
using Nvizzio.Core.Messaging;
using Nvizzio.Game.Saves.Unlockables;
using Nvizzio.Game.Services;
#endif

#if MELONLOADER
[assembly: MelonLoader.MelonInfo(typeof(UnlockConditionLeakFix.Mod), UnlockConditionLeakFix.Info.Name, UnlockConditionLeakFix.Info.Version, "Tower Dominion community")]
[assembly: MelonLoader.MelonGame("Parallel45", "tower-dominion")]
[assembly: MelonLoader.HarmonyDontPatchAll]
#endif

namespace UnlockConditionLeakFix;

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
    public const string Guid = "community.towerdominion.unlockconditionleakfix";
    public const string Name = "Unlock Condition Leak Fix";
    public const string Version = "1.3.0";
}

// UnlockConditionSaveData registers itself with MessageRelay in its constructor and only unregisters in
// its finalizer (which never runs, because the relay holds a strong reference) or once its condition is met.
// Every deserialization of the unlockables save (save file load, and the PlayerPrefs backup comparison in
// UnlockablesSaveComponent.ReadAndAdjustWithPlayerPrefs) therefore leaves a full set of orphaned observers
// that stay alive and receive every message for the rest of the session.
//
// This fix unregisters every UnlockConditionSaveData observer that is not part of the live save data.
public class Fix : IFix
{
    public string Id => Info.Guid;
    public bool NeedsTick => true;

    internal static Setting<float> SweepSeconds;
    internal static bool SweepRequested = true;
    private float nextSweep;

    public void Init(HarmonyLib.Harmony harmony)
    {
        SweepSeconds = Host.Bind("General", "SweepSeconds", 30f,
            "Seconds between safety sweeps for orphaned unlock-condition observers (0 = only after loads).");
        harmony.PatchAll(typeof(Patches));
    }

    public void Tick()
    {
        var interval = SweepSeconds.Value;
        if (!SweepRequested && (interval <= 0f || Time.unscaledTime < nextSweep))
            return;
        nextSweep = Time.unscaledTime + interval;

        var relay = MessageRelay.Instance;
        if (relay == null || relay.processingMessages)
            return;
        SweepRequested = false;

        try
        {
            Sweep(relay);
        }
        catch (Exception e) when (e.Message.Contains("Cannot find service"))
        {
            SweepRequested = true; // services not initialized yet (boot); retry next sweep
        }
        catch (Exception e)
        {
            Host.Warning($"Sweep failed: {e}");
        }
    }

    private static void Sweep(MessageRelay relay)
    {
        var lockedEntities = Services.UnlockablesService?.unlockablesSaveComponent?.SaveData?.LockedEntities;
        if (lockedEntities == null)
            return;

        var live = new HashSet<IntPtr>();
        foreach (var entity in lockedEntities.Values)
            if (entity?.UnlockableConditionsSD != null)
                foreach (var condition in entity.UnlockableConditionsSD.Values)
                    if (condition != null) live.Add(condition.Pointer);

        // UnregisterFromAll removes one occurrence per message type (List.Remove), so repeat it
        // as many times as the observer appears in any single list.
        var orphans = new Dictionary<IntPtr, (IMessageObserver Observer, int Times)>();
        foreach (var observers in relay.registeredObservers.Values)
        {
            var inThisList = new Dictionary<IntPtr, int>();
            foreach (var observer in observers)
            {
                var condition = observer.TryCast<UnlockConditionSaveData>();
                if (condition == null || live.Contains(condition.Pointer))
                    continue;
                var times = inThisList[condition.Pointer] = inThisList.GetValueOrDefault(condition.Pointer) + 1;
                if (!orphans.TryGetValue(condition.Pointer, out var known) || known.Times < times)
                    orphans[condition.Pointer] = (observer, times);
            }
        }

        foreach (var (observer, times) in orphans.Values)
            for (var i = 0; i < times; i++)
                MessageRelay.UnregisterFromAll(observer);

        if (orphans.Count > 0)
            Host.Info($"Unregistered {orphans.Count} orphaned unlock-condition observers ({live.Count} live)");
    }
}

internal static class Patches
{
    // Runs after the PlayerPrefs backup comparison, which creates and discards a duplicate condition set.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnlockablesSaveComponent), nameof(UnlockablesSaveComponent.ReadAndAdjustWithPlayerPrefs))]
    private static void AfterReadAndAdjust() => Fix.SweepRequested = true;
}
