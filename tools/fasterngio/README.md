# Bundled FasterNGIO

Upstream: https://github.com/panthuncia/FasterNGIO, GPL-3.0, revision
`5a1bff6ad3e44b5b9c4b5acdd0663ae20860ca40`. The build script pins and verifies
FasterNGIO and the four submodules used by the CPU build. It does not build the GPU stack.

`headless.patch` makes the ImGui launcher optional; AnvilLOD supplies the UI.
`tesworld-pin.patch` reads the No Grass worldspace flag directly, because this
FasterNGIO revision calls `HasNoGrass()` but its pinned TESWorld lacks that helper.
The flag is already decoded by TESWorld. Existing upstream tests cover this behavior.
`scheduler-progress.patch` gives every graph scope an explicit TBB arena and uses
`enqueue(defer(...))` for writer/timer wakeups. The original adapter inherited the
caller's arena through `task_group::run`, which can strand work when that arena has
no available workers. The regression test reproduces this by dispatching from an
arena with no worker slots; it fails before the patch and passes afterwards.
The patch also exercises writer backpressure, bounds native test timeouts, and
reports CLI cell/write progress every five seconds, with a warning after two
minutes without progress during generation or final writes. The warning does not
abort long-running work. The exact original live-run deadlock was not reproduced.
The scheduling guarantee used here is documented in
[oneTBB's task_arena reference](https://uxlfoundation.github.io/oneTBB/main/reference/source/task_scheduler/task_arena/task_arena_cls.html).
`finite-blades.patch` omits blades whose encoded half-float position/scale is NaN
or infinite, and logs each affected cell. This fixes six unrepresentable blades
in two `ksws07World` cells in the local profile; their infinite Z coordinates
produced NaN block bounds. Filtering happens after placement and rejection,
without changing RNG draws, coordinates or payloads of valid blades. The tests
cover overflow, NaN, infinity, all-invalid cells and unchanged valid output.
Placement and collision algorithms remain unchanged; only invalid blades are
excluded before serialization.

From the AnvilLOD checkout, use `bash tools/build-fasterngio.sh` (also run by
`tools/build.sh`). GCC 14+, CMake 3.28+, Ninja, pkg-config, Python 3.12+, curl, patch,
and development packages for zlib, lz4, TBB, fmt, spdlog and GoogleTest are needed.

For a source archive extracted from the packaged backend, the `FasterNGIO/` tree
already includes the four dependency submodules and all applied patches. Build it directly:

```bash
cmake -S FasterNGIO -B build -G Ninja -DCMAKE_BUILD_TYPE=Release \
  -DFASTERNGIO_ENABLE_GPU=OFF -DFASTERNGIO_HEADLESS=ON \
  -DBASICTELEMETRY_ENABLE_TRACY=OFF -DFASTERNGIO_BUILD_TESTS=ON \
  -Dlz4_DIR="$PWD/anvillod-build/tools/fasterngio/cmake"
cmake --build build --parallel 4
ctest --test-dir build --output-on-failure
```

Runtime libraries from the build host are packaged alongside the executable and
loaded with `$ORIGIN/lib`. The resulting glibc baseline is the build host's; build on
your oldest target distribution for wider compatibility. The local CachyOS build
requires glibc 2.43, while the CI configuration targets Ubuntu 24.04.
