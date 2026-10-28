# Native terrain generation

The Terrain page builds Skyrim SE BTR meshes, diffuse and model-space normal tiles,
segmented near-distance water, and optional Community/Bottled Shaders heightmaps.
Select it on Pipeline to generate terrain before object LOD; verified generated terrain
textures then feed object LOD and underside generation. Use a separate output mod folder.

The generator reads winning LAND heights, quadrant texture layers, alpha weights, vertex
colours and water levels. LOD settings determine each world's grid origin and supported
levels. Tiles share world-space height samples, including negative coordinates and the
outermost LAND edges. Skirts cover fine/coarse boundary interpolation differences.

Skyrim plugins omit landscape in some distant areas. **Fill missing landscape from existing
terrain LOD** samples installed BTR meshes and terrain diffuse textures in these areas.
The report identifies how many existing tiles were used. It cannot recover full-resolution
detail that was absent from those files. Missing coverage or unresolved landscape materials
stops publication and preserves the previous output. Missing LOD-water records are reported;
the affected world's water shapes are omitted rather than referencing an invalid water type.

The initial bake supports diffuse layers, vertex colours and height-derived normals.
[Seasonal terrain](SEASONS.md) uses written Seasons of Skyrim LTEX swaps and available
seasonal baseline textures. True PBR terrain conversion and source normal-detail baking
remain unsupported. Brightness scales linear colour; vertex colour strength controls the
landscape's painted shading. Texture repeats per cell is configurable (24 by default).

Terrain-shadow exports are loose R16_UNORM DDS files with the bounds and height range in
the filename. Keep the `Terrain.HeightMap` filename case and keep the files loose. Export
requires rectangular coverage; disable it for worlds with uncovered holes. The size limit
reduces the sample density for large worlds, without stretching their bounds.

Synthetic tests cover BTR structure at levels 4/8/16/32, UVs, shared edges, water segments,
negative coordinates, landscape materials, missing coverage, fallback sampling, heightmap
orientation, output preservation and desktop checkpoint reuse. In-game visual validation of
this native terrain implementation is still pending. Compare land/water transitions, shorelines,
tile seams and the world map in a small test profile before replacing an established terrain set.

Format references: [NifTools schema](https://github.com/niftools/nifxml/blob/develop/nif.xml),
[terrain conversion format observations](https://github.com/bryantmh/tes4skyrim/blob/master/docs/commentary/asset_convert_terrain.md),
[Bottled Shaders heightmap reader](https://github.com/InTheBottle/Bottled-Shaders/blob/f0deaabe1f5d8c675bce06927a5c257f4af4e94d/src/Features/TerrainShadows.cpp).
The writer was also compared with locally installed xLODGen SSE BTR structures. No game assets
are included in the tests or repository.
