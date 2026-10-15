# Changelog

## 1.3.0
- MelonLoader support: every fix is now also built as a MelonLoader mod (`…-melonloader.zip`, DLLs go in `Mods/`),
  tested with MelonLoader 0.7.3. Settings live in `UserData/MelonPreferences.cfg`.
- Internal: fixes share one source per loader through `src/Common/Loader.cs`. No behaviour changes on BepInEx.

## 1.2.0
- AudioInstanceLeakFix: sounds no longer go silent in long sessions (stuck per-clip instance counters).
- ManagedDataCacheFix: cache the game's settings data instead of reloading and walking it on every read. Removes
  the building-select stutter (40–65 ms → 1–2 ms) and recurring ~66 ms hitches.
- AssetUnloadThrottleFix: rate-limit `Resources.UnloadUnusedAssets` (called on every view close) to once per 5 min.
  Removes the 20–30 ms hitch after deselecting a building.

## 1.1.0
- EnemyPoolLeakFix: clean up any pooled Unity object dropped by a pool resize, not only enemies.
  Other pooled types (e.g. area-of-effect objects) have the same empty destroy method.

## 1.0.0
- EnemyPoolLeakFix: destroy enemy objects that per-wave pool resizes discard instead of leaking them.
- UnlockConditionLeakFix: unsubscribe duplicate unlock-condition records created when the save and its
  PlayerPrefs backup are both loaded.
