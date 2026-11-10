# Upgrading to PenguLOD output

From 0.6 onward, PenguLOD uses its own names throughout the generator and game runtime.
This changes filenames and plugin identity. It does not change your quality settings.

## Keep the working setup

Keep your current generated mods, settings preset and a save from before the switch.
Existing PenguLOD settings retain their values and paths. Settings from the older AnvilLOD
app can still be imported, leaving the original file intact.

Choose new, empty output folders for the stages you regenerate. PenguLOD refuses to generate
into an AnvilLOD-named output set or publish over one. Do not rename generated ESM/ESP files:
records and runtime data refer to those filenames.

For the first complete switch:

1. Save your current settings as a preset. Change only the output folders you need for the new run.
2. Disable previous object LOD output before scanning. Keep your source mods and LOD resource packs enabled.
3. Generate textures, terrain and object/tree/grass LOD with the same quality settings. A valid existing
   grass cache can be reused; changing the product name does not require new grass placement.
4. Verify the finished output in **Reports & outputs**, then install it as a separate set of mods.
   Disable the previous generated set when enabling its replacement. Do not merge the two sets.
5. Install the **PenguLOD SKSE** ZIP from the PenguLOD release page. Select your Skyrim version in
   its installer. Disable `AnvilLOD.dll` before enabling `PenguLOD.dll`; use only one LOD controller.
6. Enable the generated plugins and test the same viewpoints, transitions and performance as before.
   Keep the previous setup until you are satisfied.

Swapping generated plugins changes the active plugin list. Test on a separate save and retain the
pre-switch save for rollback; this rename is not a guarantee that an existing save can safely lose
plugins. If reverting, restore the matching old output, runtime and save together.

## Names you will see

| Item | New name |
| --- | --- |
| Large-reference master | `PenguLOD.esm` |
| Runtime placements and terrain support | `PenguLOD.esp` |
| Split effects plugins, when needed | `PenguLOD Effects*.esp` |
| Game runtime and settings | `SKSE/Plugins/PenguLOD.dll` and `PenguLOD.ini` |
| Runtime data | `SKSE/Plugins/PenguLOD/` |
| Product-owned mesh and texture folders | `meshes/pengulod/` and `textures/pengulod/` |
| Object, texture and grass manifests | `PenguLOD.manifest.json`, `PenguLOD.textures.json`, `PenguLOD.grass.json` |

Plugins are emitted only when the selected features and load order need them. A missing optional
plugin does not by itself mean the generation failed. Check the run summary.

## Older files and settings

The new runtime can read an older AnvilLOD output set when no PenguLOD runtime-data folder is
installed. It also reads `AnvilLOD.ini` if `PenguLOD.ini` is absent. Saving settings from its menu
writes a new `PenguLOD.ini` while preserving the old file. If you have customized the old INI,
copy it to `PenguLOD.ini` in your runtime mod so it overrides the package defaults.

Older grass and texture manifests remain readable as inputs, and performance reports can inspect
older generated meshes. New output always uses PenguLOD names. The previous AnvilLOD runtime
cannot read the new data, so update the runtime before testing a new generation.

Skyrim's standard LOD layout and supported mod formats keep their existing names. Resources under
`Data/DynDOLOD` still work. Upstream credits, licenses and historical release notes retain AnvilLOD's name.
