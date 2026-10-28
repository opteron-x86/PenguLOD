# Contributing to PenguLOD

Use a branch from `main`, keep changes focused, and include the relevant validation in your pull
request. Preserve upstream copyright notices and game-side compatibility names. Report bugs with
the PenguLOD version, distribution, workflow stage, settings needed to reproduce it, and a relevant
log excerpt. Synthetic fixtures are preferred; do not commit game/mod assets or personal profiles.

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

The core tests exercise engine fixtures; desktop tests use Avalonia’s rendered headless platform.
`tools/native-smoke.sh` additionally checks the actual Linux windowing backend. Optional tests
that require a real game profile are skipped by default. Keep live generation separate from builds:
start the app from a portable folder that the build will not overwrite.

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
output filename renames. Windows WPF/PowerShell tooling is inherited and is not a supported
PenguLOD release target.

## Upstream synchronization

```bash
git remote add upstream https://github.com/CageTV/AnvilLOD.git  # once
git fetch upstream --tags
git switch -c upstream/anvillod-next
git merge --no-ff <upstream-tag>
```

Keep PenguLOD’s `VERSION`, branding and Linux workflow when resolving conflicts; update
`UPSTREAM_VERSION` and `docs/PROVENANCE.md` to record the integrated upstream version.
Check desktop defaults against engine changes. Run the tests and Linux build before opening a PR.
See [RELEASING.md](docs/RELEASING.md) for version/tag conventions.
