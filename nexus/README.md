# Nexus Mods upload kit: Tower Dominion Leak Fix 1.4.0

Everything needed to fill in the Nexus "Add a mod" form and the mod page. The page text and images are maintained
by hand on the website. File uploads for new releases are automated by
[`.github/workflows/nexus-upload.yml`](../.github/workflows/nexus-upload.yml): publishing a GitHub release uploads its
three zips as new versions of the existing file entries (or run it from the Actions tab with a tag).

| File | Use it for |
|---|---|
| `summary.txt` | **Brief overview** field (224 characters) |
| `description.bbcode` | **Detailed description**: paste as-is, it's Nexus BBCode |
| `thumbnail.png` | **Primary image** (1280×720), rendered from `thumbnail.html` |
| `header.png` | **Header image** (1300×372), rendered from `header.html`. `header-clean.png` is the same without text (`header.html#clean`), for when Nexus overlays the mod name |
| `changelog.txt` | **Changelog** section, one entry per version |
| `../dist/TowerDominion-LeakFix-1.4.0-*.zip` | the files to upload (see below) |

## 0. Game page

If Tower Dominion has no Nexus game page yet: on **Add a mod**, type "Tower Dominion". When nothing matches, click
**ADD NEW GAME**, enter the official title "Tower Dominion", then continue the upload. Staff review new games, and
approval isn't guaranteed. If it has been more than 7 business days, email support@nexusmods.com.

## 1. Mod details

| Field | Value |
|---|---|
| Mod name | Tower Dominion Leak Fix (Stutters, Silent Sounds, Memory Leaks) |
| Version | 1.4.0 |
| Category | Bug Fixes (or the closest equivalent on a new game page; else Utilities) |
| Language | English |
| Author | Ian Paredes |
| Brief overview | contents of `summary.txt` |
| Detailed description | contents of `description.bbcode` |
| Primary image | `thumbnail.png` |
| Header image | `header.png` (or `header-clean.png`) |

## 2. Tags

- **AI-generated content.** Required: Nexus's guidelines treat AI-written code as AI-generated content, and
  wrong tagging can be moderated. Pick the tag closest to "AI-generated content" in the tag list.
- Useful descriptive tags if offered: Bug Fixes, Performance, Audio, Utilities.

## 3. Requirements (off-site)

| Name | URL | Notes |
|---|---|---|
| BepInEx 6 (IL2CPP, win-x64) | https://builds.bepinex.dev/projects/bepinex_be | For the BepInEx version (tested be.788). Not needed if using the bundle file. |
| MelonLoader | https://github.com/LavaGang/MelonLoader/releases | For the MelonLoader version (0.6+, tested 0.7.3) |

Add a line to both: "Use one loader, not both."

## 4. Files tab

Upload three files under **Main files**, all version 1.4.0:

| Display name | File | Description (paste) |
|---|---|---|
| MelonLoader version | `TowerDominion-LeakFix-1.4.0-melonloader.zip` | For MelonLoader 0.6+ (tested 0.7.3). Install MelonLoader, launch the game once, then extract this into the game folder (DLLs go into Mods/). |
| BepInEx version | `TowerDominion-LeakFix-1.4.0-plugins.zip` | For an existing BepInEx 6 IL2CPP install (tested 6.0.0-be.788). Extract into the game folder (DLLs go into BepInEx/plugins/). |
| BepInEx bundle (includes BepInEx 6) | `TowerDominion-LeakFix-1.4.0-with-BepInEx.zip` | BepInEx 6.0.0-be.788 (IL2CPP, LGPL-2.1, license included) plus the six fixes. Extract into the game folder. Use this if you don't have a mod loader yet. |

The bundle is optional. If you'd rather not redistribute BepInEx, skip it and rely on the off-site requirement link.

## 5. Permissions

- **Credits:** Claude (Anthropic), via Claude Code: code and research. BepInEx team (bundle only). Harmony/HarmonyX
  and Il2CppInterop (used at runtime, not redistributed).
- **Permissions:** open: the code is MIT-licensed, so others may modify, re-upload and use it in their mods with credit.
  Point to the GitHub repo for source.
- **Source link / mod page link:** https://github.com/cparedes/tower-dominion-leak-fix

## 6. After publishing

- Add the Nexus page link to the GitHub README and release notes, and to the Steam forum thread.
- When releasing a new version, upload new files and move the old ones to **Old versions**.
