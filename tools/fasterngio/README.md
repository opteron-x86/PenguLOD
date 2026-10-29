# Bundled FasterNGIO

PenguLOD includes [FasterNGIO](https://github.com/panthuncia/FasterNGIO), GPL-3.0,
at revision `5a1bff6ad3e44b5b9c4b5acdd0663ae20860ca40`. The build pins and verifies
FasterNGIO and the four submodules used by the CPU build. The GPU stack is not bundled.

## Maintained patches

- `headless.patch` makes the ImGui launcher optional so PenguLOD can supply the UI.
- `tesworld-pin.patch` reads the No Grass worldspace flag directly for compatibility
  with the pinned TESWorld API.
- `scheduler-progress.patch` gives graph scopes an explicit TBB arena and uses
  `enqueue(defer(...))` for writer/timer wakeups, so dispatch does not depend on worker
  availability in a caller's arena. It also reports cell/write progress every five
  seconds and warns after two minutes without progress. Regression tests cover arena
  starvation, writer backpressure and bounded test execution.
- `finite-blades.patch` omits placements whose encoded half-float position or scale
  is non-finite, and logs affected cells. Filtering happens after placement and
  rejection; valid placements and randomization are unchanged. Tests cover overflow,
  NaN, infinity, all-invalid cells and preserved valid output.

## Build from the PenguLOD checkout

Run `bash tools/build-fasterngio.sh`, also invoked by `tools/build.sh`. Requirements:
GCC 14+, CMake 3.28+, Ninja, pkg-config, Python 3.12+, curl, patch, and development
packages for zlib, lz4, TBB, fmt, spdlog and GoogleTest.

## Build from the packaged source archive

The `FasterNGIO/` tree includes all four dependency submodules and applied patches:

```sh
cmake -S FasterNGIO -B build -G Ninja -DCMAKE_BUILD_TYPE=Release \
  -DFASTERNGIO_ENABLE_GPU=OFF -DFASTERNGIO_HEADLESS=ON \
  -DBASICTELEMETRY_ENABLE_TRACY=OFF -DFASTERNGIO_BUILD_TESTS=ON \
  -Dlz4_DIR="$PWD/anvillod-build/tools/fasterngio/cmake"
cmake --build build --parallel 4
ctest --test-dir build --output-on-failure
```

Runtime libraries from the build host are packaged beside the executable and loaded
with `$ORIGIN/lib`. The glibc baseline follows the build host; official packages build
on Ubuntu 24.04. Build on the oldest supported distribution for wider compatibility.
