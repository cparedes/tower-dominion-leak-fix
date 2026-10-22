using HarmonyLib;
using TowerDominionFixes;
using UnityEngine;

#if MELONLOADER
[assembly: MelonLoader.MelonInfo(typeof(AssetUnloadThrottleFix.Mod), AssetUnloadThrottleFix.Info.Name, AssetUnloadThrottleFix.Info.Version, "Tower Dominion community")]
[assembly: MelonLoader.MelonGame("Parallel45", "tower-dominion")]
[assembly: MelonLoader.HarmonyDontPatchAll]
#endif

namespace AssetUnloadThrottleFix;

#if BEPINEX
[BepInEx.BepInPlugin(Info.Guid, Info.Name, Info.Version)]
public class Plugin : TowerDominionFixes.BepInExEntry<Fix, Driver> { }
public class Driver : UnityEngine.MonoBehaviour { public Driver(System.IntPtr ptr) : base(ptr) { } }
#else
public class Mod : TowerDominionFixes.MelonEntry<Fix> { }
#endif

internal static class Info
{
    public const string Guid = "community.towerdominion.assetunloadthrottlefix";
    public const string Name = "Asset Unload Throttle Fix";
    public const string Version = "1.4.0";
}

// Copper.ViewManager's ResourceViewLoader.DestroyView (RVA 0x88BC60) calls Resources.UnloadUnusedAssets() every
// time a view is destroyed (building panel on deselect, popups, tooltips...): 26 times in ~4 minutes of play.
// Each one makes Unity walk every loaded asset and reachable managed object (~70k objects); the expensive
// UnloadUnusedAssetsOperation::IntegrateMainThread -> GarbageCollectSharedAssets step lands on the main thread a
// few frames later as a 20-30 ms hitch. DestroyView discards the returned AsyncOperation.
//
// This fix lets the first call through and then at most one real unload per MinIntervalSeconds; calls in between
// are skipped (returning null, which the only caller ignores). Unity's own unloads on scene changes are unaffected.
public class Fix : IFix
{
    public string Id => Info.Guid;
    public bool NeedsTick => false;
    public void Tick() { }

    internal static Setting<float> MinIntervalSeconds;
    internal static Setting<bool> LogSkips;

    public void Init(HarmonyLib.Harmony harmony)
    {
        MinIntervalSeconds = Host.Bind("General", "MinIntervalSeconds", 300f,
            "Minimum seconds between real Resources.UnloadUnusedAssets() calls requested by the game (0 = never skip).");
        LogSkips = Host.Bind("Debug", "LogSkips", true, "Log a running count of skipped unload requests.");
        harmony.PatchAll(typeof(UnloadPatch));
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
        var interval = Fix.MinIntervalSeconds.Value;
        if (interval > 0f && now - lastUnload < interval)
        {
            __result = null;
            skipped++;
            if (Fix.LogSkips.Value && (skipped <= 3 || skipped % 25 == 0))
                Host.Info($"Skipped Resources.UnloadUnusedAssets ({skipped} skipped, {allowed} allowed)");
            return false;
        }
        lastUnload = now;
        allowed++;
        Host.Info($"Allowed Resources.UnloadUnusedAssets ({skipped} skipped, {allowed} allowed)");
        return true;
    }
}
