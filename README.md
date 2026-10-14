# Tower Dominion Leak Fix

An unofficial community [BepInEx](https://github.com/BepInEx/BepInEx) mod that fixes two memory leaks in
**Tower Dominion**. These leaks make long games use more and more memory and stutter more as they go on.

> Not made or endorsed by Parallel 45 Games, Broken Spear or Nvizzio Creations. Free, open source, and it
> changes no gameplay. It only cleans up objects the game has already discarded.

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

## Install

1. Download a zip from [Releases](../../releases):
   - `…-with-BepInEx.zip`: includes BepInEx 6. Use this if you don't have BepInEx yet.
   - `…-plugins.zip`: only the two plugin DLLs, for an existing BepInEx 6 (IL2CPP) install.
2. Steam: right-click Tower Dominion, then **Manage → Browse local files**. Extract the zip into that folder, next to `tower-dominion.exe`.
3. Launch normally. The first BepInEx launch takes a minute or two.
4. To check it loaded, look for `Loading [Enemy Pool Leak Fix` in `BepInEx/LogOutput.log`.

Uninstall by deleting the two DLLs from `BepInEx/plugins`. To remove BepInEx too, delete `winhttp.dll`,
`doorstop_config.ini`, `.doorstop_version`, `changelog.txt`, `dotnet/` and `BepInEx/`.

## Configuration

Settings are written on first launch to `BepInEx/config/community.towerdominion.*.cfg`:

| File | Setting | Default | Meaning |
|---|---|---|---|
| `…enemypoolleakfix.cfg` | `LogDestroyed` | `true` | Log how many orphaned objects each pool resize cleaned up |
| `…unlockconditionleakfix.cfg` | `SweepSeconds` | `30` | Seconds between safety sweeps (0 = only after loads) |

## Compatibility

- The fixes patch the game's methods by name, so small game updates usually don't break them.
  Bigger refactors can, and if the developers fix these bugs upstream the mod becomes unnecessary.
- Doesn't touch saves, gameplay values or online features.

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
