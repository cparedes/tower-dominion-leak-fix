// Loader shim shared by every fix: one source, built for BepInEx 6 (IL2CPP) or MelonLoader (0.6+/0.7) via
// -p:Loader=BepInEx|MelonLoader. Each fix implements IFix; the loader-specific entry point lives in this file.
using System;
using HarmonyLib;
#if BEPINEX
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
#else
using MelonLoader;
#endif

namespace TowerDominionFixes;

public interface IFix
{
    /// <summary>Harmony id and config key (e.g. "community.towerdominion.enemypoolleakfix").</summary>
    string Id { get; }
    /// <summary>Binds settings and applies patches. Called once at load.</summary>
    void Init(HarmonyLib.Harmony harmony);
    /// <summary>True if Tick() should run every frame.</summary>
    bool NeedsTick { get; }
    /// <summary>Per-frame work (main thread).</summary>
    void Tick();
}

/// <summary>A config value that reads through to the loader's config system.</summary>
public sealed class Setting<T>
{
    private readonly Func<T> read;
    internal Setting(Func<T> read) => this.read = read;
    public T Value => read();
}

internal static class Host
{
#if BEPINEX
    private static ManualLogSource log;
    private static ConfigFile config;

    internal static void Attach(ManualLogSource logSource, ConfigFile configFile)
    {
        log = logSource;
        config = configFile;
    }

    internal static Setting<T> Bind<T>(string section, string key, T defaultValue, string description)
    {
        var entry = config.Bind(section, key, defaultValue, description);
        return new Setting<T>(() => entry.Value);
    }

    internal static void Info(string message) => log.LogInfo(message);
    internal static void Warning(string message) => log.LogWarning(message);
#else
    private static MelonLogger.Instance log;
    private static MelonPreferences_Category category;

    internal static void Attach(MelonLogger.Instance logger, string id, string displayName)
    {
        log = logger;
        category = MelonPreferences.CreateCategory(id.Replace('.', '_'), displayName);
    }

    internal static Setting<T> Bind<T>(string section, string key, T defaultValue, string description)
    {
        var entry = category.CreateEntry($"{section}_{key}", defaultValue, $"{section} / {key}", description);
        return new Setting<T>(() => entry.Value);
    }

    internal static void Info(string message) => log.Msg(message);
    internal static void Warning(string message) => log.Warning(message);
#endif
}

#if BEPINEX
/// <summary>BepInEx entry: bind, patch, and (if needed) attach TDriver to call Tick() every frame.</summary>
public abstract class BepInExEntry<TFix, TDriver> : BasePlugin
    where TFix : IFix, new()
    where TDriver : UnityEngine.MonoBehaviour
{
    internal static TFix Fix;

    public override void Load()
    {
        Host.Attach(Log, Config);
        Fix = new TFix();
        Fix.Init(new HarmonyLib.Harmony(Fix.Id));
        if (Fix.NeedsTick)
            AddComponent<TDriver>();
    }
}
#else
/// <summary>MelonLoader entry: bind, patch, and call Tick() from OnUpdate.</summary>
public abstract class MelonEntry<TFix> : MelonMod where TFix : IFix, new()
{
    private TFix fix;
    private bool ticking;

    public override void OnInitializeMelon()
    {
        fix = new TFix();
        Host.Attach(LoggerInstance, fix.Id, Info.Name);
        fix.Init(new HarmonyLib.Harmony(fix.Id));
        ticking = fix.NeedsTick;
    }

    public override void OnUpdate()
    {
        if (ticking) fix.Tick();
    }
}
#endif
