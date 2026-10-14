using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Nvizzio.Core.Messaging;
using Nvizzio.Game.Saves.Unlockables;
using Nvizzio.Game.Services;
using UnityEngine;

namespace UnlockConditionLeakFix;

// UnlockConditionSaveData registers itself with MessageRelay in its constructor and only unregisters in
// its finalizer (which never runs, because the relay holds a strong reference) or once its condition is met.
// Every deserialization of the unlockables save (save file load, and the PlayerPrefs backup comparison in
// UnlockablesSaveComponent.ReadAndAdjustWithPlayerPrefs) therefore leaves a full set of orphaned observers
// that stay alive and receive every message for the rest of the session.
//
// This plugin unregisters every UnlockConditionSaveData observer that is not part of the live save data.
[BepInPlugin(Guid, "Unlock Condition Leak Fix", "1.0.0")]
public class Plugin : BasePlugin
{
    public const string Guid = "community.towerdominion.unlockconditionleakfix";

    internal static ManualLogSource Logger;
    internal static ConfigEntry<float> SweepSeconds;
    internal static bool SweepRequested = true;

    public override void Load()
    {
        Logger = Log;
        SweepSeconds = Config.Bind("General", "SweepSeconds", 30f,
            "Seconds between safety sweeps for orphaned unlock-condition observers (0 = only after loads).");
        new Harmony(Guid).PatchAll(typeof(Patches));
        AddComponent<Sweeper>();
    }
}

internal static class Patches
{
    // Runs after the PlayerPrefs backup comparison, which creates and discards a duplicate condition set.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnlockablesSaveComponent), nameof(UnlockablesSaveComponent.ReadAndAdjustWithPlayerPrefs))]
    private static void AfterReadAndAdjust() => Plugin.SweepRequested = true;
}

public class Sweeper : MonoBehaviour
{
    private float nextSweep;

    public Sweeper(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        var interval = Plugin.SweepSeconds.Value;
        if (!Plugin.SweepRequested && (interval <= 0f || Time.unscaledTime < nextSweep))
            return;
        nextSweep = Time.unscaledTime + interval;

        var relay = MessageRelay.Instance;
        if (relay == null || relay.processingMessages)
            return;
        Plugin.SweepRequested = false;

        try
        {
            Sweep(relay);
        }
        catch (Exception e) when (e.Message.Contains("Cannot find service"))
        {
            Plugin.SweepRequested = true; // services not initialized yet (boot); retry next sweep
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Sweep failed: {e}");
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
            Plugin.Logger.LogInfo($"Unregistered {orphans.Count} orphaned unlock-condition observers ({live.Count} live)");
    }
}
