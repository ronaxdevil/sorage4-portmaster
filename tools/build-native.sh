#!/usr/bin/env bash
set -euo pipefail

src=$(cd -- "$(dirname -- "$0")/.." && pwd -P)
out=$1
sysroot=$2
sdl=$3
libs="$out/compiled/libs.aarch64"
utils="$out/compiled/utils"
mkdir -p "$out/native-sources"

source_archive() {
  local name=$1 suffix=$2 url=$3 digest=$4
  local archive
  if [[ -n "${PORT_NATIVE_ARCHIVES:-}" ]]; then
    archive="$PORT_NATIVE_ARCHIVES/$name.$suffix"
  else
    archive="$out/native-sources/$name.$suffix"
    curl -fLsS --retry 3 "$url" -o "$archive"
  fi
  printf '%s  %s\n' "$digest" "$archive" | sha256sum -c -
  mkdir "$out/native-sources/$name"
  tar -xf "$archive" --strip-components=1 --no-same-owner -C "$out/native-sources/$name"
}

source_archive harfbuzz tar.xz https://github.com/harfbuzz/harfbuzz/releases/download/2.6.4/harfbuzz-2.6.4.tar.xz \
  9413b8d96132d699687ef914ebb8c50440efc87b3f775d25856d7ec347c03c12
source_archive freetype tar.xz https://download.savannah.gnu.org/releases/freetype/freetype-2.10.4.tar.xz \
  86a854d8905b19698bbc8f23b860bc104246ce4854dcea8e3b0fb21284f75784

bash "$src/tools/build-wwise.src" "$sysroot" "$sdl" "$libs/libWwise.so"
aarch64-linux-gnu-gcc -O2 -o "$utils/display-probe" "$src/compatibility/display-probe.c" -ldl

prefix="$out/native-prefix"
(
  cd "$out/native-sources/freetype"
  ./configure --host=aarch64-linux-gnu --prefix="$prefix" --enable-shared --disable-static \
    --with-zlib=no --with-bzip2=no --with-png=no --with-harfbuzz=no --with-brotli=no
  make -j"${BUILD_JOBS:-4}"
  make install
)
(
  cd "$out/native-sources/harfbuzz"
  PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig" LDFLAGS="-static-libstdc++ -static-libgcc" \
    ./configure --host=aarch64-linux-gnu --prefix="$prefix" --enable-shared --disable-static \
    --with-freetype=yes --with-glib=no --with-gobject=no --with-cairo=no \
    --with-icu=no --with-graphite2=no --with-fontconfig=no
  make -j"${BUILD_JOBS:-4}"
  make install
)
harfbuzz="$prefix/lib/libharfbuzz.so.0"
aarch64-linux-gnu-readelf -Ws "$harfbuzz" > "$out/harfbuzz-symbols.txt"
grep -qw hb_ft_font_create_referenced "$out/harfbuzz-symbols.txt"
aarch64-linux-gnu-strip --strip-unneeded -o "$libs/fallback/libharfbuzz.so.0" "$harfbuzz"
# HarfBuzz links against the firmware's FreeType at runtime.
chrpath -d "$libs/fallback/libharfbuzz.so.0"

source_archive ffmpeg tar.gz https://codeload.github.com/FFmpeg/FFmpeg/tar.gz/refs/tags/n7.1.3 \
  e0b04c4b43d7e6d67cb6710334fb513adf13ac860532f30e1d0ac4c231f232fb
bash "$src/tools/build-video.src" "$out/native-sources/ffmpeg" "$out/ffmpeg"
install -m 755 "$out/ffmpeg/ffmpeg" "$utils/sorage-video"

for file in "$out/compiled/sorage4" "$libs"/*.so "$libs/fallback/libharfbuzz.so.0" "$utils/display-probe" "$utils/sorage-video"; do
  versions=$(aarch64-linux-gnu-readelf --version-info "$file")
  highest=$(sed -n 's/.*Name: GLIBC_\([0-9.]*\).*/\1/p' <<<"$versions" | sort -Vu | tail -1)
  if [[ $(printf '%s\n' 2.29 "$highest" | sort -V | tail -1) != 2.29 ]]; then
    echo "$file requires GLIBC_$highest. Use an ARM64 cross toolchain targeting glibc 2.29 or older." >&2
    exit 1
  fi
done
