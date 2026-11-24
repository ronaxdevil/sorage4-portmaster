# Build Streets of Rage 4 for PortMaster

`source-new` is the ARM64 Linux PortMaster build tree. Run the commands from this directory on Linux or WSL. The compiler and packager accept a fresh build directory anywhere. They do not read an earlier port ZIP, prepared game data, or the project's `Work` directory.

## Build and test the patch DLLs

Install the .NET 9 SDK, Git and Python 3.9 or later. Run:

```bash
bash build.sh patch-tools build/patch-tools
```

The build checks out Mono.Cecil 0.11.5 at its pinned revision and compiles it from C#. It then builds `coopsplit.dll`, `patchgam.dll`, `skipcall.dll`, `fixplatform.dll`, `MenuCompatibility.dll` and `EnableVideo.dll` from the source in `compatibility/patches/`. The resulting tools go in `compiled/patches/`. On the handheld, the installer verifies the supported APK and game assembly hashes before running these tools on a local copy of the game. The install ZIP contains the tools.

## Build the runtime components and ZIP

For native code install CMake, Make, pkg-config, curl, xz, chrpath, and an `aarch64-linux-gnu` C/C++ toolchain that targets glibc 2.29 or earlier. Use a Linux filesystem for the build directory if your WSL mount lacks Linux symlinks. The native validation used Bootlin's AArch64 glibc stable 2018.11-1 C/C++ toolchain under the `aarch64-linux-gnu` command prefix. You also need these SDK inputs:

- The pinned Microsoft Android SDK package 35.0.92 supplies the original `Mono.Android.dll` product build 35.0.2.90 and `Java.Interop.dll` 9.0.0.0 to the source-built stub generator. The driver downloads and checks the SDK package, or accepts the same hash-verified package via `ANDROID_SDK_PACKAGE`. It generates both compatibility DLLs locally. Microsoft's implementation assemblies are SDK inputs and are built separately by Microsoft.
- `ARM_GLIBC_SYSROOT` points to an ARM64 sysroot with the glibc headers and libraries. `ARM_SDL_SYSROOT` points to an ARM64 sysroot with the SDL2 headers and `libSDL2-2.0.so.0`. The audio bridge uses both sysroots and checks its resulting glibc imports. The other native targets use the cross toolchain's own sysroot.

```bash
export ARM_GLIBC_SYSROOT=/path/to/arm64-glibc-sysroot
export ARM_SDL_SYSROOT=/path/to/arm64-SDL2-sysroot
bash build.sh all build/release
```

The driver pins the managed Git source commits and the SHA-256 hashes of the FFmpeg, HarfBuzz and FreeType source archives. It compiles the patched MonoGame GLES project, bridge, Stb libraries, NVorbis, video player, host, Wwise bridge, display probe and native texture codecs. The SDK supplies the framework-dependent ARM64 apphost template. FreeType is built as a private link dependency for HarfBuzz. Only the HarfBuzz fallback goes in the ZIP, where it uses FreeType from the firmware. The ETC1 fallback uses the bundled AOSP source.

For offline native downloads, set `PORT_NATIVE_ARCHIVES` to a directory containing the pinned archives named `ffmpeg.tar.gz`, `harfbuzz.tar.xz` and `freetype.tar.xz`. The driver checks their hashes before extracting them.

Use `bash build.sh managed-runtime build/managed` to isolate the managed build and `bash build.sh native-codecs build/codecs` to isolate the texture libraries. Set `DOTNET` to an absolute path if the .NET SDK is outside `PATH`. For managed Git downloads, `PORT_SOURCE_MIRROR` optionally points to a directory with the pinned repositories named `cecil`, `nvorbis`, `stb-image` and `stb-write`. Each checkout must still match the expected commit. `MONOGAME_SOURCE_ARCHIVE` optionally supplies the same SHA-256-verified source archive that the driver downloads.

A completed build stages `build/release/submission/` and creates `build/release/streetsofrage4.zip`. The ARM64 ELF and glibc checks run before packaging. License documents in `port/licenses/` travel with their corresponding files, including notices for retained compatibility code.

## Build inputs and copied files

| File or component | How the build produces it |
| --- | --- |
| `sorage4` | The .NET SDK supplies the precompiled ARM64 apphost. Publishing binds it to the managed host. The packager updates the entry-point path for the ZIP layout. |
| `Mono.Android.dll` and `Java.Interop.dll` | The source-built stub generator rewrites the pinned Microsoft SDK's precompiled Android assemblies into local compatibility DLLs. |
| MonoGame's OpenGL effect shaders and icon | The compiler embeds six existing `.ogl.mgfxo` files and `MonoGame.bmp` from the pinned MonoGame tree into the source-built `MonoGame.Framework.dll`. |
| Scripts, artwork, metadata, and licenses | The packager copies these files directly from `port/`. It generates the .NET runtime config and the game data placement note. |
| Game assemblies, assets, and `libWwise.real.so` | First-run setup extracts these from the user's supported APK and patches the game assembly on the handheld. The packaged `libWwise.so` is the source-built Linux bridge. |
| PortMaster .NET 9.0.22 runtime, gptokeyb2, SDL2, and FreeType | The handheld supplies these through PortMaster or its firmware. The build also compiles a private FreeType copy to link the HarfBuzz fallback. |
