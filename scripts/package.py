#!/usr/bin/env python3
"""Build the plugins and produce the two release zips in dist/.

  dist/TowerDominion-LeakFix-<version>-plugins.zip       for players who already have BepInEx 6 (IL2CPP)
  dist/TowerDominion-LeakFix-<version>-with-BepInEx.zip  BepInEx 6 be.788 + plugins; extract into the game folder
  dist/TowerDominion-LeakFix-<version>-melonloader.zip   the same fixes built for MelonLoader (Mods/*.dll)

Usage: python3 scripts/package.py [--bepinex-zip PATH] [--skip-build] [--melonloader-refs DIR]
Requires the .NET SDK and interop assemblies for both loaders (see README "Building").
"""
import argparse
import hashlib
import io
import re
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PLUGINS = ["EnemyPoolLeakFix", "UnlockConditionLeakFix", "AudioInstanceLeakFix", "ManagedDataCacheFix", "AssetUnloadThrottleFix"]

BEPINEX_URL = ("https://builds.bepinex.dev/projects/bepinex_be/788/"
               "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip")
BEPINEX_SHA256 = "f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a"
BEPINEX_LICENSE_URL = "https://raw.githubusercontent.com/BepInEx/BepInEx/master/LICENSE"


def version() -> str:
    props = (ROOT / "Directory.Build.props").read_text()
    return re.search(r"<Version>([^<]+)</Version>", props).group(1)


def fetch(url: str) -> bytes:
    print(f"downloading {url}")
    with urllib.request.urlopen(url) as response:
        return response.read()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bepinex-zip", type=Path, help="local copy of the BepInEx be.788 zip (skips download)")
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--melonloader-refs", type=Path,
                        help="folder with MelonLoader/net6 + MelonLoader/Il2CppAssemblies (default: the game install)")
    args = parser.parse_args()

    ver = version()
    if not args.skip_build:
        sln = str(ROOT / "TowerDominionLeakFix.sln")
        subprocess.run(["dotnet", "build", sln, "-c", "Release", "-p:Loader=BepInEx"], check=True)
        ml = ["dotnet", "build", sln, "-c", "Release", "-p:Loader=MelonLoader"]
        if args.melonloader_refs:
            ml.append(f"-p:MelonLoaderRefs={args.melonloader_refs}")
        subprocess.run(ml, check=True)

    def outputs(loader: str) -> dict:
        found = {}
        for name in PLUGINS:
            dll = ROOT / "src" / name / "bin" / loader / "Release" / f"{name}.dll"
            if not dll.exists():
                sys.exit(f"missing build output: {dll}")
            found[name] = dll.read_bytes()
        return found

    dlls = outputs("BepInEx")
    melon_dlls = outputs("MelonLoader")

    bepinex = args.bepinex_zip.read_bytes() if args.bepinex_zip else fetch(BEPINEX_URL)
    digest = hashlib.sha256(bepinex).hexdigest()
    if digest != BEPINEX_SHA256:
        sys.exit(f"BepInEx zip SHA-256 mismatch: {digest}")
    bepinex_license = fetch(BEPINEX_LICENSE_URL)

    dist = ROOT / "dist"
    dist.mkdir(exist_ok=True)
    readme = (ROOT / "docs" / "INSTALL.txt").read_text().replace("{version}", ver)
    license_text = (ROOT / "LICENSE").read_bytes()

    def add_plugins(z: zipfile.ZipFile) -> None:
        for name, data in dlls.items():
            z.writestr(f"BepInEx/plugins/{name}.dll", data)
        z.writestr("TowerDominion-LeakFix-README.txt", readme)
        z.writestr("TowerDominion-LeakFix-LICENSE.txt", license_text)

    plugins_zip = dist / f"TowerDominion-LeakFix-{ver}-plugins.zip"
    with zipfile.ZipFile(plugins_zip, "w", zipfile.ZIP_DEFLATED) as z:
        add_plugins(z)

    full_zip = dist / f"TowerDominion-LeakFix-{ver}-with-BepInEx.zip"
    with zipfile.ZipFile(full_zip, "w", zipfile.ZIP_DEFLATED) as z:
        with zipfile.ZipFile(io.BytesIO(bepinex)) as src:
            for item in src.infolist():
                if not item.is_dir():
                    z.writestr(item.filename, src.read(item))
        z.writestr("BepInEx/LICENSE-BepInEx.txt", bepinex_license)
        add_plugins(z)

    melon_zip = dist / f"TowerDominion-LeakFix-{ver}-melonloader.zip"
    with zipfile.ZipFile(melon_zip, "w", zipfile.ZIP_DEFLATED) as z:
        for name, data in melon_dlls.items():
            z.writestr(f"Mods/{name}.dll", data)
        z.writestr("TowerDominion-LeakFix-README.txt", readme)
        z.writestr("TowerDominion-LeakFix-LICENSE.txt", license_text)

    for path in (plugins_zip, full_zip, melon_zip):
        sha = hashlib.sha256(path.read_bytes()).hexdigest()
        print(f"{path.relative_to(ROOT)}  {path.stat().st_size:,} bytes  sha256 {sha}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
