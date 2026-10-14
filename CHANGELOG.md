# Changelog

## 1.1.0
- EnemyPoolLeakFix: clean up any pooled Unity object dropped by a pool resize, not only enemies.
  Other pooled types (e.g. area-of-effect objects) have the same empty destroy method.

## 1.0.0
- EnemyPoolLeakFix: destroy enemy objects that per-wave pool resizes discard instead of leaking them.
- UnlockConditionLeakFix: unsubscribe duplicate unlock-condition records created when the save and its
  PlayerPrefs backup are both loaded.
