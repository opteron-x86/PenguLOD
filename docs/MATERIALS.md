# Material baking and brightness

Texture generation renders source UV transforms and clamp modes, alpha-tested foliage,
vertex colours, and tangent-space normal maps. Alternate texture sets preserve the NIF texture-slot mapping, including diffuse, normal and material maps. Normal-map alpha is treated as gloss, not transparency, during
resizing. Output normals are transformed into the billboard's frame and renormalized.

**Material calibration** on the texture page controls source normals, ambient/directional
light, PBR scale/gamma and ambient-occlusion strength. Explicit `textures/pbr/` albedo paths
use the configured curve; where the source material provides RMAOS in texture slot 5,
its blue channel supplies AO. This channel convention follows the
[Bottled Shaders lighting implementation](https://github.com/InTheBottle/Bottled-Shaders/blob/f0deaabe1f5d8c675bce06927a5c257f4af4e94d/package/Shaders/Lighting.hlsl).
Missing optional normal/AO maps fall back to geometry normals/unoccluded albedo.

The output targets Skyrim's LOD shader. It is a static bake, not an implementation of
the game's PBR renderer: reflections, displacement, weather, dynamic snow, shader patch
configuration and view-dependent lighting are not reproduced. PBR twin lookup for object
LOD remains available separately in Generation settings. Do not apply another PBR curve
to already calibrated generated billboards.

For overly bright distant grass, adjust grass LOD root/tip vertex brightness; for ground,
adjust Terrain brightness; for tree billboards, adjust Texture generation brightness and
regenerate textures followed by LOD. Change one parameter at a time and compare at the
same location, weather and time. These controls serve different shader paths and should
not be assumed to use the same numeric value.

Regression fixtures check source-normal orientation, PBR/AO calibration, preserved cutout
alpha, UV transforms/clamping, and distinct material keys after texture replacement.

Terrain has separate source-normal and PBR calibration controls; see [terrain generation](TERRAIN.md).
