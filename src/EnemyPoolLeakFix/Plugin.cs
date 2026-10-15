using System;
using System.Collections.Generic;
using HarmonyLib;
using TowerDominionFixes;
using Object = UnityEngine.Object;
#if MELONLOADER
using Il2CppUPool;
#else
using UPool;
#endif

#if MELONLOADER
[assembly: MelonLoader.MelonInfo(typeof(EnemyPoolLeakFix.Mod), EnemyPoolLeakFix.Info.Name, EnemyPoolLeakFix.Info.Version, "Tower Dominion community")]
[assembly: MelonLoader.MelonGame("Parallel45", "tower-dominion")]
[assembly: MelonLoader.HarmonyDontPatchAll]
#endif

namespace EnemyPoolLeakFix;

#if BEPINEX
[BepInEx.BepInPlugin(Info.Guid, Info.Name, Info.Version)]
public class Plugin : TowerDominionFixes.BepInExEntry<Fix, Driver> { }
public class Driver : UnityEngine.MonoBehaviour { public Driver(System.IntPtr ptr) : base(ptr) { } }
#else
public class Mod : TowerDominionFixes.MelonEntry<Fix> { }
#endif

internal static class Info
{
    public const string Guid = "community.towerdominion.enemypoolleakfix";
    public const string Name = "Enemy Pool Leak Fix";
    public const string Version = "1.3.0";
}

// Every wave, EnemySwarmService.FillEnemyPools -> CleanLastPoolOfEnemies -> PoolManager.SetPoolCountTo resizes
// the enemy pools (to 0 for enemy types absent from the new wave). AbstractPool.SetPoolSize/ShrinkPool drop idle
// objects from the pool and call IPoolable.Destroy() on them, but CharacterEntity.Destroy() is an empty method,
// so the enemy GameObjects (with their VFX slots, materials and audio sources) are orphaned in the scene forever.
//
// This fix destroys the GameObjects that a pool resize removed from the pool (enemies and any other pooled component).
// (CharacterEntity.Destroy itself can't be patched: its native body is a `ret` stub shared by ~4,400 methods.)
public class Fix : IFix
{
    public string Id => Info.Guid;
    public bool NeedsTick => false;
    public void Tick() { }

    internal static Setting<bool> LogDestroyed;
    internal static long TotalDestroyed;

    public void Init(HarmonyLib.Harmony harmony)
    {
        LogDestroyed = Host.Bind("Debug", "LogDestroyed", true, "Log how many orphaned objects each pool resize destroys.");
        harmony.PatchAll(typeof(PoolPatches));
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

        Fix.TotalDestroyed += destroyed;
        if (destroyed > 0 && Fix.LogDestroyed.Value)
            Host.Info($"Destroyed {destroyed} orphaned pooled objects from pool shrink (total {Fix.TotalDestroyed})");
    }
}
