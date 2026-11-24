# Build Streets of Rage 4 for PortMaster

Run all commands from the **root of this repository** (where `build.sh` lives) on Linux, WSL, or Docker. The compiler and packager require a fresh build directory — they do not read an earlier port ZIP, prepared game data, or a previous build directory.

---

## Build Modes

| Mode | What it builds |
| --- | --- |
| `patch-tools` | Mono.Cecil + all IL patch DLLs only |
| `managed-runtime` | Mono.Cecil + full managed runtime (MonoGame, NVorbis, host, etc.) |
| `native-codecs` | Texture codec `.so` libraries only (astc-repack, sorage4astc) |
| `all` | Everything above + native libs + FFmpeg + packaging → `streetsofrage4.zip` |

---

## Dependencies

### Managed / .NET (all modes)

| Tool | Version | Purpose |
| --- | --- | --- |
| .NET SDK | **9** | Build C# projects, publish self-contained host |
| Git | any recent | Checkout pinned sources (Cecil, NVorbis, StbSharp, MonoGame) |
| Python | **3.9+** | Framework patch scripts, packager |

### Native / C++ (`all` and `native-codecs` modes)

| Tool | Purpose |
| --- | --- |
| `aarch64-linux-gnu-gcc` / `g++` | Cross-compile native `.so` and display-probe |
| `cmake` | Build astc-encoder |
| `make` | Build native targets |
| `pkg-config` | Locate libraries in sysroots |
| `curl` | Download pinned archives (HarfBuzz, FreeType, FFmpeg, Android SDK NuGet) |
| `xz` / `tar` | Extract downloaded archives |
| `chrpath` | Strip RPATH from HarfBuzz |
| `sha256sum` | Verify archive hashes |
| `aarch64-linux-gnu-strip` | Strip debug info from native output |
| `aarch64-linux-gnu-readelf` | GLIBC version validation |

### External sysroot inputs (`all` mode only)

| Environment variable | Description |
| --- | --- |
| `ARM_GLIBC_SYSROOT` | ARM64 sysroot with glibc ≤ 2.29 headers and libs (e.g. Bootlin AArch64 glibc stable 2018.11-1) |
| `ARM_SDL_SYSROOT` | ARM64 sysroot containing SDL2 headers and `libSDL2-2.0.so.0` |

### Optional environment variables

| Variable | Purpose |
| --- | --- |
| `DOTNET` | Absolute path to `dotnet` if not on `PATH` |
| `PORT_NATIVE_ARCHIVES` | Directory with pre-downloaded `ffmpeg.tar.gz`, `harfbuzz.tar.xz`, `freetype.tar.xz` |
| `PORT_SOURCE_MIRROR` | Directory with pre-cloned repos named `cecil`, `nvorbis`, `stb-image`, `stb-write` |
| `ANDROID_SDK_PACKAGE` | Path to the pre-downloaded `microsoft.android.runtime.35.android-arm64.35.0.92.nupkg` |
| `MONOGAME_SOURCE_ARCHIVE` | Path to the pre-downloaded MonoGame source archive |
| `BUILD_JOBS` | Parallel job count for `make` and CMake (default: `4`) |

---

## Install Dependencies

### Linux (Ubuntu / Debian)

```bash
sudo apt-get update
sudo apt-get install -y \
  git curl xz-utils tar make cmake pkg-config chrpath \
  gcc-aarch64-linux-gnu g++-aarch64-linux-gnu \
  binutils-aarch64-linux-gnu \
  python3 python3-pip ca-certificates

# .NET 9 SDK
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --version 9.0 --install-dir "$HOME/.dotnet"
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
export PATH="$HOME/.dotnet:$PATH"
dotnet --version
```

### WSL (Ubuntu / Debian)

```bash
sudo apt-get update
sudo apt-get install -y \
  git curl xz-utils tar make cmake pkg-config chrpath \
  gcc-aarch64-linux-gnu g++-aarch64-linux-gnu \
  binutils-aarch64-linux-gnu \
  python3 python3-pip ca-certificates

# .NET 9 SDK
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --version 9.0 --install-dir "$HOME/.dotnet"
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
export PATH="$HOME/.dotnet:$PATH"
dotnet --version
```

> **WSL Note:** The build directory **must** be on a Linux filesystem (e.g. `~/build/`) — not on a Windows mount like `/mnt/c/`. Linux symlinks are required by the native build.

### Docker

```dockerfile
# Save as Dockerfile, then: docker build -t sor4-builder .
FROM ubuntu:22.04
ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y \
    git curl xz-utils tar make cmake pkg-config chrpath \
    gcc-aarch64-linux-gnu g++-aarch64-linux-gnu \
    binutils-aarch64-linux-gnu \
    python3 python3-pip ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- \
    --version 9.0 --install-dir /usr/local/dotnet
ENV PATH="/usr/local/dotnet:$PATH"
ENV DOTNET_ROOT="/usr/local/dotnet"

WORKDIR /work
```

```bash
docker build -t sor4-builder .
```

---

## Build Commands

### Mode 1 — Patch DLLs only (fastest, no native compiler needed)

Checks out Mono.Cecil 0.11.5 and builds `coopsplit.dll`, `patchgam.dll`, `skipcall.dll`, `fixplatform.dll`, `MenuCompatibility.dll`, `EnableVideo.dll` into `compiled/patches/`.

```bash
# Linux / WSL
bash build.sh patch-tools ~/sor4-build/patch-tools

# Docker
docker run --rm \
  -v "$PWD":/src \
  -v ~/sor4-build/patch-tools:/out \
  sor4-builder \
  bash /src/build.sh patch-tools /out/build
```

### Mode 2 — Managed runtime only (no native compiler needed)

Builds the full managed stack: Mono.Cecil, NVorbis, StbImageSharp, MonoGame.Framework, SorageVideo, host, Android stubs.

```bash
# Linux / WSL
bash build.sh managed-runtime ~/sor4-build/managed

# Docker
docker run --rm \
  -v "$PWD":/src \
  -v ~/sor4-build/managed:/out \
  sor4-builder \
  bash /src/build.sh managed-runtime /out/build
```

### Mode 3 — Native texture codecs only

Requires the `aarch64-linux-gnu` cross toolchain and CMake. Builds `libastc-repack.so` and `libsorage4astc.so`.

```bash
# Linux / WSL
bash build.sh native-codecs ~/sor4-build/codecs

# Docker
docker run --rm \
  -v "$PWD":/src \
  -v ~/sor4-build/codecs:/out \
  sor4-builder \
  bash /src/build.sh native-codecs /out/build
```

### Mode 4 — Full build → `streetsofrage4.zip`

Builds all components, runs ARM64 ELF and glibc checks, and produces the final install ZIP.

```bash
# Linux / WSL
export ARM_GLIBC_SYSROOT=/path/to/arm64-glibc-sysroot
export ARM_SDL_SYSROOT=/path/to/arm64-SDL2-sysroot
export BUILD_JOBS=8   # optional

bash build.sh all ~/sor4-build/release

# Output ZIP: ~/sor4-build/release/streetsofrage4.zip
```

```bash
# Docker
docker run --rm \
  -v "$PWD":/src \
  -v /path/to/arm64-glibc-sysroot:/sysroot/glibc \
  -v /path/to/arm64-SDL2-sysroot:/sysroot/sdl2 \
  -v ~/sor4-build/release:/out \
  -e ARM_GLIBC_SYSROOT=/sysroot/glibc \
  -e ARM_SDL_SYSROOT=/sysroot/sdl2 \
  -e BUILD_JOBS=8 \
  sor4-builder \
  bash /src/build.sh all /out/build

# Output ZIP: ~/sor4-build/release/build/streetsofrage4.zip
```

---

## Packaging Only (re-run without full rebuild)

If compiled outputs already exist and you only need to re-create the ZIP:

```bash
python3 tools/package.py \
  --compiled ~/sor4-build/release/compiled \
  ~/sor4-build/release/submission \
  --zip ~/sor4-build/release/streetsofrage4.zip
```

---

## Offline / Air-gapped Build

### Pre-download native archives

```bash
mkdir -p ~/sor4-archives

# FFmpeg n7.1.3
curl -fLsS https://codeload.github.com/FFmpeg/FFmpeg/tar.gz/refs/tags/n7.1.3 \
  -o ~/sor4-archives/ffmpeg.tar.gz

# HarfBuzz 2.6.4
curl -fLsS \
  https://github.com/harfbuzz/harfbuzz/releases/download/2.6.4/harfbuzz-2.6.4.tar.xz \
  -o ~/sor4-archives/harfbuzz.tar.xz

# FreeType 2.10.4
curl -fLsS \
  https://download.savannah.gnu.org/releases/freetype/freetype-2.10.4.tar.xz \
  -o ~/sor4-archives/freetype.tar.xz

# Android SDK NuGet
curl -fLsS \
  "https://api.nuget.org/v3-flatcontainer/microsoft.android.runtime.35.android-arm64/35.0.92/microsoft.android.runtime.35.android-arm64.35.0.92.nupkg" \
  -o ~/sor4-archives/android-sdk.nupkg

export PORT_NATIVE_ARCHIVES=~/sor4-archives
export ANDROID_SDK_PACKAGE=~/sor4-archives/android-sdk.nupkg
```

### Pre-clone managed Git sources

```bash
mkdir -p ~/sor4-mirrors
git clone --bare https://github.com/jbevain/cecil.git               ~/sor4-mirrors/cecil
git clone --bare https://github.com/NVorbis/NVorbis.git             ~/sor4-mirrors/nvorbis
git clone --bare https://github.com/StbSharp/StbImageSharp.git      ~/sor4-mirrors/stb-image
git clone --bare https://github.com/StbSharp/StbImageWriteSharp.git ~/sor4-mirrors/stb-write

export PORT_SOURCE_MIRROR=~/sor4-mirrors
```

---

## Expected Output

A successful `all` build stages `submission/` and creates `streetsofrage4.zip`:

```
<build-dir>/
  streetsofrage4.zip              ← Install this on your handheld via PortMaster
  submission/
    Streets of Rage 4.sh          ← Launcher script
    README.md
    port.json / gameinfo.xml / cover.png / screenshot.png
    streetsofrage4/
      sorage4                     ← ARM64 .NET apphost (ELF)
      runtime.aarch64/            ← Managed DLLs (MonoGame, NVorbis, host…)
      patches/                    ← IL patch tools (coopsplit, patchgam…)
      libs.aarch64/               ← Native .so (libWwise, libastc-repack, libsorage4astc)
        fallback/libharfbuzz.so.0
      utils/
        display-probe
        sorage-video
      licenses/                   ← All third-party license notices
      gamedata/README.txt         ← Place base.apk here on the handheld
```

---

## Build Inputs and Copied Files

| File or component | How the build produces it |
| --- | --- |
| `sorage4` | The .NET SDK supplies the precompiled ARM64 apphost. Publishing binds it to the managed host. The packager updates the entry-point path for the ZIP layout. |
| `Mono.Android.dll` and `Java.Interop.dll` | The source-built stub generator rewrites the pinned Microsoft SDK's precompiled Android assemblies into local compatibility DLLs. |
| MonoGame's OpenGL effect shaders and icon | The compiler embeds six existing `.ogl.mgfxo` files and `MonoGame.bmp` from the pinned MonoGame tree into the source-built `MonoGame.Framework.dll`. |
| Scripts, artwork, metadata, and licenses | The packager copies these files directly from `port/`. It generates the .NET runtime config and the game data placement note. |
| Game assemblies, assets, and `libWwise.real.so` | First-run setup extracts these from the user's supported APK and patches the game assembly on the handheld. The packaged `libWwise.so` is the source-built Linux bridge. |
| PortMaster .NET 9.0.22 runtime, gptokeyb2, SDL2, and FreeType | The handheld supplies these through PortMaster or its firmware. The build also compiles a private FreeType copy to link the HarfBuzz fallback. |

---

## Troubleshooting

| Problem | Fix |
| --- | --- |
| `Use a fresh build directory` | The output path must not already exist. Choose a new path. |
| `Missing build input: ARM_GLIBC_SYSROOT` | Export `ARM_GLIBC_SYSROOT` and `ARM_SDL_SYSROOT` before running `all` mode. |
| `requires GLIBC_X.XX … targeting glibc 2.29 or older` | Use the Bootlin AArch64 glibc 2018.11-1 cross toolchain. |
| `.NET SDK not found` | Set `export DOTNET=/full/path/to/dotnet` or ensure `dotnet` is on `PATH`. |
| WSL symlink / build errors | Build dir must be on a Linux filesystem (`~/`), not a Windows mount (`/mnt/…`). |
| `Unexpected source revision for <name>` | Network issue or stale mirror. Re-download or update `PORT_SOURCE_MIRROR`. |
| `The SDK apphost's entry-point slot differs` | Wrong `sorage4` binary. Rebuild with `all` mode from a fresh directory. |
