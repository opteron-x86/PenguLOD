# Roadmap

PenguLOD currently provides a native grass → textures → terrain → object/tree/grass LOD
pipeline, with resumable stages, diagnostics, a desktop and CLI. The project is in alpha.
The [user guide](USER-GUIDE.md) and feature guides describe the supported workflows and limits.

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
runtime automatic snow generation is outside the native generator.

## Hardware and distribution

Validate Vulkan encoding on physical AMD, Intel and NVIDIA devices, including complete-job
timing, decoded image quality and device-failure recovery. Track results in
[#6](https://github.com/opteron-x86/PenguLOD/issues/6). Software-driver tests establish
correctness but do not establish hardware performance. CPU remains the default.

Maintain portable and AppImage compatibility checks on Ubuntu, Debian and Fedora.
Flatpak, additional architectures and Windows packages require separate packaging and
platform validation; they are not current release targets.

## Acceptance criteria

Changes should include focused regression checks, actionable coverage reports, and
repeatable in-game comparisons where output appearance is affected. Use the
[transition checklist](DIAGNOSTICS.md#repeatable-checks). Broad mod/shader compatibility
and complete TexGen/DynDOLOD parity are ongoing work, not guarantees of the current release.

Track and propose work in [GitHub issues](https://github.com/opteron-x86/PenguLOD/issues).
