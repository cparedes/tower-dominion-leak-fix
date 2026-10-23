Unofficial community fixes for **Tower Dominion**. No gameplay changes. Each fix is a separate DLL, so you can leave
out any you don't want. See the [README](https://github.com/cparedes/tower-dominion-leak-fix#readme) for what each fix
does, measurements and config options, and [CHANGELOG.md](https://github.com/cparedes/tower-dominion-leak-fix/blob/main/CHANGELOG.md)
for earlier versions. Also on [Nexus Mods](https://www.nexusmods.com/towerdominion/mods/8).

## Install (use one loader, not both)

Open the game folder: in Steam, right-click Tower Dominion, then **Manage → Browse local files**.

**BepInEx**
- **`TowerDominion-LeakFix-{version}-with-BepInEx.zip`**: BepInEx 6.0.0-be.788 (IL2CPP) plus the fixes. Extract into the game folder.
- **`TowerDominion-LeakFix-{version}-plugins.zip`**: just the fixes, for an existing BepInEx 6 (IL2CPP) install.

**MelonLoader**
- Install [MelonLoader](https://github.com/LavaGang/MelonLoader/releases) 0.6+ (tested with 0.7.3) and launch the game once.
- **`TowerDominion-LeakFix-{version}-melonloader.zip`**: extract into the game folder (the DLLs go into `Mods/`).
  Settings are in `UserData/MelonPreferences.cfg`.

Upgrading: extract over the old files. Uninstall by deleting the fix DLLs from `BepInEx/plugins` or `Mods/`. Not made
or endorsed by Parallel 45 Games, Broken Spear or Nvizzio Creations.

> **AI-generated:** the investigation, code and docs were produced with Claude (Anthropic) using Claude Code, directed and play-tested by Ian Paredes. See [How this was made](https://github.com/cparedes/tower-dominion-leak-fix#how-this-was-made).
