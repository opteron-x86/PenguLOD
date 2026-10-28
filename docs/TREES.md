# 3D-tree geometry and coverage

Enable 3D tree LOD in Generation settings. Installed CRC-matched or named LOD models remain
preferred. With **Build missing 3D tree models**, trees without an installed model can use
native reduction of their full model. Missing textures or unsupported geometry retain
billboard tree LOD and appear in the report.

Near and far triangle budgets apply per model (defaults: 4,000 at LOD4 and 1,600 at LOD8).
A budget of zero removes that cap. Separate error limits bound trunk simplification.
The reducer retains material boundaries, surviving UV coordinates and alpha-tested foliage
geometry. It does not cut textured leaf cards to meet a number: when intact foliage cannot
fit, the level uses billboard cards and reports the reason.

LOD8 prefers an installed `_lod_1.nif` corresponding to the near `_lod_0.nif`, or one next
to a full source model. Otherwise it attempts native reduction. Reusing an unchanged near
mesh is replaced with billboard fallback. A far model cannot be heavier than the selected
near model. Farther levels use the existing tree-card system and map-tree setting.

Overview's **Tree coverage & cost** table and `scan-report.json` show source model, selected
level, instance count, triangles per model, placed triangles, method and fallback reasons.
Memory figures are geometry estimates before block merging, not elapsed-time predictions
or a guarantee of in-game performance. Native models retain full-model textures, so geometry
savings alone do not imply a smaller texture footprint.

Tests exercise opaque reduction, intact alpha geometry and UVs, authored far-model selection,
budget fallback, cost accounting and hybrid trunk texture generation. Test transitions and
foliage shading in-game when changing model sets or error limits.
