# Contributing to PenguLOD

Use a branch from `main`, keep changes focused, and include the relevant validation in your pull
request. Preserve upstream copyright notices and game-side compatibility names. Report bugs with
the PenguLOD version, distribution, workflow stage, settings needed to reproduce it, and a relevant
log excerpt. Use synthetic fixture names and portable example paths. Do not commit game/mod
assets, personal profiles, local benchmarks, session notes or assistant context files.
Document supported behavior and reproducible procedures; keep debugging history in focused issues.

## Linux development

Install the .NET 10 SDK. On Ubuntu 24.04 the native dependencies are:

```bash
sudo apt-get install g++-14 cmake ninja-build pkg-config curl patch python3 \
  zlib1g-dev liblz4-dev libtbb-dev libspdlog-dev libfmt-dev libgtest-dev \
  libfontconfig1 libx11-6 libice6 libsm6 libicu-dev xvfb \
  libvulkan-dev mesa-vulkan-drivers glslang-tools appstream
CC=gcc-14 CXX=g++-14 bash tools/build.sh
bash tools/test.sh
xvfb-run -a bash tools/native-smoke.sh
```

`tools/build.sh` builds and tests pinned FasterNGIO sources before publishing the applications.
For a subsequent local UI-only build, `bash tools/build.sh --reuse-backend` reuses a previously
prepared `.tools/fasterngio-runtime`. Releases always rebuild the native backend in CI.
Set `PENGULOD_DOTNET` to use a particular SDK executable; `PENGULOD_NUGET_SOURCE` overrides
the package source. The previous `ANVILLOD_*` build variables remain accepted.

Set `PENGULOD_PACKAGE_DIR="$PWD/dist/candidate/linux-x64"` for an isolated package build.
The build and AppImage scripts accept this override; it must be a `linux-x64` folder
under `dist/`. `bash tools/package-appimage.sh` requires `appstreamcli` and validates
the installed metadata, desktop entry and icon before packaging.

The core tests exercise engine fixtures; desktop tests use Avalonia’s rendered headless platform.
`tools/native-smoke.sh` additionally checks the actual Linux windowing backend. Optional tests
that require a real game profile are skipped by default. Keep live generation separate from builds:
start the app from a portable folder that the build will not overwrite.

For an optional game-backed desktop test, export a preset with dedicated output and cache
folders, then run `bash tools/test.sh --smoke-settings /path/to/test-preset.json`.
This test writes to those folders. Screenshots and TRX results are saved under `.artifacts/`.

Binary packages include an explicit set of user guides from `tools/build.sh`. Keep their
relative links valid inside the extracted package. Contributor and release-maintenance
documentation stays in the source checkout. Source bundles retain build inputs, patches,
dependency pins and licenses, excluding local tooling and assistant instructions.

## Windows development

Install .NET 10 SDK, PowerShell 7.4+, Python 3.12+, Git for Windows, CMake 3.28+,
Ninja and Visual Studio 2026 C++ build tools with the Windows SDK. Run from PowerShell:

```powershell
./tools/build-native-windows.ps1
./tools/test-windows.ps1
./tools/build-windows.ps1 -ReuseBackend
./tools/windows-smoke.ps1
```

`build-windows.ps1` without `-ReuseBackend` also builds and tests the native components.
The build fetches checksum-pinned FasterNGIO sources and a pinned vcpkg baseline from
`tools/windows/vcpkg.json`. Dependencies use the static MSVC runtime. Native builds stay
under `.tools/`; the self-contained desktop/CLI and ZIP are written under `dist/`.
Use a separate extracted package for a running application because rebuilding replaces
`dist/win-x64`. The smoke test opens the real Avalonia Win32 window with isolated settings.

The test grass process is a small .NET executable shared by both platforms, so failure,
Unicode/space-containing paths, process cancellation and output recovery are exercised
on Windows as well as Linux. Optional game-profile tests remain opt-in.

## Source layout

- `src/AnvilLOD.Desktop`: PenguLOD Avalonia UI and services.
- `src/AnvilLOD.Configuration`: desktop/CLI presets, validation and request construction.
- `src/AnvilLOD.Cli`: PenguLOD command-line entry point.
- `src/AnvilLOD.Core`, `Plugins`, `Meshes`, `Textures`: shared engine.
- `tools/fasterngio`: pinned native grass backend patches and packaging.
- `native/texture-compute`: optional headless Vulkan BC7 helper.
- `tests`: engine and desktop regression fixtures.
- `skse`: inherited game-runtime plugin source; versioned separately with `UPSTREAM_VERSION`.

The retained `AnvilLOD` names help upstream merges and compatibility. Avoid broad namespace or
output filename renames. Both packages use the Avalonia desktop; the inherited WPF
application and root-level upstream release scripts are outside PenguLOD's release pipeline.

## Upstream synchronization

```bash
git remote add upstream https://github.com/CageTV/AnvilLOD.git  # once
git fetch upstream --tags
git switch -c upstream/anvillod-next
git merge --no-ff <upstream-tag>
```

Keep PenguLOD’s `VERSION`, branding and cross-platform workflow when resolving conflicts; update
`UPSTREAM_VERSION` and `docs/PROVENANCE.md` to record the integrated upstream version.
Check desktop defaults against engine changes. Run relevant local tests and require both platform builds in CI.
See [RELEASING.md](docs/RELEASING.md) for version/tag conventions.
