using System;
using System.Collections.Generic;
using HarmonyLib;
using TowerDominionFixes;
using UnityEngine;
#if MELONLOADER
using Il2CppNvizzio.Game.GamePlay.GameEntities;
using Il2CppNvizzio.Game.GamePlay.Vfx;
#else
using Nvizzio.Game.GamePlay.GameEntities;
using Nvizzio.Game.GamePlay.Vfx;
#endif

#if MELONLOADER
[assembly: MelonLoader.MelonInfo(typeof(AreaOfEffectLeakFix.Mod), AreaOfEffectLeakFix.Info.Name, AreaOfEffectLeakFix.Info.Version, "Tower Dominion community")]
[assembly: MelonLoader.MelonGame("Parallel45", "tower-dominion")]
[assembly: MelonLoader.HarmonyDontPatchAll]
#endif

namespace AreaOfEffectLeakFix;

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
    public const string Guid = "community.towerdominion.areaofeffectleakfix";
    public const string Name = "Area Of Effect Leak Fix";
    public const string Version = "1.4.0";
}

// When a bullet hits, BulletEntity.EntityUpdate spawns its AreaOfEffect (registered with EntityManager.AddEntity) and
// marks the bullet for removal. Removing a bullet (EntityManager.RemoveEntity) also removes bullet.aoe from
// EntityManager.entities. An AoE with an impact sound only marks itself for removal/deletion later, in
// AreaOfEffect.EntityUpdate once the sound has finished, but EntityManager.MarkForRemoval/MarkForDeletion ignore
// entities that aren't in `entities`, and GameEntity.MarkForDeletion sets its own flag first, so it never retries.
// Those AoEs (e.g. SlovekBombAoE, with HowitzerCannonImpactSound1) are never recycled: they stay active in the scene,
// keep their AudioSource's FMOD channel groups alive, and Unity's AudioMixer::Update scans those every frame in
// O(n^2), so FPS sinks as a session goes on. AoEs without a sound mark themselves immediately and aren't affected.
//
// This fix notes such an ignored deletion request and, from its own Update (never from inside the game's entity
// loops, which iterate these sets, e.g. EntityManager.PurgeRoundEntities at round end), hands the AoE back to the
// game's normal path: re-tracked, removed from the update set, deleted after the usual countdown and returned to
// its pool. AoEs the game has since cleaned up (IsDead, set by AreaOfEffect.CleanUp) or reused are left alone.
public class Fix : IFix
{
    public string Id => Info.Guid;
    public bool NeedsTick => true;

    internal static Setting<bool> LogRequeued;
    internal static readonly List<AreaOfEffect> Pending = new();
    internal static readonly HashSet<IntPtr> PendingPtrs = new();
    internal static EntityManager Manager;   // the instance that ignored the request (avoids the generic singleton getter)
    internal static long Requeued;
    private long logged;
    private float nextLog;

    public void Init(HarmonyLib.Harmony harmony)
    {
        LogRequeued = Host.Bind("Debug", "LogRequeued", true,
            "Log (at most once a minute) how many orphaned area-of-effect objects were handed back to the game for cleanup.");
        harmony.PatchAll(typeof(EntityPatches));
    }

    public void Tick()
    {
        if (Pending.Count > 0) Requeue();
        if (!LogRequeued.Value || Requeued == logged || Time.unscaledTime < nextLog) return;
        nextLog = Time.unscaledTime + 60f;
        Host.Info($"Re-queued {Requeued - logged} orphaned area-of-effect object(s) for cleanup (total {Requeued})");
        logged = Requeued;
    }

    private static void Requeue()
    {
        var manager = Manager;
        foreach (var aoe in Pending)
        {
            try
            {
                if (manager == null || !StillOrphaned(manager, aoe)) continue;
                manager.entities.Add(aoe);
                manager.MarkForRemoval(aoe);
                manager.MarkForDeletion(aoe);
                Requeued++;
            }
            catch (Exception e)
            {
                Host.Warning($"Could not re-queue an area-of-effect object: {e.Message}");
            }
        }
        Pending.Clear();
        PendingPtrs.Clear();
    }

    // Still asking to be deleted, not cleaned up or reused since, and still unknown to the manager.
    private static bool StillOrphaned(EntityManager manager, AreaOfEffect aoe)
    {
        if (aoe == null || aoe.IsDead || !aoe.markForDeletion) return false;
        return !manager.entities.Contains(aoe) && !manager.entitiesToRemove.Contains(aoe) && !manager.entitiesToDelete.Contains(aoe);
    }
}

[HarmonyPatch]
internal static class EntityPatches
{
    // Only records the request: the game may be enumerating its entity sets right now, so nothing is changed here.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityManager), nameof(EntityManager.MarkForDeletion))]
    private static void BeforeMarkForDeletion(EntityManager __instance, GameEntity entity)
    {
        try
        {
            if (entity == null) return;
            var aoe = entity.TryCast<AreaOfEffect>();
            if (aoe == null || aoe.IsDead || !PendingPtrs.Add(aoe.Pointer)) return;
            if (__instance.entities.Contains(entity) || __instance.entitiesToDelete.Contains(entity))
            {
                PendingPtrs.Remove(aoe.Pointer);
                return;
            }
            Fix.Manager = __instance;
            Fix.Pending.Add(aoe);
        }
        catch
        {
            // never let the fix break the game's own call
        }
    }

    private static HashSet<IntPtr> PendingPtrs => Fix.PendingPtrs;
}
