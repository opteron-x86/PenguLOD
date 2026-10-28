# Native object texture atlases

Enable **Pack object texture atlases** in Generation settings. Cell size controls the
resolution per material including its padded border. PenguLOD packs deterministic paired
diffuse/normal pages, remaps object UVs, and lets compatible surfaces merge in each BTO.
Original geometry is unchanged. The stage writes `PenguLOD.atlas.json` with packed/skipped
counts and reasons, and removes unused pages from its own generated atlas folder.

Eligibility is conservative: UVs must stay in `[0,1]`; passthrough shaders, extra shader
channels, nonstandard alpha tests, and missing source textures retain their original
material and UV layout. Synthetic grass/tree meshes keep their specialized textures.
PBR and opaque-alpha copies are prepared before atlasing. Seasonal replacements can reuse
an atlas entry when the material matches; other seasonal materials keep their source paths.

Cells have padded borders, renormalized normal mips and coverage-preserving alpha mips.
The mip chain stops while each cell still has a sampling border, preventing unrelated
materials from merging into the last global atlas mip. This trades some distant texture
filtering quality for avoiding cross-material colour bleed.

An atlas layout can affect every object block, so an executed LOD stage rebuilds all
selected blocks when atlasing is enabled. The persistent pipeline can still reuse a whole
verified unchanged LOD stage. This is deliberately different from per-block incremental
generation with atlasing disabled.

Tests check deterministic placement, paired maps, material merging, untouched tiled UVs,
geometry preservation, and generated file references. Check texture seams and far-distance
filtering in game before adopting atlases for an entire profile.
