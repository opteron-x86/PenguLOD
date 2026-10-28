#!/usr/bin/env bash
# Build a portable native Linux desktop and CLI package.
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/dotnet-env.sh"
case "${1:-}" in
    '') bash tools/build-fasterngio.sh ;;
    --reuse-backend)
        # Local development only. Release CI always builds and tests pinned backend sources.
        test -x .tools/fasterngio-runtime/FasterNGIO
        test -s .tools/fasterngio-runtime/FasterNGIO-source.tar.gz
        python3 - <<'PY'
import json, pathlib
meta = json.loads(pathlib.Path('.tools/fasterngio-runtime/backend.json').read_text())
assert meta['revision'] == '5a1bff6ad3e44b5b9c4b5acdd0663ae20860ca40', 'Unexpected backend revision'
assert {'headless', 'tesworld-pin', 'scheduler-progress', 'finite-blades'} <= set(meta['patches']), 'Missing backend patches'
PY
        ;;
    *) echo 'Usage: bash tools/build.sh [--reuse-backend]' >&2; exit 2 ;;
esac
bash tools/build-texture-compute.sh
version="$(python3 tools/version.py)"
# Only the dedicated package directory is cleared, never user output or settings.
rm -rf -- dist/linux-x64
mkdir -p dist/linux-x64
"$DOTNET" publish src/AnvilLOD.Cli/AnvilLOD.Cli.csproj -c Release -r linux-x64 \
    --self-contained true -o dist/linux-x64 --disable-build-servers \
    -m:1 -nr:false -p:UseSharedCompilation=false "${RESTORE_ARGS[@]}"
"$DOTNET" publish src/AnvilLOD.Desktop/AnvilLOD.Desktop.csproj -c Release -r linux-x64 \
    --self-contained true -o dist/linux-x64 --disable-build-servers \
    -m:1 -nr:false -p:UseSharedCompilation=false "${RESTORE_ARGS[@]}"
cp LICENSE.txt VERSION UPSTREAM_VERSION dist/linux-x64/
cp assets/pengulod-256.png dist/linux-x64/pengulod.png
cp tools/run-pengulod.sh tools/run-anvillod.sh tools/install-desktop.sh tools/repair-pglite-paths.py dist/linux-x64/
cp docs/LINUX-PORT.md dist/linux-x64/README-LINUX.md
cp docs/PROVENANCE.md dist/linux-x64/PROVENANCE.md
mkdir -p dist/linux-x64/backends/fasterngio
cp -a .tools/fasterngio-runtime/. dist/linux-x64/backends/fasterngio/
chmod +x dist/linux-x64/run-pengulod.sh dist/linux-x64/run-anvillod.sh dist/linux-x64/install-desktop.sh
ANVILLOD_SDK_DIR="$(dirname -- "$(readlink -f "$DOTNET")")" python3 tools/collect-licenses.py
"$ANVILLOD_ROOT/dist/linux-x64/PenguLOD" --version
"$ANVILLOD_ROOT/dist/linux-x64/PenguLOD.Desktop" --version
tar -czf "dist/PenguLOD-$version-linux-x64.tar.gz" -C dist linux-x64
printf '%s\n' "Desktop: $ANVILLOD_ROOT/dist/linux-x64/run-pengulod.sh" "CLI: $ANVILLOD_ROOT/dist/linux-x64/PenguLOD" "Archive: $ANVILLOD_ROOT/dist/PenguLOD-$version-linux-x64.tar.gz"
