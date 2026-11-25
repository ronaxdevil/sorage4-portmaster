#!/usr/bin/env bash
set -euo pipefail

src=$(cd -- "$(dirname -- "$0")" && pwd -P)
mode=${1:-}
if [[ $# != 2 || ! "$mode" =~ ^(all|patch-tools|managed-runtime|native-codecs)$ ]]; then
  echo "Usage: bash build.sh {all|patch-tools|managed-runtime|native-codecs} NEW_BUILD_DIRECTORY" >&2
  exit 2
fi
out=$(realpath -m "$2")
dotnet=${DOTNET:-dotnet}
compiled="$out/compiled"
managed="$compiled/runtime.aarch64"
patches="$compiled/patches"
libs="$compiled/libs.aarch64"
utils="$compiled/utils"

need_file() {
  if [[ ! -f "$1" ]]; then
    echo "Missing build input: $1" >&2
    exit 2
  fi
}

checkout() {
  local name=$1 repo=$2 revision=$3 ref=${4:-$3}
  local directory="$out/deps/$name"
  if [[ -n "${PORT_SOURCE_MIRROR:-}" ]]; then
    repo="$(realpath "$PORT_SOURCE_MIRROR")/$name"
    ref="$revision"
  fi
  git init -q "$directory"
  git -C "$directory" -c core.autocrlf=false fetch -q --depth=1 "$repo" "$ref"
  if [[ $(git -C "$directory" rev-parse 'FETCH_HEAD^{commit}') != "$revision" ]]; then
    echo "Unexpected source revision for $name" >&2
    return 1
  fi
  git -C "$directory" -c core.autocrlf=false checkout -q --detach "$revision"
}

build_managed() {
  local project=$1 name=$2 target=$3
  shift 3
  local artifacts="$out/artifacts/$name"
  "$dotnet" build "$project" -c Release --artifacts-path "$artifacts" \
    -o "$artifacts/result" -p:DebugType=None -p:DebugSymbols=false \
    -p:NuGetAudit=false -p:GeneratePackageOnBuild=false \
    -p:TargetFramework=net9.0 -p:TargetFrameworks=net9.0 \
    -p:PathMap="$src=/_/src%2C$out=/_/build" "$@"
  install -m 644 "$artifacts/result/$name.dll" "$target/"
}

build_cecil() {
  checkout cecil https://github.com/jbevain/cecil.git 8c123e16bd0d693afc9932da85e1c9e740aa508c refs/tags/0.11.5
  sed -i '/PackageReference Include="Microsoft.SourceLink.GitHub"/d' "$out/deps/cecil/Directory.Build.props"
  build_managed "$out/deps/cecil/Mono.Cecil.csproj" Mono.Cecil "$managed" -p:DefineConstants=NET_CORE
}

build_patches() {
  for tool in coopsplit patchgam skipcall; do
    build_managed "$src/compatibility/patches/PatchTool.csproj" "$tool" "$patches" \
      -p:PatchTool="$tool" -p:CecilPath="$managed/Mono.Cecil.dll"
  done
  build_managed "$src/compatibility/patches/fixplatform/FixPlatform.csproj" fixplatform "$patches" \
    -p:CecilPath="$managed/Mono.Cecil.dll"
  build_managed "$src/compatibility/patches/MenuCompatibility.csproj" MenuCompatibility "$patches" \
    -p:CecilPath="$managed/Mono.Cecil.dll"
  build_managed "$src/compatibility/patches/EnableVideo.csproj" EnableVideo "$patches" \
    -p:VideoRuntime="$managed"
}

build_runtime() {
  checkout nvorbis https://github.com/NVorbis/NVorbis.git 871138f37dd72c8d908aa6360a506337a437504d refs/tags/v0.10.4
  checkout stb-image https://github.com/StbSharp/StbImageSharp.git 6fd7aebe1dbf10e28d78745f42a0c095c61d1945
  checkout stb-write https://github.com/StbSharp/StbImageWriteSharp.git 276e0a0e389f8bcc79aa6fb4446ff825d0fdb2b4
  local nvorbis="$out/deps/nvorbis/NVorbis/NVorbis.csproj"
  # These compatibility packages are provided by the .NET 9 target framework.
  sed -i '/PackageReference Include="System.\(Memory\|ValueTuple\)"/d' "$nvorbis"
  build_managed "$nvorbis" NVorbis "$managed"
  build_managed "$out/deps/stb-image/src/StbImageSharp.csproj" StbImageSharp "$managed"
  build_managed "$out/deps/stb-write/src/StbImageWriteSharp.csproj" StbImageWriteSharp "$managed"

  mkdir -p "$out/generator"
  local bindings="$out/generator/bindings"
  local android_package="${ANDROID_SDK_PACKAGE:-$out/generator/android-sdk.nupkg}"
  local android_name=microsoft.android.runtime.35.android-arm64
  if [[ -z "${ANDROID_SDK_PACKAGE:-}" ]]; then
    curl -fLsS --retry 3 \
      "https://api.nuget.org/v3-flatcontainer/$android_name/35.0.92/$android_name.35.0.92.nupkg" \
      -o "$android_package"
  fi
  printf '%s  %s\n' 9afca0d6c63be5ddf5844a42fd227c0e292f4435e531d4eeb30ec07cb6e7748a "$android_package" | sha256sum -c -
  python3 -B - "$android_package" "$bindings" <<'PY'
from pathlib import Path
import sys
from zipfile import ZipFile

output = Path(sys.argv[2])
output.mkdir()
with ZipFile(sys.argv[1]) as package:
    for name in ("Mono.Android.dll", "Java.Interop.dll"):
        (output / name).write_bytes(package.read(f"runtimes/android-arm64/lib/net9.0/{name}"))
PY
  build_managed "$src/compatibility/android-stubber/AndroidStubber.csproj" AndroidStubber "$out/generator" \
    -p:CecilPath="$managed/Mono.Cecil.dll"
  for assembly in Mono.Android Java.Interop; do
    "$dotnet" "$out/artifacts/AndroidStubber/result/AndroidStubber.dll" \
      "$bindings/$assembly.dll" "$managed/$assembly.dll"
  done
  build_managed "$src/compatibility/patches/bridge/Bridge.csproj" SOR4Bridge "$managed" \
    -p:PortableRuntime="$managed"
  mv "$managed/SOR4Bridge.dll" "$managed/SORAGE4Bridge.dll"
  # Android SDK stubs return null for assets; route Open/List to the Linux asset bridge.
  "$dotnet" "$out/artifacts/AndroidStubber/result/AndroidStubber.dll" \
    "$managed/Mono.Android.dll" "$out/generator/Mono.Android.dll" "$managed/SORAGE4Bridge.dll"
  mv "$out/generator/Mono.Android.dll" "$managed/Mono.Android.dll"

  local monogame_archive="${MONOGAME_SOURCE_ARCHIVE:-$out/deps/monogame.tar.gz}"
  if [[ -z "${MONOGAME_SOURCE_ARCHIVE:-}" ]]; then
    curl -fLsS --retry 3 \
      https://codeload.github.com/MonoGame/MonoGame/tar.gz/caaa1170c48b8d340690014423e1c8bbc0777899 \
      -o "$monogame_archive"
  fi
  printf '%s  %s\n' 691831ff43146119a98a5d649062a91d029acec21975b6e2891104f7e6003967 "$monogame_archive" | sha256sum -c -
  mkdir "$out/deps/monogame"
  tar -xzf "$monogame_archive" --strip-components=1 --no-same-owner -C "$out/deps/monogame"
  local framework="$out/deps/monogame/MonoGame.Framework"
  python3 -B "$src/compatibility/framework/apply.py" "$framework"
  python3 -B "$src/compatibility/framework/configure.py" "$framework"
  build_managed "$framework/MonoGame.Framework.SORAGE4.csproj" MonoGame.Framework "$managed" \
    -p:PortableRuntime="$managed"
  build_managed "$src/runtime/video/VideoPlayer.csproj" SorageVideo "$managed" -p:VideoRuntime="$managed"

  "$dotnet" publish "$src/runtime/host/Host.csproj" -c Release -r linux-arm64 --self-contained false \
    --artifacts-path "$out/artifacts/host" -p:UseAppHost=true -p:DebugType=None \
    -p:DebugSymbols=false -p:NuGetAudit=false -o "$out/host"
  install -m 755 "$out/host/sorage4host" "$compiled/sorage4"
  install -m 644 "$out/host/sorage4host.dll" "$managed/"
}

build_codecs() {
  bash "$src/tools/build-codec.src" "$out/codec" aarch64
  aarch64-linux-gnu-strip --strip-unneeded -o "$libs/libastc-repack.so" "$out/codec/libastc-repack.aarch64.so"
  aarch64-linux-gnu-strip --strip-unneeded -o "$libs/libsorage4astc.so" "$out/codec/libsorage4astc.so"
}

if [[ "$mode" == all ]]; then
  need_file "${ARM_GLIBC_SYSROOT:?Set ARM_GLIBC_SYSROOT to the ARM64 glibc development sysroot}/usr/include/stdio.h"
  need_file "${ARM_SDL_SYSROOT:?Set ARM_SDL_SYSROOT to the ARM64 SDL2 development sysroot}/usr/include/SDL2/SDL.h"
fi
if [[ -e "$out" ]]; then
  echo "Use a fresh build directory: $out" >&2
  exit 2
fi
mkdir -p "$managed" "$patches" "$libs/fallback" "$utils"

case "$mode" in
  patch-tools) build_cecil; build_patches ;;
  managed-runtime) build_cecil; build_runtime ;;
  native-codecs) build_codecs ;;
  all)
    build_cecil
    build_patches
    build_runtime
    build_codecs
    bash "$src/tools/build-native.sh" "$out" "$ARM_GLIBC_SYSROOT" "$ARM_SDL_SYSROOT"
    python3 -B "$src/tools/package.py" --compiled "$compiled" "$out/submission" --zip "$out/streetsofrage4.zip"
    sha256sum "$out/streetsofrage4.zip"
    ;;
esac
