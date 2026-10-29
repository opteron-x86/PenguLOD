# Optional Vulkan texture encoding

Preferences → **Texture compute** offers `Cpu` (default), `Auto`, and `Vulkan`. Both
accelerated modes require a usable hardware Vulkan compute queue and a passing decoded
quality check. Auto also requires at least a 20% speedup on the local 512×512 opaque
benchmark. Vulkan explicitly selects a qualifying hardware device without that speed
threshold. Any initialization or encode failure switches the job to CPU.

The native helper uses headless Vulkan 1.0 compute; it does not create a window or require
a display server. Large opaque images (at least 256×256, at most 4096×4096) use BC7 mode 6.
Alpha images keep the CPU's mode 5/6 quality selection, and small mip levels stay on CPU.
This accelerates texture compression, not asset scanning, filesystem writes, grass
placement/collision checks or mesh simplification.

Use **Check texture acceleration** in Preferences or:

```sh
PenguLOD compute-info --report compute.json
PenguLOD pipeline --request pipeline.json --texture-compute Auto
```

The report includes device type, actual CPU/Vulkan timings and decoded mean-squared
difference. Measured Vulkan time includes buffer allocation, upload, dispatch and readback;
both implementations are warmed before timing. Job logs record selected/fallback mode and
image counts. Software Vulkan is excluded from automatic acceleration even if its benchmark
is faster. `compute-info --allow-software` explicitly runs it for correctness testing.

Build requirements: a C compiler, Vulkan headers/loader and `glslangValidator`. Run
`bash tools/build-texture-compute.sh`; the normal package build also runs it. The managed
application works without the optional helper or without a Vulkan driver. Runtime Vulkan
drivers are supplied by the distribution, not bundled in the package.

CI checks encoding through Mesa's software Vulkan driver. Physical GPU performance and
driver stability require separate measurements; use the benchmark on the target system
before selecting acceleration for production jobs.
