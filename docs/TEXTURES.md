# Texture generation

On **Texture generation**, choose the installed **DynDOLOD installation / resources**
folder and a separate **Texture output mod folder**. PenguLOD reads recipes and render
resources from that installation and enabled mods; it does not launch TexGen or Wine.
Game/mod assets and DynDOLOD resources are supplied separately.

**Generate textures** runs this stage alone. **Textures, then LOD** connects the generated
textures to LOD immediately. Pipeline can also combine textures with grass and terrain.
Install texture and LOD output as separate mods, with LOD output winning conflicts.
Existing TexGen output can supply textures for unsupported features underneath these mods.

## Output categories

- **Stitched object textures** follows enabled-plugin recipe order. Resources in virtual
  `Data/DynDOLOD` override same-named installation recipes. Copy rules, cropped/fractional
  tiles, intermediate textures, colour multipliers and matching normal maps are supported.
  Generated DDS files use BC7 and mipmaps; copy rules preserve the source format.
- **Tree billboards** renders winning TREE and tree-flagged STAT models, including record
  texture replacements, inclusion rules and seasonal replacement records. Height and
  resolution controls use mesh bounds. [Material calibration](MATERIALS.md) controls
  source normal maps, lighting, brightness and supported PBR/AO baking.
- **Hybrid tree trunk textures** renders front/side diffuse and normal pairs from
  `_trunk.nif` resources under `DynDOLOD/Render/Billboards/` or
  `DynDOLOD/Render/Skyrim/Billboards/`. Matching installed hybrid meshes supply camera
  directions and crops. Enabled mod resources override the standalone bundle.
  Matching hybrid meshes are required; this pass does not author new hybrid models.
- **Grass billboards** is optional and uses grass lists on winning landscape texture
  records, including seasonal replacements. Unreferenced library records are skipped.
  Explicit rules and filtered previews can select additional models.
- **Rendered objects** is experimental and uses the NIF's TexGen viewport plane and
  resolution scale. Standalone resource templates update matching installed LOD textures;
  templates for absent games or mods are skipped.

For existing hybrid trees, enable **Hybrid tree trunk textures** and **3D tree LOD**,
then run textures followed by LOD. Near/far model selection, budgets and native reduction
are configured separately; see [3D trees](TREES.md).

## Previews, reports and replacement

Use a model filter and **Preview selected texture** for a small test. Filtered generation
requires a separate output folder and cannot feed a full LOD job. Clear the filter for
a complete run. Categories, brightness, size limits and filters are stored in presets.

Each successful run replaces the previously owned texture set. Keep every category
whose output should remain selected during regeneration. The report identifies generated,
skipped, retained and failed assets. If an optional object template cannot render, a valid
installed diffuse/normal pair can be retained unchanged; brightness controls do not alter
retained textures. Missing fallback pairs and failed required renders stop publication.

`AnvilLOD.textures-report.json` records results. Successful output also includes
`AnvilLOD.textures.json` with ownership and hashes. Failed rendering and cancellation
preserve published textures; LOD verifies the manifest before using them. If publication
is interrupted and the next run reports changed files, keep that output disabled and
generate into a fresh folder. See [persistent generation](PIPELINE.md).

## Supported scope

Texture generation supports source normals and a configurable static PBR/AO bake, but
does not reproduce every TexGen conversion or the game's full shader. Special `x`-format
glow/alpha conversions, full HD billboard parity, complex-grass shading and reference-scale
discovery remain unsupported. [Seasons](SEASONS.md) uses written swap INIs.

Models with unsupported/skinned shapes are skipped as a whole. Missing required source
textures or unsupported required render resources stop publication. Missing assets must
be supplied by the source mod; generation does not repair or rewrite that mod.
