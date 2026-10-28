<p align="center"><img src="assets/pengulod.svg" width="144" alt="PenguLOD"></p>

# PenguLOD

[![Linux build](https://github.com/opteron-x86/PenguLOD/actions/workflows/linux.yml/badge.svg)](https://github.com/opteron-x86/PenguLOD/actions/workflows/linux.yml)
[![License: GPL-3.0-only](https://img.shields.io/badge/license-GPL--3.0--only-blue)](LICENSE.txt)

A native Linux LOD toolkit for Skyrim, built on [CageTV’s AnvilLOD](https://github.com/CageTV/AnvilLOD).
PenguLOD combines an Avalonia desktop and CLI with native terrain, object, tree and grass LOD,
texture generation, and offline grass caching. It reads Fluorine/MO2 profiles directly and runs without Wine.

**Current version: 0.2.0-alpha.2**, incorporating AnvilLOD **0.2.70**.
The all-in-one pipeline supports grass → textures → terrain → object/tree/grass LOD, with
verified resume and safe output publication. New terrain and seasonal rendering need broader
in-game validation; this alpha does not claim full TexGen or DynDOLOD parity.

## Download and run

Download the Linux x64 archive from [Releases](https://github.com/opteron-x86/PenguLOD/releases),
extract it, and keep the `linux-x64` folder together:

```bash
./linux-x64/run-pengulod.sh
./linux-x64/PenguLOD --version
./linux-x64/PenguLOD --help
```

The .NET runtime and native CPU grass backend are included. Release builds target **Ubuntu 24.04
or a compatible newer glibc-based x64 desktop**. X11, fontconfig, ICE/SM and ICU are required;
Wayland sessions need XWayland. CI checks Ubuntu 24.04, Debian 13 and Fedora 43.
Once the folder is in its permanent location, `./linux-x64/install-desktop.sh` adds a menu entry
(requires Python 3). No root installation is needed. An AppImage is also available; see
[packages and compatibility](docs/PACKAGING.md), including the `--cli` entry point.

On first launch, PenguLOD imports an existing `~/.config/AnvilLOD/settings.json` into
`~/.config/PenguLOD/settings.json`, respecting XDG overrides. The original is preserved, along
with all selected output paths, preferences and the old cache location. An existing PenguLOD
configuration takes precedence. `--settings /path/to/settings.json` selects an isolated configuration.

See the [Linux guide](docs/LINUX-PORT.md) for source setup, texture generation, grass caching,
settings, testing and current limitations.

## Available now

- Avalonia desktop with progress, cancellation, logs, presets and resource controls.
- Direct Fluorine/MO2 profile reading, including Wine drive mappings; deployed Data-folder support.
- Native terrain/water BTR meshes, diffuse/normal tiles and terrain-shadow heightmaps.
- Incremental object LOD, billboard trees, 3D/hybrid models and native tree reduction with
  per-level geometry budgets; grass LOD, occlusion and seasonal terrain/object/tree/grass output.
- Native texture stitching, tree/grass billboards, hybrid trunk textures, material/normal baking,
  experimental object rendering and conservative paired object atlases.
- Bundled FasterNGIO CPU grass caching without launching Skyrim.
- Shared desktop/CLI presets and persistent jobs, dependency invalidation and measured stage timings.
- Pipeline preflight, asset precedence and coverage diagnostics, mesh simplification,
  custom rules and native BC6H-to-BC7 texture repair.
- Optional Vulkan encoding for large opaque BC7 textures, with quality/speed checks and CPU fallback.

The [CLI](docs/CLI.md) runs the same persistent generation stages as the desktop, alongside
scanning and author/repair tools. PenguLOD’s texture generator does **not** yet
match every TexGen feature. It uses assets from your installed mods and does not bundle game assets,
DynDOLOD Resources or third-party LOD packs.

**Game runtime:** Skyrim still runs under Proton/Wine. Dynamic LOD and runtime quality controls
use the separate **AnvilLOD SKSE plugin** appropriate for your game runtime, available from
[upstream releases](https://github.com/CageTV/AnvilLOD/releases/tag/v0.2.70).
That Windows DLL is not included in the Linux generator package. Game-side `AnvilLOD.esp`,
`AnvilLOD.esm`, runtime data and output manifest names remain unchanged for compatibility.

## Build and contribute

Requires .NET 10 SDK, CMake 3.28+, Ninja, a C++23 compiler and the native development libraries
listed in [CONTRIBUTING.md](CONTRIBUTING.md).

```bash
git clone git@github.com:opteron-x86/PenguLOD.git
cd PenguLOD
bash tools/test.sh
bash tools/build.sh
./dist/linux-x64/run-pengulod.sh
```

`main` is the integration branch. Every push and pull request runs Linux tests, packages the
app and checks native startup. `v*` tags matching `VERSION` publish tested releases with
checksums and source archives. See [release maintenance](docs/RELEASING.md).

The [roadmap](docs/ROADMAP.md) records implemented milestones, validation and remaining limits.
Use the [transition checklist](docs/DIAGNOSTICS.md#repeatable-checks) when testing generated output in Skyrim.

## Credits and license

PenguLOD is maintained by **opteron-x86 and contributors**, based on **AnvilLOD by CageTV**.
The native grass engine is [FasterNGIO by panthuncia](https://github.com/panthuncia/FasterNGIO),
with pinned source and local patches. Thanks also to the Avalonia, Mutagen, nifly and other
dependency authors. [Provenance](docs/PROVENANCE.md) records the fork point and compatibility decisions.

[GPL-3.0-only](LICENSE.txt). Existing upstream copyright notices are retained. Packaged dependencies
include their own notices and licenses; the grass backend includes its corresponding source.
Generated output may contain assets belonging to your installed mods and is not covered by this repository’s license.
