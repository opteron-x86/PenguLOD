# Native terrain generation

The Terrain page builds Skyrim SE BTR meshes, diffuse and model-space normal tiles,
segmented near-distance water, and optional Community/Bottled Shaders heightmaps.
Select it on Pipeline to generate terrain before object LOD; verified generated terrain
textures then feed object LOD and underside generation. Use a separate output mod folder.

The generator reads winning LAND heights, quadrant texture layers, alpha weights, vertex
colours and water levels. LOD settings determine each world's grid origin and supported
levels. Tiles share world-space height samples, including negative coordinates and the
outermost LAND edges. Skirts cover fine/coarse boundary interpolation differences.

Unpainted quadrants and null base layers use Skyrim's `textures/landscape/dirt02.dds`
unless `[Landscape] sDefaultLandDiffuseTexture` overrides it. The selected explicit INI
takes priority, followed by the profile's local `SkyrimCustom.ini`/`Skyrim.ini` when enabled,
then available game INIs. Paths in this setting are relative to `textures/landscape`;
`landscape/` and `textures/landscape/` prefixes are also accepted. The resolved default is
logged. Fully covered base textures are not loaded. See the
[Terrain Helper default-texture notes](https://github.com/hakasapl/TerrainHelper#mod-authors)
for Skyrim's diffuse/normal defaults. `sDefaultLandNormalTexture` follows the same INI
precedence, defaulting to `textures/landscape/dirt02_n.dds`.

Skyrim plugins omit landscape in some distant areas. **Fill missing landscape from existing
terrain LOD** samples installed BTR meshes, diffuse textures and available model-space normals in these areas.
The report identifies how many existing tiles were used. It cannot recover full-resolution
detail that was absent from those files. Missing coverage or unresolved landscape materials
stops publication and preserves the previous output. Missing LOD-water records are reported;
the affected world's water shapes are omitted rather than referencing an invalid water type.

The bake combines source normal detail with terrain slope, using the same layer weights
as diffuse colour. Source normal strength defaults to 1; 0 restores slope-only normals.
DirectX tangent normals follow east/south texture coordinates and are converted to Skyrim's
east/up/north model-space texture layout. Missing optional normal maps use the slope and
produce a warning. Colour mipmaps average in linear light; landscape texture alpha is not
treated as transparency.

Explicit `textures/pbr/` materials support static albedo calibration and ambient occlusion
from the blue RMAOS channel. **Look for textures/pbr equivalents** is off by default; enable
it only when your runtime uses those replacements. A selected twin uses its own normal/AO
maps, without mixing in maps from the original material. Missing AO is unoccluded. PBR scale
(default 0.65) and gamma (1.15) operate on linear albedo, followed by AO strength (0.5).
Disable calibration to retain the source albedo. This does not reproduce runtime reflections,
roughness, parallax or changing illumination. `PenguLOD.terrain.json` records selected
materials and missing optional maps. Malformed maps and missing contributing diffuse maps
stop publication.

[Seasonal terrain](SEASONS.md) applies written Seasons of Skyrim LTEX swaps to colour,
normal and AO maps together. Brightness scales linear colour; vertex colour strength controls
the landscape's painted shading. Texture repeats per cell is configurable (24 by default).
These controls are saved in desktop presets and shared by the CLI `terrain` and `pipeline`
commands. A change invalidates terrain checkpoints.

Terrain-shadow exports are loose R16_UNORM DDS files with the bounds and height range in
the filename. Keep the `Terrain.HeightMap` filename case and keep the files loose. Export
requires rectangular coverage; disable it for worlds with uncovered holes. The size limit
reduces the sample density for large worlds, without stretching their bounds.

Synthetic tests cover BTR structure at levels 4/8/16/32, UVs, shared edges, water segments,
negative coordinates, landscape materials, missing coverage, fallback sampling, heightmap
orientation, output preservation and desktop checkpoint reuse. Compare land/water transitions,
shorelines, tile seams and the world map when changing source assets or settings. Broader
modlist and shader compatibility is tracked in the [roadmap](ROADMAP.md).

Format references: [Community Shaders normal and RMAOS decoding](https://github.com/community-shaders/skyrim-community-shaders/blob/dev/package/Shaders/Lighting.hlsl), [NifTools schema](https://github.com/niftools/nifxml/blob/develop/nif.xml),
[terrain conversion format observations](https://github.com/bryantmh/tes4skyrim/blob/master/docs/commentary/asset_convert_terrain.md),
[Bottled Shaders heightmap reader](https://github.com/InTheBottle/Bottled-Shaders/blob/f0deaabe1f5d8c675bce06927a5c257f4af4e94d/src/Features/TerrainShadows.cpp).
No game assets are included in the tests or repository.
