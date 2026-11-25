#!/usr/bin/env python3
"""Stage a PortMaster submission and create the install ZIP."""

import argparse
import json
from pathlib import Path
import stat
from zipfile import ZipFile, ZIP_DEFLATED, ZipInfo


PORT = Path(__file__).resolve().parents[1] / "port"
ID = "streetsofrage4"
LAUNCHER = "Streets of Rage 4.sh"
RUNTIME_VERSION = "9.0.22"
APP_FILES = {"sorage4host.dll", "Java.Interop.dll", "Mono.Android.dll",
             "Mono.Cecil.dll", "MonoGame.Framework.dll", "NVorbis.dll", "SORAGE4Bridge.dll",
             "StbImageSharp.dll", "StbImageWriteSharp.dll"}
METADATA = ("README.md", "port.json", "gameinfo.xml", "screenshot.png", "cover.png")
EXEC = {LAUNCHER, "sorage4", "display-probe", "repack.src", "setup-screen.src", "sorage-video"}
COMPILED_FILES = (
    "sorage4",
    *sorted("runtime.aarch64/" + name for name in APP_FILES | {"SorageVideo.dll"}),
    *("patches/" + name + ".dll" for name in ("coopsplit", "EnableVideo", "fixplatform", "MenuCompatibility", "patchgam", "skipcall")),
    *("libs.aarch64/" + name for name in ("libastc-repack.so", "libsorage4astc.so", "libWwise.so", "fallback/libharfbuzz.so.0")),
    "utils/display-probe",
    "utils/sorage-video",
)


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    if path.name in EXEC:
        path.chmod(0o755)


def bind_apphost(data):
    # The .NET apphost reserves a zero-padded slot for its managed entry point.
    replacement = b"runtime.aarch64/sorage4host.dll\0"
    original = b"sorage4host.dll".ljust(len(replacement), b"\0")
    if data.count(original) != 1:
        raise ValueError("The SDK apphost's entry-point slot differs")
    return data.replace(original, replacement, 1)


def write_runtime_config(app):
    config = {"runtimeOptions": {
        "tfm": "net9.0",
        "framework": {"name": "Microsoft.NETCore.App", "version": RUNTIME_VERSION},
        "rollForward": "LatestPatch",
        "configProperties": {
            "System.Reflection.Metadata.MetadataUpdater.IsSupported": False,
            "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": False,
        },
    }}
    write(app / "sorage4host.runtimeconfig.json", (json.dumps(config, indent=2) + "\n").encode())


def stage_compiled(compiled, stage):
    missing = [name for name in COMPILED_FILES if not (compiled / name).is_file()]
    if missing:
        raise ValueError("Missing compiled inputs: " + ", ".join(missing))
    stage.mkdir(parents=True)
    folder = stage / ID
    for name in COMPILED_FILES:
        data = (compiled / name).read_bytes()
        if name == "sorage4" or name.startswith(("libs.aarch64/", "utils/")):
            if data[:6] != b"\x7fELF\x02\x01" or data[18:20] != b"\xb7\x00":
                raise ValueError(f"{name} must be an ARM64 ELF")
        if name.endswith(".dll") and not data.startswith(b"MZ"):
            raise ValueError(f"{name} must be a managed assembly")
        if name == "sorage4" and b"runtime.aarch64/sorage4host.dll\0" not in data:
            data = bind_apphost(data)
        write(folder / name, data)
    write_runtime_config(folder / "runtime.aarch64")
    write(stage / LAUNCHER, (PORT / LAUNCHER).read_bytes())
    for name in METADATA:
        write(stage / name, (PORT / "metadata" / name).read_bytes())
    for name in ("patches/prepare.src", "patches/runtime.src", "patches/setup-screen.src",
                 "patches/assembly_store.py", "patches/texture.py", "repack.src",
                 "utils/patcher-name.lua"):
        write(folder / name, (PORT / name).read_bytes())
    write(folder / "gamedata/README.txt", b"Place your supported Android 1.4.10 base.apk here.\n")
    for notice in sorted((PORT / "licenses").glob("LICENSE-*.TXT")):
        write(folder / "licenses" / notice.name, notice.read_bytes())


def package(stage, target):
    with ZipFile(target, "x", compression=ZIP_DEFLATED, compresslevel=6) as archive:
        for file in sorted(stage.rglob("*")):
            if not file.is_file():
                continue
            name = file.relative_to(stage).as_posix()
            if name in METADATA:
                name = ID + "/" + name
            member = ZipInfo(name, (2026, 11, 8, 0, 0, 0))
            member.create_system = 3
            # Store POSIX execute bits in the ZIP entry instead of relying on launcher chmod.
            member.external_attr = (stat.S_IFREG | (0o755 if file.name in EXEC else 0o644)) << 16
            archive.writestr(member, file.read_bytes(), compress_type=ZIP_DEFLATED)
    with ZipFile(target) as archive:
        if archive.testzip():
            raise ValueError("Install ZIP CRC check failed")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stage", type=Path)
    parser.add_argument("--compiled", required=True, type=Path, help="Outputs of build.sh")
    parser.add_argument("--zip", required=True, type=Path)
    args = parser.parse_args()
    if args.stage.exists() or args.zip.exists():
        parser.error("Use fresh paths for the submission and ZIP")
    stage_compiled(args.compiled, args.stage)
    package(args.stage, args.zip)
