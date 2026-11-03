"""Prepare local ModDB + mirrored author patch feeds, without publishing anything.

The author's RAR remains byte-identical inside the small mirrored ZIP. The
existing published module manifest is retained, proving installed file parity.
"""
import argparse
import base64
import hashlib
import json
import subprocess
import zipfile
from pathlib import Path

BASE_SHA = "c0e279d88b10ffbe4ebddbc361e694c89413d1109147df8fdab911f330c0df07"
PATCH_SHA = "674c6ed16bae670dd94b978e3250e02a1791db57e9d016efdf70949d8ccd791c"
START = "https://www.moddb.com/downloads/start/249627"
PAGE = "https://www.moddb.com/mods/kohan-2-arcane-wars-v078beta/downloads/arcane-wars-082beta"


def sha(path):
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest().upper()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ("base", "patch", "reference", "output", "dotnet", "tests"):
        p.add_argument("--"+name, type=Path, required=True)
    a = p.parse_args()
    repo = Path(__file__).resolve().parents[1]
    a.output.mkdir(parents=True, exist_ok=True)
    if sha(a.base).lower() != BASE_SHA or sha(a.patch).lower() != PATCH_SHA:
        raise ValueError("Source archives differ from the reviewed author releases")
    channels = {}
    for channel in ("stable", "beta"):
        env = json.loads((repo / "feed" / "v2" / (channel+".json")).read_text())
        channels[channel] = json.loads(base64.b64decode(env["payload"]))
    package = next(x for x in channels["stable"]["packages"] if x["id"] == "arcane-wars")
    if sha(a.reference) != package["sha256"].upper():
        raise ValueError("Reference package differs from the repository's published catalog")
    with zipfile.ZipFile(a.reference) as ref:
        manifest = ref.read("module.json")
    bundle = a.output / "darquan-0.82.1.8-fix-v2-source.zip"
    credits = ("Arcane Wars and patch 0.82.1.8 fix v2 by Darquan Mortis.\n"
               "Original patch archive is included unchanged.\nBase mod: " + PAGE + "\n"
               "Author community: https://discord.gg/krCK7DDwyz\nLauncher packaging by Paw.\n")
    with zipfile.ZipFile(bundle, "w") as z:
        for name, data in (("module.json", manifest), ("darquan-patch.rar", a.patch.read_bytes()), ("credits.txt", credits.encode())):
            info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, data, compresslevel=9)
    assembled = a.output / "arcane-wars-source-assembled.zip"
    subprocess.run([str(a.dotnet), str(a.tests), "--build-arcane-sources", str(a.base), str(bundle), str(assembled)], check=True)
    # No fallback URL: old launchers must not silently download the monolithic
    # mirror and make an apparent ModDB install bypass the author's download.
    package["urls"] = []
    package["size"] = assembled.stat().st_size
    package["sha256"] = sha(assembled)
    package["sourceRecipe"] = {"kind": "arcane-moddb-v1",
        "base": {"url": START, "size": a.base.stat().st_size, "sha256": sha(a.base)},
        "patch": {"url": str(bundle.resolve()), "size": bundle.stat().st_size, "sha256": sha(bundle)}}
    def write(name, value):
        (a.output/name).write_text(json.dumps(value, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    write("package-live.json", package)
    for channel, manifest in channels.items():
        manifest["packages"] = [package if x["id"] == "arcane-wars" else x for x in manifest["packages"]]
        manifest["launcher"] = {"version": "0.0.0", "urls": []}  # test EXE must not replace itself
        write(channel+".json", manifest)
    local = json.loads(json.dumps(package))
    local["sourceRecipe"]["base"]["url"] = str(a.base.resolve())
    write("package-local.json", local)
    config = json.loads((repo/"src/PawsPatchLauncher/launcher.config.json").read_text())
    config["feedUrls"] = [str((a.output/"stable.json").resolve())]
    config["betaFeedUrls"] = [str((a.output/"beta.json").resolve())]
    config["cacheRoot"] = str((a.output/"test-cache").resolve())
    write("launcher.config.json", config)
    print("LOCAL ONLY: base from ModDB; author patch bundle from", bundle)


if __name__ == "__main__":
    main()
