#!/usr/bin/env bash
# Pinned, native CPU BVH backend. No game process, Wine, or second GUI.
# Build dependencies: CMake >=3.28, Ninja, a C++23 compiler, zlib, lz4, TBB,
# spdlog/fmt and GoogleTest development packages; curl, Python 3 and patch.
set -euo pipefail
ANVILLOD_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ANVILLOD_ROOT"
mkdir -p .tools .artifacts
fetch_source() {
    local name="$1" repo="$2" revision="$3" destination="$4"
    local archive="$ANVILLOD_ROOT/.tools/$name-source.tar.gz"
    if [[ ! -f "$archive" ]]; then
        curl -fL --retry 3 "https://codeload.github.com/$repo/tar.gz/$revision" -o "$archive.tmp"
        mv "$archive.tmp" "$archive"
    fi
    python3 - "$archive" tools/fasterngio/sources.sha256 <<'PY'
import hashlib, pathlib, sys
archive = pathlib.Path(sys.argv[1])
expected = dict(line.split()[::-1] for line in pathlib.Path(sys.argv[2]).read_text().splitlines())
if hashlib.file_digest(archive.open('rb'), 'sha256').hexdigest() != expected[archive.name]:
    raise SystemExit('Source checksum mismatch: ' + str(archive))
PY
    if [[ ! -f "$destination/.anvillod-revision" ]]; then
        python3 - "$archive" "$destination" <<'PY'
import pathlib, sys, tarfile
root = pathlib.Path(sys.argv[2]); root.mkdir(parents=True, exist_ok=True)
with tarfile.open(sys.argv[1]) as tar:
    for member in tar.getmembers():
        member.name = '/'.join(member.name.split('/')[1:])
        if member.name: tar.extract(member, root, filter='data')
PY
        printf '%s\n' "$revision" > "$destination/.anvillod-revision"
    fi
    [[ "$(cat "$destination/.anvillod-revision")" == "$revision" ]] || { echo "Wrong revision in $destination" >&2; exit 1; }
}
fetch_source fasterngio panthuncia/FasterNGIO 5a1bff6ad3e44b5b9c4b5acdd0663ae20860ca40 .tools/FasterNGIO
fetch_source BasicTelemetry panthuncia/BasicTelemetry 3bd82d9884cbc693a2cf98a566882e705f801425 .tools/FasterNGIO/external/BasicTelemetry
fetch_source ORGModuleServices panthuncia/ORGModuleServices 22993f32e418737fa823fcec5a4f6f3232bb0e30 .tools/FasterNGIO/external/ORGModuleServices
fetch_source tesworld panthuncia/TESWorld 763aee7622b336e05bf33893a372e3382b2431a4 .tools/FasterNGIO/external/tesworld
fetch_source nifly ousnius/nifly cca0a770094bb962fb28ea1fec5ea903e68fda8e .tools/FasterNGIO/external/nifly
if [[ ! -f .tools/FasterNGIO/.anvillod-headless ]]; then
    patch -d .tools/FasterNGIO -p1 < tools/fasterngio/headless.patch
    touch .tools/FasterNGIO/.anvillod-headless
fi
if [[ ! -f .tools/FasterNGIO/.anvillod-tesworld-pin ]]; then
    patch -d .tools/FasterNGIO -p1 < tools/fasterngio/tesworld-pin.patch
    touch .tools/FasterNGIO/.anvillod-tesworld-pin
fi
apply_backend_patch() {
    local name="$1" checksum
    checksum="$(sha256sum "tools/fasterngio/$name.patch" | cut -d ' ' -f1)"
    if [[ ! -f ".tools/FasterNGIO/.anvillod-$name" ]]; then
        patch -d .tools/FasterNGIO -p1 < "tools/fasterngio/$name.patch"
        printf '%s\n' "$checksum" > ".tools/FasterNGIO/.anvillod-$name"
    fi
    [[ "$(cat ".tools/FasterNGIO/.anvillod-$name")" == "$checksum" ]] || {
        echo "FasterNGIO $name patch changed. Re-extract the pinned backend before rebuilding." >&2; exit 1;
    }
}
apply_backend_patch scheduler-progress
apply_backend_patch finite-blades
cmake -S .tools/FasterNGIO -B .tools/fasterngio-build -G Ninja \
    -DCMAKE_BUILD_TYPE=Release -DFASTERNGIO_ENABLE_GPU=OFF -DFASTERNGIO_HEADLESS=ON \
    -DBASICTELEMETRY_ENABLE_TRACY=OFF -DFASTERNGIO_BUILD_TESTS=ON \
    -Dlz4_DIR="$ANVILLOD_ROOT/tools/fasterngio/cmake" \
    '-DCMAKE_INSTALL_RPATH=$ORIGIN/lib' -DCMAKE_EXE_LINKER_FLAGS=-Wl,--disable-new-dtags
cmake --build .tools/fasterngio-build --parallel "${ANVILLOD_NATIVE_JOBS:-4}"
ctest --test-dir .tools/fasterngio-build --output-on-failure --parallel 4
cmake --install .tools/fasterngio-build --component FasterNGIO --strip --prefix "$ANVILLOD_ROOT/.tools/fasterngio-runtime"
python3 tools/fasterngio/package.py
