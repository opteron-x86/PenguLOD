# PenguLOD roadmap

The initial alpha establishes a maintained native Linux generator and the existing grass →
textures → object/tree/grass LOD workflow. This roadmap describes work still to do; it is
not a claim of parity with every xLODGen, TexGen or DynDOLOD feature.

## 0.2: All-in-one foundations

1. **Resumable jobs and reliable outputs.** Persist stage state and input fingerprints;
   safely reuse completed work after cancellation or failure; show preparation, generation
   and output-writing time separately. Invalidate dependent stages when inputs change.
2. **Native terrain and water LOD.** Build BTR meshes and diffuse/normal texture tiles from
   winning landscape data, with consistent edges, worldspace bounds and water levels.
   Validate seams and transitions across LOD4/8/16/32. Export compatible terrain heightmaps
   for shader terrain shadows where supported.
3. **Practical 3D trees.** Report coverage and geometry budgets per model/level; use distinct
   near/far models and simpler distant silhouettes. Extend native model generation with
   tree-specific simplification and validate hybrid textures against their UV layouts.
4. **Diagnose the entire pipeline.** Explain missing model/texture coverage, stale caches,
   disabled outputs and runtime dependencies before an expensive generation. Add repeatable
   small-world fixtures and in-game transition checks.

Ship these as incremental alpha releases. The first terrain implementation should be explicit
about supported worldspaces/materials rather than quietly producing incomplete output.

## Further milestones

- Improve texture rendering and material baking, including True PBR and Community/Bottled
  Shaders consistency, alpha-tested foliage, normals and lighting calibration.
- Complete seasonal tree billboards and terrain, with a dedicated seasonal test profile.
- Improve native object texture atlasing, UV handling and material grouping.
- Add optional Vulkan acceleration where it helps measured workloads; retain the portable
  CPU path and clear capability checks. Acceleration will not solve every source/output I/O bottleneck.
- Broaden distro packaging and compatibility tests, then evaluate AppImage/Flatpak distribution.
- Extend CLI coverage to the same persistent pipeline as the desktop.

## Already in the first alpha

Native Linux Avalonia desktop and LOD CLI; direct Fluorine/MO2 profiles; offline CPU grass
caching; texture stitching, tree/grass billboards and hybrid trunk textures; existing 3D
LOD model consumption; combined generation; upstream occlusion and seasonal object/grass
support; progress, cancellation, logs, presets and author/repair tools.

Track scoped work and acceptance criteria in [GitHub issues](https://github.com/opteron-x86/PenguLOD/issues).
The upstream [AnvilLOD roadmap](https://github.com/CageTV/AnvilLOD/blob/main/docs/ROADMAP.md)
remains useful context, but PenguLOD tracks its release status independently.
