# 3D-tree geometry and coverage

Enable 3D tree LOD in Generation settings. Installed CRC-matched or named LOD models remain
preferred. With **Build missing 3D tree models**, trees without an installed model can use
native reduction of their full model. Missing textures or unsupported geometry retain
billboard tree LOD and appear in the report.

Supported inputs include legacy triangle strips and self-contained skinned models baked
to their serialized static pose. Active switch branches prevent duplicate static/animated
tree geometry. See [model support](MODEL-SUPPORT.md) for skeleton and material limits.

Record-specific alternate texture sets are applied to full-model shapes before native
reduction, including seasonal replacements. Each variant has separate materials and cache
keys. Automatic billboards use the same overrides. An unmatched or ambiguous shape, or a
missing replacement asset, retains billboard fallback and is reported. Authored LOD UVs
are not assumed to match full-model texture overrides; enable native model generation to
use those variants in 3D.

Near and far triangle budgets apply per model (defaults: 4,000 at LOD4 and 1,600 at LOD8).
A budget of zero removes that cap. Separate error limits bound trunk simplification.
Opaque trunks retain material boundaries and surviving UV coordinates during reduction.
**Reduce redundant leaf cards** can remove complete alpha-tested cards while preserving
the remaining cards' UVs. Coverage is sampled at 64×64 from 14 directions, independently
for each material. Sampled silhouette edges and model bounds are retained. Near/far limits
allow at most 1%/3% interior coverage loss by default; set either to 0 to remove only
redundant coverage. Disable the option to keep all leaf cards.

**Tree geometry target (%)** also optimizes models already below the hard budget. The
default is 65% of source triangles; 100 restores budget-only reduction. This is a target,
not a required reduction: coverage constraints can retain more geometry without forcing
billboards. The CLI equivalent is `--tree-foliage-target 65` with `--tree-3d`.

Blended materials, unsupported connected geometry, unknown alpha textures and subpixel
cards remain intact. Sampling has bounded work and memory; reaching a limit retains the
foliage. These are sampled coverage bounds, not a guarantee of identical shading from every
camera position. When geometry still exceeds its budget, the level uses billboards and
reports the reason.

LOD8 prefers an installed `_lod_1.nif` corresponding to the near `_lod_0.nif`, or one next
to a full source model. Otherwise it attempts native reduction. Reusing an unchanged near
mesh is replaced with billboard fallback. A far model cannot be heavier than the selected
near model. Farther levels use the existing tree-card system and map-tree setting.

Overview's **Tree coverage & cost** table and `scan-report.json` show source model, selected
level, instance count, triangles per model, placed triangles, removed foliage triangles,
measured maximum coverage loss, material variant, method and fallback reasons.
Memory figures are geometry estimates before block merging, not elapsed-time predictions
or a guarantee of in-game performance. Native models retain full-model textures, so geometry
savings alone do not imply a smaller texture footprint.

## Native hybrid trunks

**Author native hybrid trunks** is an optional addition to **Build missing 3D tree models**.
For supported full trees, it replaces opaque trunk geometry with two aligned front/side
cards and combines them with a reduced 3D crown. Existing installed LOD models retain
precedence. This does not require TreeLODGen, TexGen or prebuilt trunk render resources.

The source must have separate opaque trunk shapes and alpha-tested foliage shapes. Blended
parts, incomplete geometry, missing render textures or an empty camera view retain the
existing native geometry reduction path. The tree coverage report explains the fallback.
Models that still exceed the triangle budget use billboards, as before. The four trunk-card
triangles count toward both near and far budgets; foliage coverage limits still apply to
the crown. No source models are changed.

The renderer and cards share model-space dimensions, origin and base height. Texture
coordinates and normals use the matching camera basis. **Maximum native trunk texture
size** and **Native trunk world units per pixel** control resolution. Record/season bark
variants and source texture changes produce separate verified resources. The static bake
uses the renderer's default material calibration; it does not reproduce bark parallax,
animation or the complete runtime shader.

Generated trunk diffuse/normal textures are published under `textures/pengulod/hybrid` in
the **LOD output**. Keep them with that output. Private cache corruption is repaired on the
next generation. Changing these controls requires a LOD run; existing whole-tree billboards
remain necessary for farther levels. Inspect trunk/crown joins, side views and near/far
transitions in game before enabling this across a full tree set.

Tests exercise opaque reduction, rendered alpha coverage, material/UV preservation, authored
far-model selection, texture variants, budget fallback and seasonal output. Test transitions
and foliage shading in-game when changing model sets, coverage limits or error bounds.
