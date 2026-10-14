using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UPool;
using Object = UnityEngine.Object;

namespace EnemyPoolLeakFix;

// Every wave, EnemySwarmService.FillEnemyPools -> CleanLastPoolOfEnemies -> PoolManager.SetPoolCountTo resizes
// the enemy pools (to 0 for enemy types absent from the new wave). AbstractPool.SetPoolSize/ShrinkPool drop idle
// objects from the pool and call IPoolable.Destroy() on them, but CharacterEntity.Destroy() is an empty method,
// so the enemy GameObjects (with their VFX slots, materials and audio sources) are orphaned in the scene forever.
//
// This plugin destroys the GameObjects that a pool resize removed from the pool (enemies and any other pooled component).
// (CharacterEntity.Destroy itself can't be patched: its native body is a `ret` stub shared by ~4,400 methods.)
[BepInPlugin(Guid, "Enemy Pool Leak Fix", "1.1.0")]
public class Plugin : BasePlugin
{
    public const string Guid = "community.towerdominion.enemypoolleakfix";

    internal static ManualLogSource Logger;
    internal static ConfigEntry<bool> LogDestroyed;
    internal static long TotalDestroyed;

    public override void Load()
    {
        Logger = Log;
        LogDestroyed = Config.Bind("Debug", "LogDestroyed", true, "Log how many orphaned enemies each pool resize destroys.");
        new Harmony(Guid).PatchAll(typeof(PoolPatches));
    }
}

[HarmonyPatch]
internal static class PoolPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(AbstractPool), nameof(AbstractPool.SetPoolSize))]
    [HarmonyPatch(typeof(AbstractPool), "ShrinkPool")]
    private static void Before(AbstractPool __instance, int targetSize, out Dictionary<IntPtr, IPoolable> __state)
    {
        __state = null;
        var pool = __instance._pool;
        if (pool == null || pool.Count <= targetSize)
            return; // growing or unchanged: nothing is dropped

        __state = new Dictionary<IntPtr, IPoolable>();
        foreach (var poolable in pool)
            if (poolable != null) __state[poolable.Pointer] = poolable;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AbstractPool), nameof(AbstractPool.SetPoolSize))]
    [HarmonyPatch(typeof(AbstractPool), "ShrinkPool")]
    private static void After(AbstractPool __instance, Dictionary<IntPtr, IPoolable> __state)
    {
        if (__state == null)
            return;

        var stillPooled = new HashSet<IntPtr>();
        foreach (var poolable in __instance._pool)
            if (poolable != null) stillPooled.Add(poolable.Pointer);

        var destroyed = 0;
        foreach (var (ptr, poolable) in __state)
        {
            if (stillPooled.Contains(ptr))
                continue;
            // Every IPoolable seen so far (CharacterEntity, AreaOfEffect, ...) has the same empty Destroy(),
            // so clean up any pooled Unity component the resize dropped, not only enemies.
            var component = poolable.TryCast<UnityEngine.Component>();
            if (component == null) // Unity null check: also skips already-destroyed objects
                continue;
            Object.Destroy(component.gameObject);
            destroyed++;
        }

        Plugin.TotalDestroyed += destroyed;
        if (destroyed > 0 && Plugin.LogDestroyed.Value)
            Plugin.Logger.LogInfo($"Destroyed {destroyed} orphaned pooled objects from pool shrink (total {Plugin.TotalDestroyed})");
    }
}
