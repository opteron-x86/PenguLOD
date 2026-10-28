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
Object and grass swaps remain supported. Filenames follow the
[Seasons of Skyrim loader](https://github.com/powerof3/SeasonsOfSkyrim/blob/master/include/LODSwap.h).

Terrain shape/water heights do not change with a material swap, so terrain-shadow
heightmaps are shared. Areas without source LAND use installed seasonal terrain textures
when available and otherwise retain default terrain, with a warning. Missing landscape
data cannot be reconstructed as seasonal layers from a baked default texture.

Runtime-generated winter swaps must first be saved by Seasons of Skyrim as INIs and
available to the selected profile. PenguLOD does not execute SKSE or recreate the runtime's
automatic snow rules. Dynamic shader snow remains a runtime effect.

`TerrainFixture(seasons: true)` is the dedicated synthetic seasonal test profile. It
contains landscape and tree replacements with visibly different colours. Tests generate
both default and seasonal output, inspect NIF texture paths, check BTT/LST/atlas coexistence,
exercise native 3D replacements, and verify failure/cleanup behavior. No game assets are
included. Follow [the transition checks](DIAGNOSTICS.md) to validate a real seasonal profile.
