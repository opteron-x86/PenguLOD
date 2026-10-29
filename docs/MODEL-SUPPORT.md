# Model and render support

PenguLOD reads Skyrim SE and Skyrim LE NIF files directly on Linux and Windows.
Support applies to texture rendering, native model authoring and object/tree LOD.
The selected model must be complete: an unreadable visible shape cannot silently
disappear from an otherwise successful result.

## Geometry

| Input | Supported behavior |
| --- | --- |
| `BSTriShape`, `BSSubIndexTriShape`, `BSMeshLODTriShape` | Skyrim vertex buffers, triangle indices, UVs, vertex colors and tangent frames |
| `NiTriShape`, `BSLODTriShape` | Legacy triangle lists in `NiTriShapeData` |
| `NiTriStrips` | Strip conversion with alternating winding, degenerate joins and separate strips |
| `BSDynamicTriShape` | Separate dynamic position buffer, including partitioned skins |
| `NiSkinInstance`, `BSDismemberSkinInstance` | Static pose from the model's own skeleton, inverse-bind matrices and weights |
| `NiSkinPartition` | SSE shared vertex buffer and global triangle indices across partitions |
| `NiSwitchNode`, `NiLODNode` | Serialized active child; inactive alternate geometry is not duplicated |

Node transforms are baked into positions and directions. Missing normals and tangents
are reconstructed for supported geometry. Buffer sizes, indices, transforms and finite
coordinates are checked within each NIF block's boundary.

Static skinning requires all bones in the same NIF. Legacy skins require per-bone weights
in `NiSkinData`; SSE skins use their vertex-buffer weights. Missing bones, invalid weights
or inconsistent buffers retain a reported fallback. The pose uses serialized bone transforms;
PenguLOD does not evaluate animation timelines, wind, physics or an external skeleton.
Written LOD meshes are static and do not retain skin or tree-animation shader flags.

A switch with active index `-1` draws no children. An invalid active index, including an
uninitialized distance-dependent LOD selector, is reported instead of guessing a branch.

## Materials and render resources

Lighting shaders retain the existing diffuse/normal, UV, alpha and material-calibration
support described in [textures](TEXTURES.md) and [materials](MATERIALS.md).
Native full-tree reduction uses diffuse and normal textures with a plain LOD material;
PBR, environment, parallax and glow-specific texture slots are not carried into that
material. Authored passthrough LOD models keep their own material settings.

Texture generation additionally accepts static `BSEffectShaderProperty` resources with
a source texture, UV transform, clamp mode, base-color tint and optional vertex colors
or alpha testing. Effect lighting influence controls whether the surface is lit.
These materials are render-only and cannot be exported as ordinary object LOD shaders.
Animated effect controllers, blending, palette mapping, falloff and view-dependent
effects remain explicit failures. This is not full game-shader emulation.

Object render templates still need a valid TexGen viewport plane. Hybrid trunk rendering
still needs the installed model's matching cards and crops. Geometry support does not
create absent resources or supply a mod's missing textures.

## Reports and verification

Tree coverage reports explain model rejection and retain billboard fallback. Texture
reports name the failing block and reason; a required render failure preserves the
previous published output. Optional object resources can retain an existing valid
diffuse/normal pair. Deep diagnostics include incomplete-model warnings with asset origins.

Synthetic regression fixtures cover strips, multi-bone poses, partition indices, dynamic
positions, active branches, effect materials, malformed inputs and texture publication.
Compare tree size, trunk/crown alignment, silhouette and transitions in-game after changing
models; automated geometry checks cannot establish visual compatibility with every mod.
See [repeatable checks](DIAGNOSTICS.md#repeatable-checks).

## TreeLODGen resources

[TreeLODGen](https://github.com/Cl3mus33/TreeLODGen) generates compatible CRC-named tree
LOD models. PenguLOD can consume those installed models through its existing tree-model
selection; it does not need to launch TreeLODGen. Its crown pruning, bark reduction and
hybrid authoring are useful references for further tree-generation work. An installed
model still needs matching textures, a compatible mesh checksum and an acceptable triangle
budget. TreeLODGen source code is GPL-3.0; any future code reuse must preserve its license
and attribution. This implementation adds no TreeLODGen or nifly runtime dependency.

The binary layouts follow the [NIF format definitions](https://github.com/niftools/nifxml)
and static skin-transform conventions in [niflib](https://github.com/niftools/niflib).
