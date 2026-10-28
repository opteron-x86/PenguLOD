# PenguLOD roadmap

The original roadmap's implementation milestones are included in **0.2.0-alpha.1**.
The table records what each delivers and where its validation stops. This is not a claim
of complete xLODGen, TexGen or DynDOLOD feature parity.

| Milestone | Implemented | Validation and limits |
| --- | --- | --- |
| Resumable jobs and reliable outputs | Shared durable stage graph, input fingerprints, verified reuse, dependency invalidation, locks and staged publication. | Fixtures cover cancellation, interruption, corrupted outputs, input changes and preservation of previous output. [Pipeline](PIPELINE.md). |
| Native terrain and water | Winning LAND/LTEX layers, BTR land/water, diffuse/normal tiles, LOD4/8/16/32, baseline fallback and terrain-shadow heightmaps. | Synthetic formats, orientation and mixed-level seams checked. In-game shorelines, transitions and shader shadows need validation. [Terrain](TERRAIN.md). |
| Practical 3D trees | Near/far geometry budgets, authored far models, native trunk reduction and conservative foliage/card fallback; coverage/cost reports. | Reduction, UV preservation, fallback and costs tested. Dense alpha geometry may remain billboards. [Trees](TREES.md). |
| Pipeline diagnostics | Preflight, runtime requirements, stale inputs, asset precedence, texture/model coverage and metadata exports. | Failure paths and fixture workflow tested; repeatable game checklist provided. [Diagnostics](DIAGNOSTICS.md). |
| Materials and shader consistency | Source normals, UV transforms/clamp, cutout alpha and configurable PBR albedo/AO/light bake. | Numerical fixtures pass. Static bake cannot reproduce the full shader, weather or displacement. [Materials](MATERIALS.md). |
| Seasonal tree and terrain output | Seasons form swaps produce seasonal terrain, BTT/LST atlases and native 3D tree replacements. | Synthetic seasonal profile exercises terrain and both tree paths. Written swap INIs required; runtime automatic snow is not executed. [Seasons](SEASONS.md). |
| Native object atlases | Paired diffuse/normal pages, padded mips, UV remapping and compatible material grouping. | Fixture pixels/UVs/grouping checked. Unsupported shaders, repeating UVs and special alpha stay separate. [Atlases](ATLASES.md). |
| Optional Vulkan | Headless opaque BC7 compute with quality checks, measured Auto selection and CPU fallback. | Actual Mesa software-driver execution passes; physical GPU performance and driver coverage remain unverified. [Compute](COMPUTE.md). |
| Packaging and compatibility | Portable archive and AppImage, checksums/source/licenses, Ubuntu/Debian/Fedora smoke matrix. Flatpak evaluated. | Startup checks cover packaged applications; they do not establish full modlist/GPU compatibility. No Flatpak is published. [Packaging](PACKAGING.md). |
| CLI parity | Desktop presets, all persistent generation stages, exported jobs, diagnostics and job inspection. | Shared request tests and a generate/resume CLI fixture pass. UI-only preview/editor interactions remain graphical. [CLI](CLI.md). |

## Release validation still needed

The next validation work is game-side comparison on representative modlists: mixed-distance
terrain/water seams, tree silhouettes and UV alignment, seasonal transitions, and shader
brightness. Follow the [repeatable checks](DIAGNOSTICS.md#repeatable-checks) and attach paired
screenshots plus diagnostic exports. Automated structure checks do not substitute for this.

Physical AMD/Intel/NVIDIA Vulkan measurements and driver recovery tests are also needed before
making hardware speed claims. CPU remains the default. Additional shader/material support,
runtime-generated seasonal swaps and Flatpak profile grants need separate scoped work.

## Already in the first alpha

Native Linux Avalonia desktop and LOD CLI; direct Fluorine/MO2 profiles; offline CPU grass
caching; texture stitching, tree/grass billboards and hybrid trunk textures; existing 3D
LOD model consumption; combined generation; upstream occlusion and seasonal object/grass
support; progress, cancellation, logs, presets and author/repair tools.

Track work in [GitHub issues](https://github.com/opteron-x86/PenguLOD/issues).
The upstream [AnvilLOD roadmap](https://github.com/CageTV/AnvilLOD/blob/main/docs/ROADMAP.md)
remains context; PenguLOD tracks its own release status.
