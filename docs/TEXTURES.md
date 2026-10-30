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
  tiles, intermediate textures, colour multipliers, matching normal maps and `x` mipmap
  conversion recipes are supported.
  Generated DDS files use BC7 and mipmaps; copy rules preserve the source format.
- **Tree billboards** renders winning TREE and tree-flagged STAT models, including record
  texture replacements, inclusion rules and seasonal replacement records. Height and
  resolution controls use mesh bounds. [Material calibration](MATERIALS.md) controls
  source normal maps, lighting, brightness and supported PBR/AO baking.
  Automatic discovery excludes whole trees whose animated, blended or view-dependent
  effects cannot be rendered as static billboards. The texture report explains each
  exclusion. Explicit billboard rules and model filters still require a supported render.
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

## Recipe-driven LOD materials

Run **Textures, then LOD**, or select **Use this generated texture output for subsequent
LOD jobs**. The generated manifest passes the exact recipe order, base size and brightness
to LOD generation. CLI pipelines use the same preset with `pipeline --settings <preset.json>
--stages textures,lod`, or a JSON request with `Lod.GeneratedTextureFolder`. Texture matching
is enabled by default. Regenerate older texture output once
to save this metadata. Without a generated manifest, installed recipes can still be read
from the resource folder and virtual `Data/DynDOLOD`, using the default recipe base size.

For a LOD shape using a full texture, PenguLOD can move its UVs into a recipe atlas when
the complete UV range fits the recipe layout. Repeated tiles, clipped edges, copy chains
and nested atlases participate in that check. Several equally applicable layouts, extra
shader texture channels, or shader UV transforms retain the full texture and are reported.

For a shape already using a recipe atlas, alternate textures rebuild that atlas with the
relevant sources replaced. Unrelated tiles and the shape's UVs stay intact. Normal-only
replacements use the texture set's actual normal path. Merged shapes combine their
replacements; conflicting replacements for the same source cannot be resolved reliably
and stop the combined job. Duplicate full-model shape names with alternate textures also
require distinct names before generation. A full-model texture path changed by a mesh
patch can be associated by an unambiguous source/UV region; unresolved associations are
reported rather than guessed.

If a matched diffuse texture or explicit normal map is missing, PenguLOD keeps that
material's supplied LOD textures and UVs and logs a warning naming the model, shape and
missing file. Other materials and record/season variants continue to use texture matching.
This fallback requires the supplied LOD textures to exist. Invalid texture data, broken
recipe inputs, cycles and conflicting replacements still fail generation. Installing the
missing replacement and regenerating restores the intended match automatically.

`PenguLOD.recipes.json` in LOD output lists material mappings, generated variants, retained
full textures, missing-replacement fallbacks and failures, with the source model path.
Fallback warnings also appear in the run log and pipeline receipt. Variant textures live under `textures/pengulod/recipes` in
LOD output. Keep texture output enabled underneath it. The texture manifest also owns
original input copies under `textures/pengulod/recipe-inputs` for in-place conversions;
keep those files with the output so subsequent jobs never process an already-adjusted
input a second time.

Recipe and source content participate in LOD block fingerprints. The pipeline's normal
stage-reuse check uses asset metadata; select **Verify all source asset contents** (CLI
`--verify-source-contents`) when checking assets whose timestamps might have been preserved.

## Conversion recipe format

The twelve tab-separated columns in an `alpha` or `noalpha` recipe can describe either
tile placement or a conversion. In a conversion, columns 6 and 12 contain `x`; these
are mipmap instructions, not a request to execute a shader or another program.

| Columns (starting at 1) | Conversion meaning |
| --- | --- |
| 1 | Base RGB brightness offset, -255 to 255 |
| 2–4 | Nonnegative RGB multipliers, interpreted with gamma 2.2 |
| 5 | Source DDS path; `\|` separates alternatives in priority order |
| 6 | `x` |
| 7 | Mipmap filter number |
| 8 | Mipmap contrast percentage, -100 to 100 |
| 9 | Mipmap brightness percentage, -100 to 100 |
| 10 | Destination DDS path |
| 11 | First adjusted mip level, starting at 0 |
| 12 | `x` |

Filters are 0 nearest, 1 linear, 2 cosine, 3 Hermite, 4 quadratic, 5 Gaussian,
6 spline, 7 Lanczos, 8 Mitchell and 9 Catmull–Rom. Each mip samples the base image.
At level `m >= start`, contrast and brightness are multiplied by `m - start + 1`;
RGB is adjusted as `RGB × (1 + contrast/100) + brightness/100`, then clamped to 0–1.
Alpha is retained by `alpha` recipes and removed by `noalpha` recipes. These operations
retain source dimensions and generate a complete mip chain down to 1×1 in BC7; compressed
bytes need not match TexGen. A conversion writes the specified texture, without inventing
a normal companion. See the upstream [TexGen configuration guide](https://dyndolod.info/Help/TexGen-Configuration)
and [mod-author resource conventions](https://dyndolod.info/Mod-Authors). Filter numbering
follows the [ImagingLib sampling filters](https://github.com/ElminsterAU/imaginglib/blob/aef974039fb64d8d4ee8709cf07c667c59d88a91/Source/ImagingFormats.pas).

## Supported scope

Texture generation supports source normals and a configurable static PBR/AO bake, but
does not reproduce the game's full shader. Full HD billboard parity, complex-grass shading
and reference-scale discovery remain unsupported. [Seasons](SEASONS.md) uses written swap INIs.
Recipe parsing and rendering are native; resource directories named `DynDOLOD` do not
require TexGen/DynDOLOD executables. Resource files and models must still be supplied by
their authors and installed with their required mods.

Triangle strips, dynamic vertex buffers and suitable self-contained static skin poses
are supported. Static effect materials can be rendered from compatible resources; see
[model support](MODEL-SUPPORT.md) for formats and limits. An unreadable visible shape,
missing required texture or unsupported required render resource stops publication and
preserves existing output. Missing assets must be supplied by the source mod; generation
does not repair or rewrite that mod.
