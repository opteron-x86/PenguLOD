# PenguLOD user guide

PenguLOD generates Skyrim LOD on Linux and Windows from installed game and mod assets.
The desktop, CLI, texture processing and bundled CPU grass backend run natively.
On Linux, Skyrim and its game-side plugins still run through Proton/Wine.

## Install and launch

Download a portable archive, Windows ZIP or Linux AppImage from
[Releases](https://github.com/opteron-x86/PenguLOD/releases).
On Windows, extract the entire ZIP into a new directory and launch `win-x64/PenguLOD.Desktop.exe`.
On Linux, extract the archive into a new directory, keep the `linux-x64` folder together,
and run `linux-x64/run-pengulod.sh`. After moving it to its permanent location,
`linux-x64/install-desktop.sh` can add an application-menu entry; this requires Python 3.
The .NET runtime is included. See [package compatibility](PACKAGING.md) for requirements
and AppImage instructions.

## Select the source

1. On **Overview**, select **Mod Organizer 2** for Fluorine/MO2 profiles, or
   **Data folder / Vortex** for a deployed game. Choose the game edition.
2. For Fluorine/MO2, select the instance directory containing `ModOrganizer.ini`, then
   **Read profiles** and select a profile. A blank profile uses the instance's selected
   profile. PenguLOD reads enabled mods, archives, loose-file overrides, Overwrite and
   plugin order directly; the mod manager does not need to be running.
3. On Linux, if the instance uses Windows paths, expand **Advanced source paths** and select its
   Wine/Proton prefix (`pfx`, or the containing compatdata directory). `C:` and custom
   drives use the prefix's `dosdevices` mappings. Without a prefix, `Z:` maps to `/`.
   Native game, mods, profiles and overwrite path overrides take precedence. On Windows,
   native drive paths work directly without a Wine prefix.
4. For a Data-folder source, select the game/Data directory and `plugins.txt`. An empty
   plugin list is valid for a vanilla game; installed implicit masters are added.
   Only deployed mods are visible in this mode.
5. Select worldspace editor IDs such as `Tamriel, DLC2SolstheimWorld`, or `*` for worlds
   with LOD settings. **Scan load order** shows coverage before LOD generation.

Steam discovery is available in **Preferences**. It reads standard native/Flatpak Steam
locations on Linux, `Program Files (x86)\Steam` on Windows, and their library manifests.
Choose explicit paths for Steam installed elsewhere or for other game installations.

Compatible rules, texture recipes and render models can come from enabled mods or optional
standalone [resource packs](RESOURCE-PACKS.md). No DynDOLOD executable is required.

## Choose stages and outputs

Each stage needs a separate output mod folder. Use empty folders outside the game and
source directories, for example `~/PenguLOD-output/Grass`, `Textures`, `Terrain` and `LOD`.
The application rejects conflicting source/output locations and unrelated nonempty folders.

| Stage | Purpose | Guide |
| --- | --- | --- |
| Grass cache | Offline grass placement for the game and grass LOD | [Grass caching](GRASS.md) |
| Textures | Stitched textures, billboards and hybrid tree trunk textures | [Texture generation](TEXTURES.md) |
| Terrain | Landscape/water meshes, terrain textures and optional shadow heightmaps | [Terrain](TERRAIN.md) |
| LOD | Object, tree and grass LOD plus enabled runtime features | [Objects](OBJECTS.md), [trees](TREES.md), [materials](MATERIALS.md), [atlases](ATLASES.md) |

Select the required stages on **Pipeline** to run them in order: grass → textures →
terrain → LOD. Successful outputs feed later stages directly. The individual pages
can run a single stage; existing generated inputs must be valid when they are reused.
See [persistent generation](PIPELINE.md) for resume, verification and output backups,
and [CLI commands](CLI.md) for the equivalent command-line workflow.

Inspect preflight findings before generation. Review the completion report for missing
models, retained textures and billboard fallbacks. A successful run can still contain
these reported limitations. **Diagnostics** can identify the winning source of an asset
and export a report suitable for a bug report; see [diagnostics](DIAGNOSTICS.md).

## Install generated output

Install each completed output as a separate mod and enable any generated plugins.
Texture output should override its source textures; LOD output should win conflicts with
the texture output. Terrain and grass output should override older output of the same
type. Replace superseded output rather than merging it, so obsolete files do not remain.
Use the selected mod manager's normal conflict and plugin-order controls.

Dynamic LOD and runtime quality controls require the separate **AnvilLOD SKSE plugin**
matching the game runtime. Generator packages do not include that game DLL. See
[dependencies and compatibility](PROVENANCE.md) for its source and version relationship.
Choosing SKSE installation **None** means the plugin is installed separately; it does
not disable runtime-dependent features. The **Objects only** preset disables those features.

After installation, check nearby-to-distant transitions, shorelines, tree silhouettes
and brightness using the [in-game checklist](DIAGNOSTICS.md#repeatable-checks).
[Seasons](SEASONS.md) require matching installed swap configuration and runtime support.

Base Object Swapper removers are detected from enabled root `*_SWAP.ini` files when
`SKSE/Plugins/po3_BaseObjectSwapper.dll` is installed. Deterministic swaps to Skyrim's
invisible XMarker are excluded from LOD. This covers reference-specific tree and clutter
removers without excluding every instance of the same model. Regenerate **LOD** and
replace the previous LOD output after enabling or disabling such a remover; textures,
terrain and grass caches do not need rebuilding for a tree-only removal.
See [swap diagnostics and limits](DIAGNOSTICS.md#base-object-swapper) for other BOS rules.

## Settings and tools

- **Generation settings** controls LOD levels, trees, grass, rules, texture matching,
  runtime features, seasons and experimental occlusion. Presets keep source paths.
- **Mesh maker** creates reduced LOD meshes and rule files, and converts loose or
  archived Oldrim meshes. **Mod author** can create meshes, billboards and stand-ins
  in a separate output folder. Inspect generated assets before distribution.
- **Texture tools** inspects loose DDS files and repairs BC6H textures into a separate
  override folder. Dimensions and mip levels are preserved; HDR values are clamped
  to 0–1. Cube maps, arrays, volume textures and packed BSA repair are unsupported.
- **Custom rules** writes DynDOLOD-format LODGen entries, with the first matching rule
  taking priority. Save a separate custom INI; unrelated sections are not preserved.
- **Preferences** controls workers, cache location, presets and optional
  [Vulkan texture encoding](COMPUTE.md). Worker count 0 leaves one logical processor free.

On Windows, settings use `%APPDATA%\PenguLOD\settings.json`; caches and run history use
`%LOCALAPPDATA%\PenguLOD`. On Linux, settings use `$XDG_CONFIG_HOME/PenguLOD/settings.json` (normally
`~/.config/PenguLOD/settings.json`). Cache and run history use `$XDG_CACHE_HOME/PenguLOD`
(normally `~/.cache/PenguLOD`) unless overridden. Each run saves logs, settings and reports.
These local files contain paths and profile information; review them before sharing.

On first launch, an existing AnvilLOD configuration is imported once, preserving its
paths, cache location and original file. Existing PenguLOD settings take precedence.
`PenguLOD.Desktop --settings /path/to/settings.json` uses an isolated configuration
and disables automatic migration. Preset imports back up the existing settings.

See [troubleshooting](TROUBLESHOOTING.md) for failed runs and [the roadmap](ROADMAP.md)
for planned improvements. Source builds and tests are documented in
[Contributing](https://github.com/opteron-x86/PenguLOD/blob/main/CONTRIBUTING.md).
