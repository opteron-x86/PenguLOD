# Changelog

PenguLOD uses its own semantic version, independent of AnvilLOD’s game plugin version.
Pre-release versions are marked alpha or beta until the supported workflows have broader validation.

## Unreleased

## [0.3.0-alpha.1] - 2026-10-28

- Native 3D trees and automatic billboards preserve record-specific texture variants.
  Missing or ambiguous shape mappings produce explicit fallback reasons.
- Optional foliage reduction removes whole cards within configurable sampled alpha-coverage
  limits (1% near, 3% far by default), preserving surviving UVs and reporting removed geometry.
- Terrain combines source normal detail with slope normals, honors the default landscape normal
  INI setting, and supports PBR albedo/AO calibration with opt-in texture twin lookup.
  Colour mipmaps average in linear light without treating material alpha as transparency.
- Seasonal diagnostics report resolved swaps and grass-cache gaps. Pipeline reports count
  default/WIN/SPR/SUM/AUT output. Invalid seasonal records or required meshes stop publication.
- Interrupted terrain jobs resume verified mesh/diffuse/normal tile sets, including seasons.
  Corrupt tiles are rebuilt individually; source, settings or version changes invalidate reuse.
- Added terrain phase timings, bounded sampling caches, synthetic benchmarks and CI reports.
  Pipeline caches are kept outside source game files and input mods.
- Shared desktop/CLI controls and neutral user guides cover the new features. In-game terrain/tree
  appearance and physical GPU validation remain separate open work.

## [0.2.0-alpha.3] - 2026-10-28

- Reorganized documentation into setup, grass, textures and troubleshooting guides, with
  current workflow instructions and a focused roadmap.
- Removed profile-specific paths, local benchmark results and development-session notes
  from documentation; replaced profile-derived fixture names with neutral examples.
- Binary packages now include only user-facing guides. Bundled backend source excludes
  assistant instruction files and local tool state while retaining build inputs and licenses.
- No generation behavior changes.

## [0.2.0-alpha.2] - 2026-10-27

- Fixed terrain generation failing on unpainted LAND by using Skyrim's `landscape/dirt02.dds`
  default instead of `landscape/default.dds`. Honor `sDefaultLandDiffuseTexture` from the
  selected profile/explicit INI with case-insensitive Linux path lookup.
- Skip the base texture when alpha layers fully cover it; missing painted textures
  still stop publication and now identify the world, cell and quadrant.
- Added regression coverage for omitted/null base layers, INI precedence and opaque paint.

## [0.2.0-alpha.1] - 2026-10-27

- Portable archive and AppImage packages, isolated candidate builds, and compatibility checks
  for Ubuntu 24.04, Debian 13 and Fedora 43.

- Shared desktop/CLI presets and validation, per-stage CLI commands, request export,
  saved-job inspection and compact reports. Legacy CLI generation now uses safe publication.

- Optional headless Vulkan BC7 encoding with device/quality checks, a measured automatic
  speed gate, CPU fallback and desktop/CLI benchmark reports. Software-driver validation
  passes; hardware performance validation remains pending.

- Native paired object texture atlases with UV remapping, material grouping, padded mips,
  conservative eligibility checks and coverage reports.

- Seasonal terrain, tree atlases/BTT/LST and native 3D tree replacements, using Seasons of
  Skyrim form swaps. Added a synthetic seasonal profile and end-to-end regression checks.

- Billboard/trunk/object rendering now bakes source normals, UV transforms and clamp modes,
  with configurable PBR albedo/AO and light calibration. Fixed stale material grouping keys
  after texture replacement.

- Added automatic pipeline preflight and a Diagnostics page with model/texture coverage,
  stale-cache checks, runtime requirements, asset precedence and metadata-only JSON reports.

- Shared desktop/CLI pipeline with durable checkpoints, dependency invalidation, verified
  output reuse, cancellation recovery and per-stage measured timings.
- Isolated LOD publication with previous-output backups, concurrent-job locks and retained
  failure logs; a failed or empty generation preserves the installed output.
- Native terrain/water stage with landscape layer baking, model-space normals, existing-BTR
  fallback, terrain-shadow heightmap export and desktop controls.
- Per-level tree budgets, authored far-model selection, native trunk reduction and optional
  full-model fallback. Foliage geometry and UVs are preserved; over-budget levels use
  billboards with explicit coverage/cost reports in the desktop and scan JSON.
- Native fallback preserves record-specific alternate tree textures by retaining their
  generated billboards, including seasonal replacements.

## [0.1.0-alpha.1] - 2026-10-27

Initial PenguLOD Linux release, based on CageTV/AnvilLOD 0.2.70.

### Included

- Native Avalonia desktop and Linux CLI, with direct Fluorine/MO2 profile access.
- Native texture stitching, tree/grass billboards, hybrid tree trunk textures and experimental object rendering.
- Bundled FasterNGIO CPU grass caching and combined grass → textures → LOD jobs.
- Incremental LOD generation, existing 3D-tree model support, settings/presets, previews, progress and cancellation.
- Native BC6H texture decoding/repair and Linux path handling that preserves mod-manager configuration.
- Fixes for numeric settings bindings, UI stalls, backend scheduling, invalid grass blades and pipeline failure handling.
- AnvilLOD 0.2.70 occlusion fixes and city grass exclusion; new presets use its updated occlusion sampling defaults.
- PenguLOD branding, independent versioning and one-time migration of existing AnvilLOD settings.
- Automated Linux tests, native backend build/tests, portable packages, source archives and SHA-256 checksums.

### Limits at release

- Linux x64 release target: Ubuntu 24.04 or compatible newer glibc distributions. Other distributions need validation.
- Native terrain/water LOD generation, full TexGen/PBR parity, seasonal billboard generation and GPU grass acceleration remain planned.
- 3D trees require suitable installed LOD models; dense tree packs can produce large geometry budgets.
- The Windows AnvilLOD SKSE plugin is installed separately for dynamic LOD/runtime features. Its game-side names and formats are retained.
- No game assets or generated mod output are included. Grass/texture generation are desktop workflows in this release.

Download the `linux-x64` archive, extract it, and launch `linux-x64/run-pengulod.sh`.
Existing AnvilLOD settings are imported once without changing the original, output paths or cache location.
See the [user guide](https://github.com/opteron-x86/PenguLOD/blob/main/docs/USER-GUIDE.md) for setup.
