# Resource packs

PenguLOD reads compatible data directly. No DynDOLOD or TexGen executable, Wine session,
or installed application is needed. The paths and file names identify data formats;
resources distributed under `Data/DynDOLOD` can be consumed natively.

Enabled mods are searched automatically through the selected Data/Fluorine/MO2 view.
For external packs, set **Primary resource folder (optional)** and **Additional resource
folders** on Generation or Texture generation. Enter additional folders one per line.
The settings are shared between texture, LOD and mod-author tools and saved in presets.

## Layouts

A selected folder can contain `Rules`, `Configs` or `Render` directly, or wrap them in
`DynDOLOD`, `Data/DynDOLOD` or `Edit Scripts/DynDOLOD`. Only one recognized resource root
is used per selected folder, in that search order: `Edit Scripts/DynDOLOD`, `Data/DynDOLOD`,
`DynDOLOD`, then the selected folder itself. Use separate entries for separate roots.
Directory and file names are matched without regard to case on Linux and Windows.

| Resource | Recognized location below the resource root |
| --- | --- |
| LOD rules | `Rules/DynDOLOD_SSE_*.ini` or legacy flat `DynDOLOD_*.ini` |
| Texture recipes and billboard rules | `Configs/DynDOLOD_*_TexGen_*.txt` or flat files |
| Mesh lookup | `Configs/DynDOLOD_SSE_mesh_lookup.txt` or flat file |
| World exclusions | `Configs/DynDOLOD_SSE_mod_world_ignore.txt` or flat file |
| Child-world copies and matches | `Configs/DynDOLOD_SSE_childworld_*.ini`, `Configs/DynDOLOD_SSE_ChildworldMatches.txt`, or flat files |
| Object render models | `Render/Objects/**/*.nif` or `Render/Skyrim/Objects/**/*.nif` |
| Hybrid trunk render models | `Render/Billboards/**/*_trunk.nif` or `Render/Skyrim/Billboards/**/*_trunk.nif` |

Enabled mods use the same layouts under `Data/DynDOLOD`. Legacy flat data files in the
Data root are also accepted. Rules and recipe files still need the expected plugin-based
names and supported syntax; discovery does not enable recipes for absent plugins.
See [textures](TEXTURES.md), [model support](MODEL-SUPPORT.md) and [trees](TREES.md) for limits.

External folders supply configuration and render templates. Their ordinary `meshes` and
`textures` folders are not mounted into the game view. Install required game assets through
the selected mod manager or deployed Data folder. A pack need not contain all resource
categories, and PenguLOD does not bundle third-party packs or alter their redistribution terms.

## Precedence and reporting

For the same logical resource, later entries replace earlier ones:

1. Primary external resource folder.
2. Additional external folders, in their listed order.
3. Winning enabled Data-root files.
4. Winning files under `Data/DynDOLOD`, with structured `Rules`/`Configs` paths replacing flat aliases.

Loose/archive and enabled-mod priority are resolved by the normal asset index first.
For render aliases, later external packs win; enabled mod templates override external
ones. Within the same layer, `Render/Objects` and `Render/Billboards` take precedence
over the legacy `Render/Skyrim` versions. A custom LOD rule file remains the highest
rule priority. File discovery and rule evaluation order are separate: plugin order and
the selected preset still determine which rules match.

Overview's **Resource files** table lists winning logical files and their sources.
Scan JSON and texture reports include `resources`/`Resources`; generated LOD also contains
`PenguLOD.resources.json`. Replaced sources are listed in `Overridden`. These are private
run reports and may contain local paths. Use Diagnostics for a redacted report to share.

Missing stitching recipes or object render models produce actionable preflight errors
when those categories are selected. Invalid explicit folders are rejected; an empty
resource setting is valid when enabled mods supply the needed data. Resource folders
must be separate from output folders. Saved jobs fingerprint recognized external data
files, including render models, so changes rebuild affected stages. Unrelated executables
and folders are ignored. Source files are never modified.
