using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace AssetUnloadThrottleFix;

// Copper.ViewManager's ResourceViewLoader.DestroyView (RVA 0x88BC60) calls Resources.UnloadUnusedAssets() every
// time a view is destroyed (building panel on deselect, popups, tooltips...): 26 times in ~4 minutes of play.
// Each one makes Unity walk every loaded asset and reachable managed object (~70k objects); the expensive
// UnloadUnusedAssetsOperation::IntegrateMainThread -> GarbageCollectSharedAssets step lands on the main thread a
// few frames later as a 20-30 ms hitch. DestroyView discards the returned AsyncOperation.
//
// This plugin lets the first call through and then at most one real unload per MinIntervalSeconds; calls in between
// are skipped (returning null, which the only caller ignores). Unity's own unloads on scene changes are unaffected.
[BepInPlugin(Guid, "Asset Unload Throttle Fix", "1.0.0")]
public class Plugin : BasePlugin
{
    public const string Guid = "community.towerdominion.assetunloadthrottlefix";

    internal static ManualLogSource Logger;
    internal static ConfigEntry<float> MinIntervalSeconds;
    internal static ConfigEntry<bool> LogSkips;

    public override void Load()
    {
        Logger = Log;
        MinIntervalSeconds = Config.Bind("General", "MinIntervalSeconds", 300f,
            "Minimum seconds between real Resources.UnloadUnusedAssets() calls requested by the game (0 = never skip).");
        LogSkips = Config.Bind("Debug", "LogSkips", true, "Log a running count of skipped unload requests.");
        new Harmony(Guid).PatchAll(typeof(UnloadPatch));
    }
}

[HarmonyPatch(typeof(Resources), nameof(Resources.UnloadUnusedAssets))]
internal static class UnloadPatch
{
    private static float lastUnload = float.NegativeInfinity;
    private static int skipped, allowed;

    private static bool Prefix(ref AsyncOperation __result)
    {
        var now = Time.realtimeSinceStartup;
        var interval = Plugin.MinIntervalSeconds.Value;
        if (interval > 0f && now - lastUnload < interval)
        {
            __result = null;
            skipped++;
            if (Plugin.LogSkips.Value && (skipped <= 3 || skipped % 25 == 0))
                Plugin.Logger.LogInfo($"Skipped Resources.UnloadUnusedAssets ({skipped} skipped, {allowed} allowed)");
            return false;
        }
        lastUnload = now;
        allowed++;
        Plugin.Logger.LogInfo($"Allowed Resources.UnloadUnusedAssets ({skipped} skipped, {allowed} allowed)");
        return true;
    }
}
