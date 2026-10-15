# Tower Dominion Leak Fix

An unofficial community [BepInEx](https://github.com/BepInEx/BepInEx) mod pack that fixes memory leaks,
silent sounds and stutters in **Tower Dominion**. Without it, long games use more and more memory, some UI sounds
stop playing, and selecting or deselecting a building causes a visible hitch.

> Not made or endorsed by Parallel 45 Games, Broken Spear or Nvizzio Creations. Free, open source, and it
> changes no gameplay. It only cleans up objects the game has already discarded and removes redundant work.

Tested with Tower Dominion Steam build **22911335** (Unity 2022.3.62f2) and BepInEx **6.0.0-be.788** (IL2CPP).

## What it fixes

### 1. Enemy objects are never freed between waves (`EnemyPoolLeakFix`)

At each wave, the game resizes its enemy object pools. A pool for an enemy type that isn't in the next wave is
shrunk to zero. The pool removes the idle enemies from its bookkeeping and asks each one to destroy itself, but
the enemy's destroy method is empty. Those enemies stay in memory for the rest of the session, along with their
visual-effect slots, materials and audio sources. The next wave then creates brand-new enemies instead of reusing them.

The mod destroys the objects a pool resize discards. This covers enemies and any other pooled object with the same
empty destroy method.

| 26–38 min test session | Without the fix | With the fix |
|---|---|---|
| Enemy objects in memory | 223 → **2,516**, climbing steadily | rises and falls with wave size (peaks ~600, settles ~240) |
| Unity native memory at 25 min | 436 MB | 396 MB |
| Orphaned enemies cleaned up | n/a | 3,266 in 38 min, no errors |

### 2. Duplicate unlock-condition listeners (`UnlockConditionLeakFix`)

Each unlock-condition record subscribes to the game's internal message system when it's created, and only
unsubscribes once the condition completes. Loading the save also reads a backup copy of the unlock data from the
Windows registry (PlayerPrefs), which creates a second full set of condition records. That set is discarded but
stays subscribed. Every relayed message is then delivered to both copies for the rest of the session. A save
with 130 conditions ends up with 260 subscribers.

The mod unsubscribes condition records that aren't part of the live save data, after each load and every
30 seconds as a safety net.

### 3. Sounds stop playing after a while (`AudioInstanceLeakFix`)

Each sound clip has a "currently playing" counter, capped at 5 simultaneous instances. The counter goes up when a
sound starts and only comes back down in the sound's finish routine. If the object playing the sound is hidden or
destroyed mid-sound (very common for UI buttons and panels), that routine never runs and the count stays up
forever. Once a clip reaches 5 it's silently skipped for the rest of the session. The decrement also uses
whichever clip the source holds at stop time, so counters can drift negative.

The mod remembers which clip each play counted, releases each count exactly once (never below zero), and every second
releases counts left behind by sounds that were destroyed, hidden, or stopped without finishing.

| 41 min without the fix | Stuck at the cap (silent) |
|---|---|
| Click, tower-select, reinforcement, rotate-zone, zone-hover, hover sounds | 5/5 (reroll was at 2/5 and climbing) |
| With the fix | all counters back to 0 between plays |

### 4. Building select stutter and recurring ~66 ms hitches (`ManagedDataCacheFix`)

The game's settings-data accessor (`ManagedDataHandler.DataResources`) has no cache check. Every read reloads the
data asset with `Resources.Load` and walks every data entry to prune destroyed ones, about 5 ms per read. Opening the
building panel reads it around 11 times (once per upgrade tile and per sell label), and other systems read it during play.

The mod returns the already-loaded data. The original load and cleanup still run on first use, after an unload,
and once after each scene load.

| | Without the fix | With the fix |
|---|---|---|
| Building panel open (`BuildingView.Initialize`) | 40–65 ms, plus a 15–19 ms second pass | **1–2 ms** |
| Frames ≥ 50 ms in the same ~4.5 min of play | 21 (20 of them ~66 ms) | **0** |

### 5. Hitch after deselecting a building (`AssetUnloadThrottleFix`)

The game's view manager calls `Resources.UnloadUnusedAssets()` every time it closes a UI view (the building panel,
popups and so on): 26 times in 4 minutes. Each call makes Unity scan about 70,000 loaded objects. The expensive part
runs on the main thread a few frames later as a 20–30 ms hitch.

The mod allows one real unload at most every 5 minutes and skips the rest. The only caller ignores the result.
Unity's own cleanup on scene changes is unaffected.

| Gameplay hitches | Without the fix | With the fix |
|---|---|---|
| Frames ≥ 25 ms per minute | 6.8 | **1.1** |
| Worst frame | 214 ms | **45 ms** |

## Install

1. Download a zip from [Releases](../../releases):
   - `…-with-BepInEx.zip`: includes BepInEx 6. Use this if you don't have BepInEx yet.
   - `…-plugins.zip`: only the five plugin DLLs, for an existing BepInEx 6 (IL2CPP) install.
2. Steam: right-click Tower Dominion, then **Manage → Browse local files**. Extract the zip into that folder, next to `tower-dominion.exe`.
3. Launch normally. The first BepInEx launch takes a minute or two.
4. To check it loaded, look for `Loading [Enemy Pool Leak Fix` in `BepInEx/LogOutput.log`.

Each fix is a separate DLL, so you can leave out any you don't want. Uninstall by deleting the DLLs from
`BepInEx/plugins` (`EnemyPoolLeakFix`, `UnlockConditionLeakFix`, `AudioInstanceLeakFix`, `ManagedDataCacheFix`,
`AssetUnloadThrottleFix`). To remove BepInEx too, delete `winhttp.dll`,
`doorstop_config.ini`, `.doorstop_version`, `changelog.txt`, `dotnet/` and `BepInEx/`.

## Configuration

Settings are written on first launch to `BepInEx/config/community.towerdominion.*.cfg`:

| File | Setting | Default | Meaning |
|---|---|---|---|
| `…enemypoolleakfix.cfg` | `LogDestroyed` | `true` | Log how many orphaned objects each pool resize cleaned up |
| `…unlockconditionleakfix.cfg` | `SweepSeconds` | `30` | Seconds between safety sweeps (0 = only after loads) |
| `…audioinstanceleakfix.cfg` | `SweepSeconds` | `1` | Seconds between sweeps for orphaned sound counts |
| `…audioinstanceleakfix.cfg` | `SilentGraceSeconds` | `5` | Release a count if its sound has been silent this long while still "playing" |
| `…audioinstanceleakfix.cfg` | `LogReleases` | `true` | Log released sound counts |
| `…manageddatacachefix.cfg` | `RefreshSeconds` | `0` | Also re-run the original load and cleanup this often (0 = only on first use, unload, scene load) |
| `…assetunloadthrottlefix.cfg` | `MinIntervalSeconds` | `300` | Minimum seconds between real asset unloads (0 = never skip) |
| `…assetunloadthrottlefix.cfg` | `LogSkips` | `true` | Log a running count of skipped unloads |

## Compatibility

- The fixes patch the game's methods by name, so small game updates usually don't break them.
  Bigger refactors can, and if the developers fix these bugs upstream the mod becomes unnecessary.
- Doesn't touch saves, gameplay values or online features.
- `AssetUnloadThrottleFix` keeps assets from closed views in memory up to 5 minutes longer before they're freed.
  Lower `MinIntervalSeconds` if memory is tight.

## Building

Requirements: .NET SDK 6.0+, and a game install with BepInEx 6 (IL2CPP) that has been launched once, so
`BepInEx/interop/` exists. The plugins compile against those generated assemblies, which are not included here.

```bash
# point the build at your game install (default: the standard Steam path)
export TOWER_DOMINION_DIR="/path/to/Tower Dominion"
dotnet build -c Release                    # build
dotnet build -c Release -p:Deploy=true     # build and copy into BepInEx/plugins
python3 scripts/package.py                 # build both release zips into dist/
```

## License

[MIT](LICENSE) for the code in this repository. The `-with-BepInEx` release zip redistributes unmodified
BepInEx binaries under their own license (LGPL-2.1, included as `BepInEx/LICENSE-BepInEx.txt`). Tower Dominion
and all its code and assets belong to their respective owners. This repository contains none of them.
