# AnvilLOD for Linux

This branch adds a native **Avalonia desktop application** and a native CLI to
AnvilLOD 0.2.68, based on upstream commit
[`84531ba`](https://github.com/CageTV/AnvilLOD/commit/84531ba648ed588835776786c3fe528457861e37).
Both applications call the existing C# engine directly. Wine is not required to
run either generator. The Windows WPF application remains in `src/AnvilLOD.App`.

## Run the desktop application

From this checkout, after building:

```bash
./dist/linux-x64/run-anvillod.sh
```

The portable package is `dist/AnvilLOD-linux-x64.tar.gz`. Extract it anywhere,
keep the entire `linux-x64` folder together, and run `run-anvillod.sh` inside it.
The .NET runtime is included. Once the folder is in its permanent location,
`./install-desktop.sh` optionally adds a per-user application-menu entry; it
requires Python 3. No system installation or root access is needed.

The desktop uses Avalonia's Linux X11 backend. On Wayland desktops it runs through
XWayland. Standard Linux desktop libraries (X11, ICE/SM, fontconfig and ICU) must
be installed. It has been built and exercised on Linux x64/CachyOS. The visible desktop could not be reached from the agent session; rendering and
job execution were verified with the headless platform. Other Linux distributions
have not been manually checked. See Avalonia's
[platform documentation](https://docs.avaloniaui.net/docs/supported-platforms).

## First build

1. On **Overview**, select **Mod Organizer 2** or **Data folder / Vortex**, and the
   game edition (SE/AE or VR).
2. For MO2, choose the directory containing `ModOrganizer.ini`, then **Read profiles**.
   A blank profile uses the selected profile from the INI. MO2 does not need to
   be running: enabled mod folders, loose-file overrides, BSAs and plugin order
   are read directly. Global instances work when their instance folder is selected.
3. If MO2's paths use Windows drives, expand **Advanced source paths** and select
   the Wine/Proton prefix (`pfx`, or its parent compatdata directory). `C:` and
   custom drives use `dosdevices` mappings; without a prefix, `Z:` maps to `/`.
   Native overrides for game, mods, profiles and overwrite folders take precedence.
4. For a deployed Data folder, choose the game/Data directory and `plugins.txt`.
   An empty plugin list is valid for a vanilla game; installed implicit masters
   are added automatically. Undeployed MO2 mods are not present in a plain Data folder.
5. Choose a separate output mod folder. Generation refuses the game folder,
   source roots, MO2 profiles/overwrite, and unrelated nonempty output folders.
   Incremental generation is the desktop default; the explicit **Clear previous
   AnvilLOD output** option requests a clean build.
6. Enter worldspace editor IDs, such as `Tamriel, DLC2SolstheimWorld`, or `*` for all
   worlds with LOD settings. **Scan load order**, inspect the results, then
   **Generate LOD**. Scanning does not write the output mod; it may populate the
   configured billboard cache. Occlusion output is generated only by Generate in the UI.
7. Install the completed output using your mod manager and enable generated plugins.
   Review warnings and missing assets before installing. Cancelled jobs can leave
   partial output; run generation again before installing it.

Steam installation discovery is available under **Preferences**. It checks standard
native/Flatpak Steam roots and the libraries recorded in `libraryfolders.vdf`.
For manually moved games, choose the paths explicitly. No game files, mod lists or
MO2 configuration files are changed by setup or discovery.

## Desktop tools and configuration

### Repairing older output that disables PGLite

An early Linux build could create a literal `textures/d:` folder when a source NIF
contained its author's absolute Windows texture path. PGLite can then fail with
`Unable to list directory: Path not found.: "d:"` while scanning the virtual Data
folder. This is malformed generated output; adding a Wine drive mapping is not the fix.

The generator now resolves these references from `textures\...`, rejects unsafe
output paths, and rebuilds old mesh caches. It also fixes the Grass Optimizations
folder separators. Restart the updated application, enable **Clear previous
AnvilLOD output before generation**, generate again, and **replace** the installed
output mod. Merging into the old mod can leave the malformed folders behind.

For immediate recovery before regeneration, close Skyrim and preview the bundled
repair script (`tools/repair-pglite-paths.py` in a source checkout):

```bash
python3 repair-pglite-paths.py "/path/to/installed/AnvilLOD output" "/path/to/generation/output"
```

Add `--apply` to quarantine the verified placeholder folders. It validates the
AnvilLOD manifest and placeholder bytes, backs up the files outside the mod, and
prints a receipt containing their original paths. It refuses unknown contents or
symlinks. It does not edit meshes, the mod list, PGLite settings, or Wine mappings;
regeneration is still needed to correct baked mesh texture references. Restart
Skyrim through the mod manager and check the new PGLite log after repairing.

### Tools

- **Generation settings:** searchable options for LOD levels, quality rules,
  triangle culling, texture matching, snow/insets, trees and grass, 3D tree models,
  seasons, PBR, native game references, dynamic objects, map LOD, terrain undersides,
  large references and the built-in experimental occlusion solver. Balanced, High
  quality and Objects only presets affect generation choices, keeping source paths.
- **Mesh maker:** loose NIF files/folders, LOD levels, detail reduction, minimum size,
  recommended/custom triangle budgets, owner-plugin rule files and overwrite control.
  Also includes Oldrim-to-SSE mesh conversion from loose files and BSAs.
- **Mod author:** analyze a plugin or all missing tree billboards, then create LOD
  meshes, billboards, rules and optional tree stand-ins in a separate output folder.
  Generated assets are a starting point for visual review.
- **Texture tools:** inspect loose DDS files in selected or enabled MO2 mod folders.
  BC6H-to-BC7 repair uses a managed decoder and AnvilLOD's BC7 encoder, preserving
  dimensions and every mip level. HDR values are clamped to 0–1. Cube maps, arrays
  and volume textures are rejected. Originals are preserved; repaired files go to
  an override mod. Packed BSA textures are not repaired by this tool.
- **Custom rules:** edit mesh/reference rules and save a custom DynDOLOD-format INI.
  First match wins, with the catch-all saved last. The editor writes LODGen entries;
  it does not preserve unrelated INI sections. Save a separate custom copy.
- **Preferences:** worker limit, cache/run-history location, Steam detection and
  JSON settings import/export. Import backs up existing settings. Corrupt settings
  are preserved before defaults are saved.

Settings normally live at `$XDG_CONFIG_HOME/AnvilLOD/settings.json` (default
`~/.config/AnvilLOD/settings.json`). Cache and timestamped run folders use
`$XDG_CACHE_HOME/AnvilLOD` (default `~/.cache/AnvilLOD`), or the preference override.
Each run stores its full log, settings snapshot and result report. The visible log
is bounded to 2,500 lines. Worker count 0 leaves one logical processor available.
Cancel is cooperative; closing during a job cancels and waits for the worker.

Use `AnvilLOD.Desktop --settings /path/to/settings.json` for a separate configuration.
`--smoke-test` opens a native window and exits after two seconds, for installation checks.

## Native grass caching

The **Grass cache** page runs [FasterNGIO](https://github.com/panthuncia/FasterNGIO) offline.
The Linux package includes its native CPU BVH backend; Skyrim, Wine, and a separate download
are not needed for generation. NGIO is still needed to load the cache in game.

1. Select your game source and Fluorine/MO2 profile on **Overview**.
2. On **Grass cache**, choose a separate empty **output mod folder**, such as
   `/mnt/glacier/AnvilLOD Grass`. The app creates `grass/` inside it. Do not select your
   game Data folder, Overwrite, old downloaded cache, texture output, or LOD output.
3. Leave worldspaces at `all`, or enter editor IDs such as `Tamriel`. This selection is
   independent of Overview's LOD worldspace list. Smooth placement is the upstream default;
   Vanilla follows the engine's placement algorithm. Auto collision rejection honors the
   winning NGIO configuration; CPU/GPU explicitly select a rejection backend.
4. Generate. The activity log shows preparation, backend progress and warnings. **Cancel job**
   terminates the backend process and removes its staging files.
5. Install the completed folder as its own mod and disable the previous grass cache mod.
   Replace caches instead of merging them. Keep the old mod available until you have inspected
   the result in game. AnvilLOD does not change NGIO or Community/Bottled Shaders settings.

For a preview, select exactly one worldspace (for example `Tamriel`), tick **Small-area preview**,
and enter cell coordinates and a radius. Radius 0 selects one cell, radius 1 a 3×3 square.
Use a separate preview output. Previews cannot replace a full generated cache or feed a full
LOD run; they are only for inspecting the selected area.

AnvilLOD constructs a temporary Data view from winning files in game Data, enabled mods and
Overwrite. File symlinks avoid copying archives or meshes. Plugin enablement/order, profile
INIs, plugin INIs, `GrassControl.ini`, `*_NGIO.ini` and Seasons configuration are retained.
Source files are not modified. Missing enabled plugins/mod folders abort preparation.

**Advanced** allows a GrassControl.ini override, minimum grass size override (0 inherits
settings), writer threads, experimental render-geometry rejection and an external native
FasterNGIO executable. Worker threads follow **Preferences**. A compatible GPU-enabled
FasterNGIO build can use Vulkan ray tracing; the bundled build supports CPU only. Unsupported
GPU choices fail before replacing output. This integration currently supports SE/AE, not VR.

Every generated CGID is checked for format, finite positions, safe block size and complete
contents. Output includes `AnvilLOD.grass.json`, SHA-256 hashes, generation settings and the
backend log. Only validated output is published. A previous AnvilLOD cache is retained in a
sibling `.previous-<timestamp>-<id>` folder. Failed/cancelled runs preserve it, and modified or
unrecognized output is not replaced. A hard interruption during the directory swap can leave
the previous folder alongside the destination; it contains the recoverable earlier output.

After a full successful run, **Use generated grass cache for LOD** is enabled. LOD reads the
generated cache directly, suppressing older installed grass files. Texture overrides and grass
overrides work together. A changed plugin order or edited generated files requires regeneration.
Also regenerate after changing grass models, landscape or placement settings. This check does
not establish visual placement accuracy; inspect the output in game.

**Grass, textures, then LOD** runs all three steps using their respective settings and separate
output folders. A failed stage stops later stages. Earlier successful outputs remain available.
Keep texture grass billboards disabled if your profile has the missing diffuse textures noted below.

Local verification on the KINMUNE CS profile (2,084 plugins): a 3×3 Tamriel preview produced
9 files / 30,564 instances in about 16 seconds. A full Tamriel run produced 37,056 files
(29,926 empty) / 28,173,502 instances in about 23 seconds with six CPU workers, including
preparation and validation. These are local filesystem benchmarks, not in-game validation.
The 23-second measurement used the workspace NVMe drive and selected only Tamriel;
it is not an estimate for every output drive or worldspace selection.
The backend reported 178 missing grass models affecting 202 grass types and 24 models using
fallback block sizing. These warnings remain visible in the log/results; they were not silently repaired.

The 2026-10-27 follow-up investigated a run that stopped at 32,224 of 37,056 Tamriel
files. The bundled backend now owns its TBB scheduling arena instead of inheriting
an external callback's arena, and queues wakeups with an execution guarantee. A
regression test reproduces the scheduler defect; the exact original live stall
was not reproduced. CLI progress reports cells processed and files written every
five seconds, and warns after two minutes without either advancing during generation
or final writes. Cancelling preserves the previously installed cache.

The updated complete pipeline generated and validated **111,036 files / 141,165,120
instances across 93 worldspaces in 37.5 seconds**, using 31 workers and the workspace
NVMe drive. A Tamriel test with deliberately delayed writes also completed, with
progress visible during the final disk writes. All 125 native tests pass. The
backend additionally omits and warns about non-finite encoded positions/scales:
six instances in two `ksws07World` cells overflowed the cache's half-float height
range. Valid placements and their randomization are unchanged.

The pinned upstream source and dependencies are packaged in
`backends/fasterngio/FasterNGIO-source.tar.gz`, with licenses and build information beside it.
The local CachyOS backend requires **glibc 2.43 or newer**. Other distributions should rebuild
with `tools/build-fasterngio.sh` on their target distribution or select a compatible executable.
The Ubuntu CI configuration builds against its older system libraries; that workflow has not
been run from this workspace.

## Native texture generation

On **Texture generation**, choose your installed **DynDOLOD installation / resources**
folder and a separate **Texture output mod folder**. For the Amaranth installation,
the resource folder is `/mnt/mods/Amaranth/tools/DynDOLOD`. This reads recipes and
render models from the installation; it does not launch TexGen or Wine. Resources
are supplied by the user and are not bundled in AnvilLOD.

- **Generate textures** prepares textures independently and enables their use in
  subsequent LOD jobs after a successful full run.
- **Textures, then LOD** runs the two stages in one job using the source/profile
  from Overview and the LOD options from Generation settings. LOD reads the new
  textures immediately, without an intermediate mod installation. A texture failure
  stops the second stage before it changes LOD output.
- Install the texture output and LOD output as separate mods. Let LOD output win
  file conflicts. Keep existing TexGen output underneath these mods for the
  unsupported features listed below. This is a partial native replacement.

The default selection generates stitched object textures, basic tree billboards and hybrid tree trunks.
Grass billboards and experimental rendered objects are optional. Stitched recipes
follow enabled-plugin order; recipes in the virtual `Data/DynDOLOD` supersede
same-named files from the installation. Copy rules, alternate source paths,
fractional/cropped tiles, intermediate `DynDOLOD-temp` textures, colour multipliers
and matching normal maps are handled natively. Intermediate files are not installed.
If recipes disagree about an atlas size, the final definition wins and a warning
identifies the texture. Output is BC7 DDS with mipmaps; copy rules preserve the
original format. Native decoding also supports legacy 24-bit DDS inputs.

Billboards use winning TREE / tree-flagged STAT / GRAS records, their NIF models,
and diffuse textures. Record texture replacements and billboard inclusion/scale
rules are respected. The software renderer produces diffuse and geometry-normal
maps plus standard `textures/terrain/lodgen` metadata. Minimum height uses the
loaded mesh bounds; resolution can be adjusted with world units per pixel and a
maximum size. Normal maps do not bake the source material's tangent-space details.
Rendered objects use the NIF's TexGen viewport plane and resolution scale.

**Hybrid tree trunk textures** renders the front and side diffuse/normal pairs
(`*_trunk_1.dds`, `*_trunk_2.dds`, and their `_n.dds` maps) natively, with BC7
compression and mipmaps. It reads `_trunk.nif` resources from enabled mods under
`DynDOLOD/Render/Billboards/` or `DynDOLOD/Render/Skyrim/Billboards/`, including
archives, and from the corresponding folders in the optional DynDOLOD installation.
Enabled mod resources override the standalone bundle. No TexGen, Wine, game process
or graphics context is needed for this step.

The installed hybrid meshes under `meshes/dyndolod/lod/trees` provide the two
camera directions, texture orientation and independent crops. This preserves the
alignment between each trunk and its existing 3D crown, including offsets and
mirrored UVs. Render resources without a matching installed hybrid are not rendered.
Missing resources are reported, with existing installed textures still available
to LOD. Unsupported/skinned trunk geometry, conflicting crops and missing source
textures prevent publication instead of producing an incomplete trunk. The adjacent
`.txt` file records the native renderer's view sizes, offsets and orientation for
diagnostics; it is not a replacement hybrid-model authoring tool.

To enable this for existing output, restart the updated desktop, leave **Hybrid
tree trunk textures** checked on **Texture generation**, enable **3D tree LOD**
in **Generation settings**, and choose **Textures, then LOD**. Keep the other
texture categories selected if you want to retain those generated outputs: each
successful run publishes a complete replacement for the previously owned set.
The existing grass cache can be reused. Install both outputs with LOD output
winning conflicts. These are hybrid models (3D crowns with crossed trunk cards);
LOD4 uses them by default when enabled, with LOD8 optional and billboard trees at
farther levels. Full tree meshes and new hybrid models are not synthesized.

Grass selection uses the grass lists on winning landscape texture records, including
seasonal replacement landscapes. Unreferenced library definitions are skipped;
an explicit billboard rule or a model-filter preview can still select them.
Object templates from the standalone resource bundle update matching installed
LOD textures, so resources for absent games or mods are skipped. If an optional
template has missing source textures or unsupported shapes, a decodable existing
diffuse/normal pair of matching dimensions is copied unchanged and reported as
**Retained** with a warning; current brightness settings do not affect retained
textures. Missing or invalid fallback pairs still fail. Mod-provided templates
and filtered previews remain strict and can create new texture destinations.

If a combined grass/texture/LOD run fails after grass completes, correct the
texture issue and choose **Textures, then LOD** to resume with the completed cache.
The combined run saves both grass and texture reports alongside its activity log.

Use the model filter and **Preview selected texture** to inspect small test runs.
A filtered run must use a separate output folder and cannot feed a LOD job.
Clear the filter for a full run. Brightness, sizes, categories and filters are
saved in presets alongside the source and profile.

Each run writes `AnvilLOD.textures-report.json`; a successful run also publishes
`AnvilLOD.textures.json`, which records owned files and their hashes. Work is staged;
failed rendering and cancellation preserve the previous published textures.
Regeneration replaces only owned, unmodified files and removes their stale outputs.
Publication attempts rollback on an I/O failure. A process killed during publication
can leave an incomplete manifest; use a fresh output folder if the next run reports
changed files. Keep incomplete output disabled. LOD verifies the manifest and includes
its content fingerprint in incremental-build settings, so changing generated
textures rebuilds affected work instead of reusing an old build blindly.

This is **not full TexGen feature parity**. PBR material baking, TexGen's special
`x`-format glow/alpha conversions, HD billboards, seasonal billboard variants,
complex-grass material shading and reference-scale discovery are not implemented.
Models with unsupported/skinned shapes are skipped as a whole, rather than
producing billboards with missing branches or leaves; rendered objects with
unsupported shapes fail explicitly. Existing TexGen billboards remain useful for
these models. No partial mesh render is presented as a complete replacement.
Unsupported shader-conversion recipes are listed as skipped; missing source textures
and rendering failures prevent publication. The native page reports results and
warnings without claiming these features were generated.

Grass is off by default. Its native pass uses the current landscape grass lists;
obsolete library records are skipped, while a selected model with missing diffuse
textures still requires its source assets. This does not modify the profile or
repair source mods.

## Windows compatibility

[Avalonia supports Windows](https://docs.avaloniaui.net/docs/supported-platforms).
The new desktop project targets ordinary `net10.0`, uses `UsePlatformDetect()`,
and shares the managed engine with the CLI. Its UI and native texture code remain
suitable for a Windows build. The distributed binaries, launcher and packaging
scripts here target Linux x64; a Windows build, platform testing and packaging
are separate work and have not been performed. The upstream WPF UI is also retained.

## Build and tests

Requirements: .NET 10 SDK, Bash and Python 3. Bundling FasterNGIO also needs GCC 14+ (C++23),
CMake 3.28+, Ninja, curl, patch, pkg-config and development packages for zlib, lz4, TBB,
spdlog/fmt and GoogleTest. `tools/build-fasterngio.sh` downloads checksum-pinned sources into
`.tools`, applies two small patches (headless CLI and the pinned TESWorld No Grass flag API),
builds the backend, runs its upstream tests and bundles its runtime libraries. From the repository root:

```bash
bash tools/test.sh
bash tools/build.sh
bash tools/native-smoke.sh  # needs an X11/XWayland display
```

Scripts prefer `.tools/dotnet/dotnet`, then `dotnet` on `PATH`. Set
`ANVILLOD_DOTNET` to another SDK executable, or `ANVILLOD_NUGET_SOURCE` to an offline
feed. Restored packages and SDK state are kept under `.tools/` by default.
`AnvilLOD.Linux.slnx` excludes WPF and includes the desktop, CLI and both test projects.
The original upstream solution still includes Windows-only WPF.

`tools/build.sh` publishes both self-contained apps into `dist/linux-x64`, collects
package license notices, and creates the portable archive. `tools/test.sh` runs
engine tests and Avalonia headless tests using actual Skia rendering. Screenshots
are saved under `.artifacts/screenshots`; TRX reports go to `.artifacts/test-results`.
The GitHub Actions workflow builds, tests and checks native X11 startup on Ubuntu;
it has been added here but has not been run on GitHub from this local branch.

For an optional real-game desktop test, export a JSON preset with an isolated
source, output directory and cache directory, then run:

```bash
bash tools/test.sh --smoke-settings /path/to/isolated-preset.json
```

When the preset supplies `TextureGenOutput`, that test first runs texture generation
and feeds it directly into LOD. It generates through the desktop job runner, parses the resulting BTOs,
and verifies an incremental rerun. It **writes to the output and cache folders in
the preset**. Use a dedicated test output; do not select a live output mod.

## CLI

```bash
./dist/linux-x64/AnvilLOD --help
./dist/linux-x64/AnvilLOD scan \
  --mo2 /path/to/MO2 --profile Default \
  --wine-prefix /path/to/compatdata/489830/pfx \
  --worldspace Tamriel --threads 4 --report reports/scan.json
```

The CLI adds `--wine-prefix`, `--game-ini`, `--threads`, `--cache-folder` and Ctrl+C
cancellation. Native filesystem overrides remain available as `--mo2-game`,
`--mo2-mods`, `--mo2-profiles` and `--mo2-overwrite`.
Unlike the desktop, the upstream CLI retains its clean-generation default: add
`--keep-output` for incremental reuse. A scan with no matching worldspaces or a
build with failed blocks returns a nonzero exit code. `fix-textures --apply` now
uses native managed conversion by default; an explicit `--texconv` can select a
compatible external executable. Windows `.exe` converters are rejected on Linux.

## Verification and limits

Local verification on 2026-10-27 (earlier baseline results are identified below):

| Check | Result |
| --- | --- |
| Native engine, filesystem and texture-generation tests | 474 passing |
| Desktop controls, settings, cancellation, texture preview and real-game combined job | 9 passing |
| KINMUNE CS native stitching (64-pixel base) | 592 published files, 0 failures; unsupported conversions reported |
| Default stitching + tree generation on KINMUNE CS | 3,097 published files; unsupported inputs and skipped models recorded |
| Native render previews | Aspen billboard and Windhelm atlas visually inspected |
| Combined texture-to-LOD desktop test | 75 BTO files; all parsed; second run 0 rebuilt / 75 unchanged |
| Earlier MO2-style isolated input | Five base/DLC masters, 17 archives, 1,138 Solstheim LOD references |
| Earlier object generation through desktop | 78 BTO files; all parsed successfully |
| Earlier incremental desktop generation | 0 rebuilt, 78 unchanged, no failed blocks |
| Earlier object geometry readback | 447,240 triangles decoded from 78 blocks |
| Plain Data-folder scan | 10 installed implicit plugins, 22 archives, expected 78-block plan |
| Native texture repair | Signed and unsigned BC6H, preserved mip chain, source unchanged |
| Live native startup in this agent session | X11 display access unavailable; not validated on the visible desktop |
| UI rendering | Eight pages, compact window, texture preview and completed-generation views inspected |

The combined LOD tests read the installed game through a dedicated empty MO2-style profile under
`.artifacts/vanilla-probe`. Texture-only checks also read KINMUNE CS directly.
Generated assets, logs and reports stayed in the checkout.
The user's game, mods, load order and real MO2 profile were not changed.

This is a working local Linux port, not a claim of full in-game parity:

- The Windows **AnvilLOD SKSE DLL** still runs inside Skyrim under Proton/Wine.
  It is not included in this local Linux package. Install the matching upstream
  game plugin separately for dynamic LOD, parent copies and related runtime features.
  Choosing SKSE installation **None** means separately installed; it does not turn
  those features off. The Objects only preset disables the runtime-dependent features.
- Terrain LOD generation and GPU object-texture atlasing are unfinished upstream
  features. The desktop exposes the existing CPU engine and terrain-underside feature.
- Full modlist performance, seasons, PBR, grass/tree integration, experimental
  occlusion and in-game visuals still need testing against the intended mod setup.
  VR is selectable and uses the shared engine, but no VR installation was available
  for an end-to-end check.
- The existing external Windows xLODGen launcher has not been ported. The desktop
  uses the built-in occlusion calculator.

The project retains [GPL-3.0-only](../LICENSE.txt). Avalonia and BCnEncoder.NET
license texts and Inter font licensing are included under `assets/licenses` and
in the portable package. No release, remote commit or mod installation has been made.
