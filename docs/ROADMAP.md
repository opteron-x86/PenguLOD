# Roadmap

PenguLOD currently provides a native grass → textures → terrain → object/tree/grass LOD
pipeline, with resumable stages, diagnostics, a desktop and CLI. The project is in alpha.
The [user guide](USER-GUIDE.md) and feature guides describe the supported workflows and limits.

## Next priorities

1. **3D tree quality and coverage.** Improve reduction of dense foliage and support
   record-specific texture variants in native models. Compare near/far silhouettes,
   hybrid trunk alignment and billboard transitions. Keep per-level budgets and explicit
   fallback reasons. Track transition validation in [#3](https://github.com/opteron-x86/PenguLOD/issues/3).
2. **Terrain material matching.** Add source normal detail and broader terrain PBR material
   handling. Check brightness, shorelines, mixed-distance seams and terrain shadows against
   loaded terrain. Track terrain validation in [#2](https://github.com/opteron-x86/PenguLOD/issues/2).
3. **Seasonal consistency.** Exercise terrain, grass, billboards and 3D tree replacements
   together across seasons. Improve coverage reporting for missing swaps and unsupported
   materials. Runtime-generated swaps currently require saved INI files.
4. **Performance and recovery.** Measure scan, texture, terrain and publication costs on
   representative workloads. Prioritize the slowest stages and finer-grained resume for
   expensive stages. Retain cancellation, output verification and reliable CPU fallbacks.

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
