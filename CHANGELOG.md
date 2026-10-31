# Changelog

PenguLOD uses its own semantic version, independent of AnvilLOD’s game plugin version.
Pre-release versions are marked alpha or beta until the supported workflows have broader validation.

## Unreleased

- Rebuild large-reference lists from source plugins even when previous generated output
  remains enabled. An installed `AnvilLOD.esm` or `PenguLOD.esm` no longer causes the
  replacement master to be omitted. Current source overrides and other mods' lists
  still determine which references qualify.

## [0.4.0-alpha.13] - 2026-10-30

- Prevent previous output from feeding back into object LOD. Generated plugin references,
  terrain undersides and generated object models are excluded as sources. Preflight detects
  installed output plugins even when generation writes to a different folder. Regenerate
  object LOD if an earlier automatic-object run included previous output.
- Keep static architecture when separate glow, smoke, animated effects or shaderless layers
  cannot be drawn as static LOD. Report each omission; opaque unsupported materials and
  damaged geometry still reject the model. Empty legacy geometry links no longer reject
  otherwise valid structures. Texture and tree rendering retain their stricter checks.
- Reduce redundant foliage on installed trees below their hard triangle budgets. The new
  geometry target defaults to 65%; sampled silhouette, coverage and material constraints
  may retain more. Desktop, saved settings and CLI expose the target.
- Stage only retained object blocks instead of copying blocks scheduled for rebuild or
  deletion. Record output hashes during block writes and retained-file copies, avoiding
  another full read of those files before publication. Resume still verifies published
  contents; changed retained blocks with saved hashes are rebuilt.

Install the matching texture output alongside object LOD. Generating textures does not
automatically update an installed copy in a mod manager. Terrain and grass caches do not
need regeneration for these fixes.

## [0.4.0-alpha.12] - 2026-10-30

- Discover compatible rules, recipes, mesh lookups, world exclusions, child-world settings
  and render models in enabled mods and resource-only folders. Structured Data/DynDOLOD
  folders and legacy layouts work without any executable requirement.
- Scan-only requests no longer start occlusion generation or terrain preparation for
  an enabled underside/occlusion option. These operations require generation.
- Multiple external packs have explicit priority, with enabled mods winning. Overview and
  JSON reports identify selected files and replaced sources; preflight explains missing data.
- Desktop, presets, JSON and CLI expose additional resource folders. Resource content
  changes invalidate saved jobs; unrelated bundle executables are not resource inputs.

## [0.4.0-alpha.11] - 2026-10-30

- Optional native hybrid tree authoring creates aligned front/side trunk cards and normal
  textures from full models, combined with reduced 3D crowns. Installed LOD models retain
  precedence; ambiguous material separation or unavailable renders keep native geometry.
- Both cards count toward tree budgets. Record/season variants have separate verified
  resources; damaged private caches are repaired. Used trunk textures ship with LOD output.
- Desktop, presets, JSON and CLI expose hybrid authoring and trunk resolution controls.
  Tree coverage reports distinguish native hybrids, geometry reduction and billboard fallback.

## [0.4.0-alpha.10] - 2026-10-30

- Optional automatic object LOD fills missing levels requested by active rules, preserving
  installed models and explicit exclusions. Native generation applies per-level budgets,
  minimum size and reduction controls; unsupported sources skip with model-specific reasons.
- Generated meshes participate in record/season texture matching and optional object
  atlases. Content-keyed private caches verify generated files before reuse and repair damage.
  Overview, scan reports and `PenguLOD.objects.json` show coverage and costs.
- Desktop, presets, JSON requests and CLI flags expose the same controls. CLI scans now
  honor JSON LOD requests and preset worker/cache settings while always disabling publication.

## [0.4.0-alpha.9] - 2026-10-30

- Native tree and grass billboards can render separate front/side diffuse and normal
  maps. Object-LOD cards consume each view with its own dimensions; standard BTT tree
  LOD retains the front view. Origin-centered framing keeps off-center crowns aligned.
- Tree texture resolution follows the largest winning reference scale, with an unplaced
  default and explicit rule overrides. It does not change physical tree dimensions.
- Packed complex-grass color and normals are detected per material and split before
  resizing. Grass normal atlases use normalized mipmaps, independent of specular alpha;
  the full runtime grass shader is not reproduced.
- Desktop, saved presets and CLI requests expose HD views, scale discovery, grass layout
  and detection threshold. Regenerate textures followed by LOD; grass placement caches
  do not need regeneration for these settings.

## [0.4.0-alpha.8] - 2026-10-30

- LOD scans honor deterministic Base Object Swapper removals that replace references or
  base objects with Skyrim's invisible XMarker. Removed objects stay out of tree/object
  LOD, child-world copies, generated runtime references and new large-reference entries.
- Winning enabled `*_SWAP.ini` files are processed in BOS filename order. Reference
  swaps override base swaps; ambiguous conditional/chance replacements do not become
  blanket removals. Scan reports identify removed references and unsupported entries.
- The BOS runtime must be present in the selected Data view. Visible substitutions,
  property-only edits, seeded chance evaluation and MergeMapper remapping are not yet
  supported. Regenerate LOD after changing swap configurations.

## [0.4.0-alpha.7] - 2026-10-29

- Missing recipe texture replacements now retain the affected material's supplied LOD
  textures and UVs. Valid matches on other materials and record/season variants continue.
- Fallback warnings identify the model, shape and missing file in the run log, pipeline
  receipt and `PenguLOD.recipes.json`. Repeated uses of the same fallback warn once.
- A fallback requires the supplied LOD textures to exist. Corrupt texture data, broken
  recipe inputs, cycles and conflicting mappings still fail safely. Installing missing
  replacements invalidates affected cached output; recipe-mapped blocks rebuild after
  this update.

## [0.4.0-alpha.6] - 2026-10-29

- Fixed full-model texture matching for atlases made from one repeated source, including
  meshes whose UVs wrap beyond the atlas rectangle. Record texture variants now rebuild
  those atlases while preserving the LOD mesh's UVs.
- Full shapes already using an atlas with identical texture slots no longer substitute
  that atlas into its own source tiles or cause cyclic normal-map recipe failures.
- Duplicate full-model shape names can be resolved by a unique matching LOD material.
  Ambiguous record overrides and distinct contributors to merged shapes remain protected.
- Multiple-source layouts still require an unambiguous source tile, and missing required
  replacement textures remain errors. Recipe-mapped blocks rebuild after this update.

## [0.4.0-alpha.5] - 2026-10-29

- Grass LOD atlases use only types with instances in the selected cache cells, including
  matching seasonal cells when seasons are enabled. Unused grass-library records can no
  longer fail atlas generation because their textures are absent.
- Atlas preparation reads cache type headers and checks payload bounds without decoding
  every blade. Used models absent from GRAS records still receive model-based billboards.
- Grass atlas preparation now runs before occlusion generation, so required grass asset
  failures are reported before the long occlusion pass.

## [0.4.0-alpha.4] - 2026-10-29

- Fixed vanilla and legacy grass meshes using Bethesda's `\bNOR` normal-map marker.
  Native rendering uses geometry normals instead of treating the marker as a file path.
  Other invalid texture paths remain errors.
- Automatic tree billboard discovery reports and excludes whole trees with unsupported
  animated, blended or view-dependent effect materials. Explicitly requested renders,
  required grass/trunk/object resources and malformed geometry still fail safely.
  No partial tree billboard is published.

## [0.4.0-alpha.3] - 2026-10-29

- Added native triangle-strip reading, self-contained static skin poses, SSE skin
  partition buffers and separate dynamic vertex positions on Linux and Windows.
- Scene traversal honors active switch branches, avoiding duplicate static/animated
  tree geometry. Invalid indices, weights and truncated blocks report precise failures.
- Texture rendering supports static effect textures, tint, UV transforms and lighting
  influence. Animated, blended and view-dependent effect resources remain unsupported.
- Incomplete visible models cannot publish partial LOD or required texture output.
  Deep diagnostics report rejected geometry with its source. Existing texture output
  is preserved when a required model fails; optional valid texture pairs can be retained.
- Native full-tree reduction uses plain diffuse/normal LOD materials, avoiding inherited
  PBR, environment, parallax and glow settings. Authored passthrough materials are preserved.
- Geometry and automatic billboard cache versions changed; affected LOD must regenerate.
  Animation evaluation and external skeletons are not supported; in-game visual validation
  remains ongoing.

## [0.4.0-alpha.2] - 2026-10-29

- Added a self-contained Windows x64 ZIP with the Avalonia desktop, CLI, native
  grass backend and optional Vulkan texture helper. Linux portable and AppImage
  packages continue to use the same application source and version.
- Releases require Linux and Windows engine/UI tests, native backend tests and
  extracted-package startup checks before publishing either platform.
- Native dependencies are selected for the target OS. Windows builds retain UTF-8
  path support and use Windows configuration/cache locations. Grass process tests
  cover success, cancellation and recovery on both hosts, including Unicode paths.
- Windows grass staging uses hard links with a cancellable copy fallback, avoiding
  a requirement for administrator rights or Developer Mode. Steam discovery also
  reads the default Windows Steam installation and its library manifests.
- Output locks account for Windows path casing, preventing two jobs from targeting
  the same directory through differently capitalized paths.
- Windows desktop modlist testing and physical GPU validation remain open work.

## [0.4.0-alpha.1] - 2026-10-29

- Native TexGen `x` recipes retain source dimensions and apply the specified mipmap
  filter, progressive contrast/brightness and alpha handling. Copy and conversion
  dependencies support source alternatives, intermediates and in-place conversions.
- Object LOD follows recipe UV layouts, including repeated/cropped tiles and nested
  copy/stitch chains. Record and seasonal texture overrides rebuild isolated atlas
  variants, preserving unrelated tiles, normals and original atlas UVs.
- Normal-only overrides work on full textures and stitched atlases. Normal-map reduction
  averages directions independently of gloss alpha. Native authoring preserves shape
  names needed for subsequent material matching.
- Texture manifests retain validated recipes and original in-place conversion inputs.
  LOD fingerprints include recipe/source content; reports explain mapped, variant,
  retained and failed materials. Ambiguous full-texture layouts retain the full texture;
  conflicting atlas replacements stop the combined pipeline and preserve previous output.
- Synthetic tests cover conversions, merged shapes, UV coverage, seasonal output,
  object atlasing, changed sources, reuse and recovery. Full HD billboard/render parity
  and broad in-game compatibility remain separate work.

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
