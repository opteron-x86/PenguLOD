#!/usr/bin/env bash
# Pinned, native CPU BVH backend. No game process, Wine, or second GUI.
# Build dependencies: CMake >=3.28, Ninja, a C++23 compiler, zlib, lz4, TBB,
# spdlog/fmt and GoogleTest development packages; curl, Python 3 and patch.
set -euo pipefail
ANVILLOD_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ANVILLOD_ROOT"
mkdir -p .tools .artifacts
python3 tools/fasterngio/prepare.py
cmake -S .tools/FasterNGIO -B .tools/fasterngio-build -G Ninja \
    -DCMAKE_BUILD_TYPE=Release -DFASTERNGIO_ENABLE_GPU=OFF -DFASTERNGIO_HEADLESS=ON \
    -DBASICTELEMETRY_ENABLE_TRACY=OFF -DFASTERNGIO_BUILD_TESTS=ON \
    -Dlz4_DIR="$ANVILLOD_ROOT/tools/fasterngio/cmake" \
    '-DCMAKE_INSTALL_RPATH=$ORIGIN/lib' -DCMAKE_EXE_LINKER_FLAGS=-Wl,--disable-new-dtags
cmake --build .tools/fasterngio-build --parallel "${ANVILLOD_NATIVE_JOBS:-4}"
ctest --test-dir .tools/fasterngio-build --output-on-failure --parallel 4
cmake --install .tools/fasterngio-build --component FasterNGIO --strip --prefix "$ANVILLOD_ROOT/.tools/fasterngio-runtime"
python3 tools/fasterngio/package.py
