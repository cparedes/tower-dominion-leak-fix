using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Nvizzio.Core.ManagedData;
using UnityEngine;

namespace ManagedDataCacheFix;

// ManagedDataHandler.DataResources (getter RVA 0x57F300) never checks its cached static field: every access does
// Path.Combine + Resources.Load<ManagedDataResources>(...) and then ManagedDataResources.Cleanup(), which walks
// every ManagedDataRoot/ManagedSubData entry doing a native Unity null check. The game reads DataResources for
// every settings lookup (e.g. CurrencySettingsData in each sell label / upgrade item), costing ~5 ms per access:
// selecting a building does it ~11 times, a 40-65 ms stutter.
//
// This plugin returns the already-loaded instance and only lets the original (load + cleanup) run on first use,
// after the asset is unloaded, or once after each scene load so destroyed entries still get pruned.
[BepInPlugin(Guid, "Managed Data Cache Fix", "1.0.0")]
public class Plugin : BasePlugin
{
    public const string Guid = "community.towerdominion.manageddatacachefix";

    internal static ManualLogSource Logger;
    internal static ConfigEntry<float> RefreshSeconds;

    public override void Load()
    {
        Logger = Log;
        RefreshSeconds = Config.Bind("General", "RefreshSeconds", 0f,
            "Also let the original getter (Resources.Load + Cleanup) run at most this often. 0 = only on first use, " +
            "after the asset is unloaded, and after each scene load (when destroyed entries can actually appear).");
        new Harmony(Guid).PatchAll(typeof(DataResourcesPatch));
        UnityEngine.SceneManagement.SceneManager.sceneLoaded +=
            (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)
            new System.Action<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>(
                (scene, mode) => DataResourcesPatch.Invalidate());
    }
}

[HarmonyPatch(typeof(ManagedDataHandler), nameof(ManagedDataHandler.DataResources), MethodType.Getter)]
internal static class DataResourcesPatch
{
    private static ManagedDataResources cached;
    private static float lastRefresh = float.NegativeInfinity;
    private static long served, refreshed;

    // Next access runs the original (reload + Cleanup) once, pruning entries destroyed by the scene change.
    internal static void Invalidate() => cached = null;

    private static bool Prefix(ref ManagedDataResources __result)
    {
        var refresh = Plugin.RefreshSeconds.Value;
        var due = refresh > 0f && Time.realtimeSinceStartup - lastRefresh >= refresh;
        if (cached != null && !due) // Unity null check: also false if the asset was unloaded
        {
            __result = cached;
            served++;
            return false;
        }
        return true;
    }

    // HarmonyX runs postfixes even when the prefix skipped the original; only act when the original ran.
    private static void Postfix(ManagedDataResources __result, bool __runOriginal)
    {
        if (!__runOriginal || __result == null) return;
        if (cached == null || __result.Pointer != cached.Pointer)
            Plugin.Logger.LogInfo($"Caching ManagedDataResources ({served} cached reads, {refreshed} refreshes so far)");
        cached = __result;
        lastRefresh = Time.realtimeSinceStartup;
        refreshed++;
    }
}
