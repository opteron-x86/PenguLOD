# Seasonal generation

Enable **Seasons** for LOD and **Generate seasonal terrain** on the Terrain page; they
share the same preset setting. Generate tree textures first using the same profile.
Texture generation includes winning replacement TREE records, even when those records
are not directly placed in the default world.

PenguLOD reads `Data/Seasons/*_WIN.ini`, `_SPR.ini`, `_SUM.ini` and `_AUT.ini` in name order.
Later entries override earlier ones, including identity entries that undo a swap.
`[Trees]` and `[LandTextures]` accept EditorIDs and `0xLocalID~Plugin.esp` references.
Unknown replacement records stop generation. Missing seasonal tree billboards tell you
to regenerate textures instead of silently substituting the default tree.

For each configured season, terrain emits `.WIN.btr`, `.WIN.dds`, `.WIN_n.dds` (and the
equivalent other suffixes). Tree LOD emits seasonal `.btt`, `.lst`, and tree atlas files.
Trees in object LOD receive replacement 3D models within the configured geometry budgets,
or replacement billboard cards. The native full-model fallback respects its setting.
Object swaps validate both the record type and its required LOD models. Failed seasonal
blocks stop publication, preserving the previous LOD output. Grass uses the matching
seasonal `.cgid` files and their GRAS billboards. Filenames follow the
[Seasons of Skyrim loader](https://github.com/powerof3/SeasonsOfSkyrim/blob/master/include/LODSwap.h).

Terrain shape/water heights do not change with a material swap, so terrain-shadow
heightmaps are shared. Areas without source LAND use installed seasonal terrain textures
when available and otherwise retain default terrain, with a warning. Missing landscape
data cannot be reconstructed as seasonal layers from a baked default texture.

Runtime-generated winter swaps must first be saved by Seasons of Skyrim as INIs and
available to the selected profile. PenguLOD does not execute SKSE or recreate the runtime's
automatic snow rules. Dynamic shader snow remains a runtime effect.

Follow [the transition checks](DIAGNOSTICS.md#repeatable-checks) in each configured season.
Compare loaded-cell textures and trees with the corresponding distant output.

Diagnostics lists resolved and unresolved entries by season and record category. Unknown
entries affecting a selected stage block that stage; entries for unselected categories
are warnings. The report also counts installed seasonal grass cells and identifies gaps:
a missing seasonal cell retains default grass, while a seasonal cell without a default
cache cannot enter the LOD grid. Rebuild grass caches before generating LOD when these
counts differ unexpectedly.

After a successful pipeline, `seasonal-output.json` in the saved job folder counts terrain
tiles, object blocks, tree blocks and generated grass cells for Default/WIN/SPR/SUM/AUT.
Tree blocks are zero when trees are entirely in object LOD. Grass counts refer to the
selected grass generation stage; externally installed caches are checked in diagnostics.
The same counts appear in the pipeline log and CLI JSON. Seasonal INI contents participate
in checkpoint fingerprints, even if file size and timestamps are unchanged.
