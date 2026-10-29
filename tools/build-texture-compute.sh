#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
mkdir -p .artifacts/texture-compute .tools/texture-compute
includes=()
if [[ -f .tools/vulkan-headers/vulkan/vulkan_core.h ]]; then includes+=(-I.tools/vulkan-headers); fi
glslangValidator -V --target-env vulkan1.0 native/texture-compute/bc7.comp -o .artifacts/texture-compute/bc7.spv
python3 tools/spirv-header.py .artifacts/texture-compute/bc7.spv .artifacts/texture-compute/bc7_spv.h
"${CC:-cc}" -std=c11 -O2 -fPIC -shared -Wall -Wextra -Werror \
    "${includes[@]}" -I.artifacts/texture-compute native/texture-compute/backend.c \
    -Wl,-z,relro,-z,now -lvulkan -o .tools/texture-compute/libpengulod_compute.so
cp native/texture-compute/bc7.comp .tools/texture-compute/
