# Streets of Rage 4 — PortMaster

ARM64 Linux port for PortMaster handhelds. See [BUILD.md](BUILD.md) for full build instructions, dependency setup, and troubleshooting.

---

## Project Layout

| Folder | Contents |
| --- | --- |
| `runtime/` | Managed game host and video player |
| `compatibility/` | Graphics and audio compatibility code, texture codecs, Android stub generator, and game setup patch tools |
| `vendor/` | Bundled third-party codec sources (astc-encoder, lz4, aosp-etc1) |
| `tools/` | Native build scripts and ZIP packager |
| `port/` | Launcher, first-run scripts, package metadata, artwork, and licenses |

---

## Quick Build

Install the dependencies listed in [BUILD.md](BUILD.md), then run from the root of this repository:

```bash
# Patch DLLs only (no native compiler needed)
bash build.sh patch-tools ~/sor4-build/patch-tools

# Full build — all components + streetsofrage4.zip
export ARM_GLIBC_SYSROOT=/path/to/arm64-glibc-sysroot
export ARM_SDL_SYSROOT=/path/to/arm64-SDL2-sysroot
bash build.sh all ~/sor4-build/release
```

For all build modes, Docker usage, offline builds, expected output layout, and troubleshooting, see [BUILD.md](BUILD.md).

---

## Installation on Handheld

1. Install `streetsofrage4.zip` via PortMaster or extract to your ports directory.
2. Place your `base.apk` (SOR4 v1.4.10, SHA-256 `b96068021a1c59758734843c25d584f4e168525735a5677fca748135ddc15b2b`) at `ports/streetsofrage4/gamedata/base.apk`.
3. Launch **Streets of Rage 4** from PortMaster. First-run setup patches and extracts game data (~3 GB free required).
4. Keep `conf/` when updating — it holds saves and settings.

> **DLC Note:** Mr. X Nightmare characters remain locked in this port — Epic purchase verification is unavailable on handheld.

---

## Controls

| Button | Action |
| --- | --- |
| D-pad / left stick | Move / navigate |
| X | Attack |
| A | Jump |
| Y | Special |
| B | Pick up or throw |
| R2 | Backside Attack |
| START | Confirm |
| Select + Start | Exit through the game |
| Guide/Menu + Start | Force quit via gptokeyb2 |
