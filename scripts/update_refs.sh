#!/usr/bin/env bash
# Refreshes the private build-reference repo (cparedes/tower-dominion-refs) that CI compiles against.
#
# Usage: scripts/update_refs.sh [GAME_DIR] [MELONLOADER_REFS_DIR]
#   GAME_DIR              game install with BepInEx 6 run at least once (BepInEx/core, BepInEx/interop)
#   MELONLOADER_REFS_DIR  folder containing MelonLoader/net6 and MelonLoader/Il2CppAssemblies from a MelonLoader
#                         install that has run once (the game folder itself while MelonLoader is installed)
#
# Copies only the DLLs listed in Directory.Build.props. The interop assemblies are generated from the game, so the
# refs repo must stay private. Run after a game, BepInEx or MelonLoader update, then re-test before tagging.
set -euo pipefail
GAME_DIR=${1:-"/mnt/c/Program Files (x86)/Steam/steamapps/common/Tower Dominion"}
ML_DIR=${2:-"$HOME/workspace/tower-dominion-re/refs/melonloader-0.7.3"}
REPO=${REFS_REPO:-cparedes/tower-dominion-refs}
INTEROP=(Assembly-CSharp.dll Il2Cppmscorlib.dll Il2CppSystem.Core.dll UnityEngine.CoreModule.dll UnityEngine.AudioModule.dll)

work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
gh repo clone "$REPO" "$work/refs" -- --quiet
cd "$work/refs"
rm -rf BepInEx MelonLoader
mkdir -p BepInEx/core BepInEx/interop MelonLoader/net6 MelonLoader/Il2CppAssemblies
for f in BepInEx.Core.dll BepInEx.Unity.IL2CPP.dll 0Harmony.dll Il2CppInterop.Runtime.dll; do cp "$GAME_DIR/BepInEx/core/$f" BepInEx/core/; done
for f in MelonLoader.dll 0Harmony.dll Il2CppInterop.Runtime.dll; do cp "$ML_DIR/MelonLoader/net6/$f" MelonLoader/net6/; done
for f in "${INTEROP[@]}"; do
  cp "$GAME_DIR/BepInEx/interop/$f" BepInEx/interop/
  cp "$ML_DIR/MelonLoader/Il2CppAssemblies/$f" MelonLoader/Il2CppAssemblies/
done

cat > README.md <<README
# tower-dominion-refs (private)

Build references for [tower-dominion-leak-fix](https://github.com/cparedes/tower-dominion-leak-fix) CI. The interop
assemblies are generated from Tower Dominion's binaries, so **keep this repository private**.

Layout matches the leak-fix build: \`-p:GameDir=<this repo>\` (BepInEx) and \`-p:MelonLoaderRefs=<this repo>\` (MelonLoader).
Refresh with \`scripts/update_refs.sh\` in the leak-fix repo.

| | |
|---|---|
| Updated | $(date -u +%Y-%m-%dT%H:%MZ) |
| Steam build | ${STEAM_BUILD:-22911335} |
| BepInEx | $(grep -o '6\.0\.0-be\.[0-9]*' "$GAME_DIR/BepInEx/LogOutput.log" 2>/dev/null | head -1 || echo unknown) |
| MelonLoader | $(basename "$ML_DIR") |
README
(cd BepInEx && sha256sum core/* interop/*) > BepInEx/SHA256SUMS
(cd MelonLoader && sha256sum net6/* Il2CppAssemblies/*) > MelonLoader/SHA256SUMS

git add -A
if git diff --cached --quiet; then echo "refs already up to date"; exit 0; fi
git -c user.name="$(git -C "$OLDPWD" config user.name 2>/dev/null || echo refs)" \
    -c user.email="$(git -C "$OLDPWD" config user.email 2>/dev/null || echo refs@localhost)" \
    commit -q -m "Update build references ($(date -u +%Y-%m-%d))"
git push -q origin HEAD
git log --oneline -1
