# Project provenance and compatibility

PenguLOD is a renamed fork of [CageTV/AnvilLOD](https://github.com/CageTV/AnvilLOD),
not a clean-room rewrite. Upstream history and copyright notices are retained.

| Component | Baseline |
| --- | --- |
| Initial Linux port base | AnvilLOD 0.2.68, `84531ba648ed588835776786c3fe528457861e37` |
| Native Linux baseline commit | `90d2e30` |
| Integrated upstream release | AnvilLOD 0.2.70, `e10cb46219727d819a298b2c4a0c23e6f6228d20` |
| PenguLOD application version | Root `VERSION`; current `0.2.0-alpha.2` |
| AnvilLOD game-plugin version | Root `UPSTREAM_VERSION`; currently `0.2.70` |
| FasterNGIO | `5a1bff6ad3e44b5b9c4b5acdd0663ae20860ca40` |

The repository’s `origin` should be `git@github.com:opteron-x86/PenguLOD.git`, and
`upstream` should be `https://github.com/CageTV/AnvilLOD.git`. Upstream changes are
merged on a branch, tested and reviewed before reaching `main`.

PenguLOD’s executables, menu entry and default configuration directory have their own names.
C# namespaces/project directories and game-facing identifiers remain `AnvilLOD` to reduce
merge churn and preserve runtime compatibility. This includes plugin names, SKSE data,
texture subdirectories, output ownership markers and manifests. The inherited Windows WPF
application and PowerShell build scripts remain in source; they are outside PenguLOD’s Linux release pipeline.

The initial Linux release does not redistribute the SKSE DLL. Install the upstream AnvilLOD
plugin matching your Skyrim runtime separately when using dynamic/runtime features.
Do not use PenguLOD’s application version to select that DLL.

FasterNGIO is built from checksum-verified archives with the headless, TESWorld pin,
scheduler-progress and finite-blades patches under `tools/fasterngio/`. Its submodules,
corresponding source, patches and runtime dependency notices are included in the Linux package.
NuGet package versions are recorded in the published `.deps.json` files and
`THIRD-PARTY-PACKAGES.txt`. No Skyrim, mod-list or generated game assets belong in this repository.

AppImage packaging uses [appimagetool 1.9.1](https://github.com/AppImage/appimagetool/releases/tag/1.9.1),
verified against SHA-256 `ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0`.
Its embedded [type2 runtime at caf24f9](https://github.com/AppImage/type2-runtime/tree/caf24f9)
is reused without downloading another runtime. The runtime license and dependency notices
are retained under `assets/licenses/AppImage-runtime-LICENSE.txt` and in binary packages.
The Vulkan helper is PenguLOD source; its compiler and the host Vulkan driver are not bundled.
