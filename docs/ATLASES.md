# Object materials and LOD textures

Two independent options in **Generation → Object atlases** reduce object LOD material
and texture costs. Both run during the LOD stage and write their textures inside its
output mod. Install that complete output, including its `textures` folder.
Full-detail source assets, terrain, tree geometry and grass density are unchanged.

## Consolidate object materials

Enable **Pack object texture atlases** to group compatible surfaces on shared pages.
**Atlas cell size** controls resolution per material, including its padded border.
Use 512 for a first quality-focused performance comparison; 256 saves more texture space.
The drawable interior is seven-eighths of the selected cell width.

Pages group matching shader flags, alpha tests, emittance, color spaces and cubemaps.
Diffuse, normal/gloss and supported auxiliary 2D maps receive matching UV layouts.
Normal gloss alpha is preserved. Alpha-test mip coverage uses each material's actual
threshold; different thresholds keep separate material groups. Cubemap references stay
unchanged. Moving a texture preserves the existing HD/plain snow and ash shader selection.

UVs within one tile can be remapped without changing geometry. An integer-shifted tile
can also be packed when the source wraps. UVs spanning multiple tiles retain their original
layout. Passthrough shaders, alpha blending, unusual alpha tests, projected UVs, parallax
displacement and multilayer sampling remain outside the atlas. An unavailable or unsupported
texture leaves the affected material unchanged and produces a warning; it does not disable
packing elsewhere. Existing validation of invalid game assets still applies.

Cells have padded borders, normalized normal mips and coverage-preserving cutout mips.
The mip chain stops while each cell still has a sampling border, preventing unrelated
materials from blending into the last global mip. Check distant filtering, thin cutouts,
snow transitions and texture seams in game when adopting atlases.

`PenguLOD.atlas.json` records source/result material counts, page bytes, skipped reasons
and warnings. Material counts describe loaded source models, not measured in-game draw
calls: block limits, snow variants and visible geometry also affect rendering cost.

## Optimize textures outside atlases

Enable **Optimize object LOD textures** and choose **Maximum standalone LOD texture size**.
1024 is a conservative starting point for tiled surfaces and large structures. This option
also works with atlasing disabled.

PenguLOD writes content-addressed copies under its own texture folders and redirects only
object LOD materials. Authored compressed mip levels are reused byte-for-byte when they
provide the requested size, preserving compression format, color-space metadata and
existing mip filtering. Uncompressed images can become BC7 copies; supported oversized
images without a suitable authored mip can be resized and encoded with new mips.
Normal vectors, gloss alpha, packed data channels and supported cutout thresholds are
handled separately. Copies are only used when their encoded file size is smaller.

Already suitable compressed textures retain their source paths. Atlas pages keep their
own cell resolution and mip chain. Cubemaps, volume/array textures, passthrough materials
and unsupported formats retain their original assets. Conversion problems produce local
fallback warnings. This is texture sizing and compression, not a change to geometry,
UV repetition or material brightness.

`PenguLOD.object-textures.json` lists rewritten paths, retained inputs and encoded byte
counts. Those counts are not a prediction of VRAM savings: full-detail objects may still
need the original texture, and the engine determines which mip levels are resident.

## Rebuild and compare

Either option rebuilds selected object blocks when the LOD stage executes. The persistent
pipeline can still reuse a whole verified unchanged stage. Texture generation, terrain and
grass caches do not need regeneration solely for these settings.

Synthetic tree/grass meshes keep their specialized material pipelines. Seasonal materials
already encountered in the ordinary scene can share mappings; newly encountered seasonal
materials retain their source textures. Previous content-addressed pages and copies are
kept because retained worldspaces or seasonal blocks can still reference them. A fresh
output directory avoids carrying unused older files when rebuilding an entire setup.

Replace the previous installed object output with the complete new output and restart the
game. Compare the same saved viewpoint, weather and camera direction. Measure FPS and
inspect transitions; lower material counts and smaller texture files do not by themselves
guarantee a particular FPS gain.
