# Roadmap

PenguLOD currently provides a native grass → textures → terrain → object/tree/grass LOD
pipeline, with resumable stages, diagnostics, a desktop and CLI. The project is in alpha.
The [user guide](USER-GUIDE.md) and feature guides describe the supported workflows and limits.

## Compatibility priorities

Each priority has a milestone, implementation issues and acceptance checks. Work advances
one milestone at a time; an implementation milestone does not claim universal mod parity.

| Priority | Scope | Tracking |
| --- | --- | --- |
| 1 — Recipes and texture replacement | Native conversion recipes, recipe UV layouts, record/season atlas variants, diagnostics and pipeline recovery | [v0.4 milestone](https://github.com/opteron-x86/PenguLOD/milestone/6), [#16](https://github.com/opteron-x86/PenguLOD/issues/16), [#17](https://github.com/opteron-x86/PenguLOD/issues/17), [#18](https://github.com/opteron-x86/PenguLOD/issues/18) |
| 2 — HD tree and grass billboards | HD material/camera fidelity, complex grass, reference scale coverage | [Milestone](https://github.com/opteron-x86/PenguLOD/milestone/7), [#19](https://github.com/opteron-x86/PenguLOD/issues/19) |
| 3 — Model and render support | More NIF geometry/material variants and render-template coverage | [Milestone](https://github.com/opteron-x86/PenguLOD/milestone/8), [#20](https://github.com/opteron-x86/PenguLOD/issues/20) |
| 4 — Automatic object LOD | Integrated discovery, generation and coverage of missing object LOD models | [Milestone](https://github.com/opteron-x86/PenguLOD/milestone/9), [#21](https://github.com/opteron-x86/PenguLOD/issues/21) |
| 5 — Hybrid tree authoring | Generate aligned trunk/crown resources and hybrid models from full trees | [Milestone](https://github.com/opteron-x86/PenguLOD/milestone/10), [#22](https://github.com/opteron-x86/PenguLOD/issues/22) |
| 6 — Resource-pack discovery | Discover compatible installed data packs and explain missing resources | [Milestone](https://github.com/opteron-x86/PenguLOD/milestone/11), [#23](https://github.com/opteron-x86/PenguLOD/issues/23) |

Priority 1 is implemented in v0.4.0-alpha.1. [Texture generation](TEXTURES.md) documents
supported recipes, ambiguous mappings and how to pass generation metadata to LOD.
Priority 3 is implemented in v0.4.0-alpha.3; [model support](MODEL-SUPPORT.md) defines the
supported geometry, static skin poses, render materials and failure behavior.
Priority 2 is implemented in v0.4.0-alpha.9; [billboard settings](TEXTURES.md#hd-billboards-and-complex-grass)
describe separate views, packed grass inputs, resolution scaling and shader limits.
Priorities 4–6 are queued. In-game visual validation remains ongoing.

## v0.3 quality milestones

| Milestone | Delivered | Tracking |
| --- | --- | --- |
| 3D tree quality | Record texture variants, whole-card foliage reduction with configurable sampled coverage limits, near/far cost reports | [Milestone](https://github.com/opteron-x86/PenguLOD/milestone/2), [#10](https://github.com/opteron-x86/PenguLOD/issues/10), [#11](https://github.com/opteron-x86/PenguLOD/issues/11) |
| Terrain materials | Source-normal detail, default normal INI support, PBR albedo/AO calibration and optional twin lookup | [Milestone](https://github.com/opteron-x86/PenguLOD/milestone/3), [#12](https://github.com/opteron-x86/PenguLOD/issues/12) |
| Seasonal consistency | Swap diagnostics, Default/WIN/SPR/SUM/AUT integration tests, output counts and failure preservation | [Milestone](https://github.com/opteron-x86/PenguLOD/milestone/4), [#13](https://github.com/opteron-x86/PenguLOD/issues/13) |
| Performance and recovery | Verified per-tile terrain resume, phase timings, bounded sample caches and reproducible synthetic benchmarks | [Milestone](https://github.com/opteron-x86/PenguLOD/milestone/5), [#14](https://github.com/opteron-x86/PenguLOD/issues/14) |

These milestones cover implementation and automated acceptance. In-game terrain appearance,
shorelines, mixed-distance seams and shader compatibility remain tracked in
[#2](https://github.com/opteron-x86/PenguLOD/issues/2). Tree silhouettes, hybrid trunk alignment,
foliage shading and billboard transitions remain tracked in
[#3](https://github.com/opteron-x86/PenguLOD/issues/3). Seasonal swaps use written INI files;
the terrain/tree/object stages require runtime automatic swaps to be saved first.

## Hardware and distribution

Validate Vulkan encoding on physical AMD, Intel and NVIDIA devices, including complete-job
timing, decoded image quality and device-failure recovery. Track results in
[#6](https://github.com/opteron-x86/PenguLOD/issues/6). Software-driver tests establish
correctness but do not establish hardware performance. CPU remains the default.

Maintain portable and AppImage compatibility checks on Ubuntu, Debian and Fedora, plus
Windows x64 ZIP checks on separate Windows build and runtime runners. Windows desktop
modlists and physical GPU drivers still need broader validation. Flatpak and additional
architectures are not current release targets.

## Acceptance criteria

Changes should include focused regression checks, actionable coverage reports, and
repeatable in-game comparisons where output appearance is affected. Use the
[transition checklist](DIAGNOSTICS.md#repeatable-checks). Broad mod/shader compatibility
and complete TexGen/DynDOLOD parity are ongoing work, not guarantees of the current release.

Track and propose work in [GitHub issues](https://github.com/opteron-x86/PenguLOD/issues).
