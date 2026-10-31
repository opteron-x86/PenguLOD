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
# Local verification can use a separate candidate folder without replacing an installed build.
export PENGULOD_PACKAGE_DIR="${PENGULOD_PACKAGE_DIR:-$PWD/dist/linux-x64}"
python3 - <<'CHECK'
import os,pathlib
p=pathlib.Path(os.environ['PENGULOD_PACKAGE_DIR']).resolve()
dist=pathlib.Path('dist').resolve()
assert p.name=='linux-x64' and p.is_relative_to(dist), 'Package output must be a linux-x64 folder under dist/'
CHECK
rm -rf -- "$PENGULOD_PACKAGE_DIR"
mkdir -p "$PENGULOD_PACKAGE_DIR"
"$DOTNET" publish src/AnvilLOD.Cli/AnvilLOD.Cli.csproj -c Release -r linux-x64 \
    --self-contained true -o "$PENGULOD_PACKAGE_DIR" --disable-build-servers \
    -m:1 -nr:false -p:UseSharedCompilation=false "${RESTORE_ARGS[@]}"
"$DOTNET" publish src/AnvilLOD.Desktop/AnvilLOD.Desktop.csproj -c Release -r linux-x64 \
    --self-contained true -o "$PENGULOD_PACKAGE_DIR" --disable-build-servers \
    -m:1 -nr:false -p:UseSharedCompilation=false "${RESTORE_ARGS[@]}"
cp LICENSE.txt VERSION UPSTREAM_VERSION "$PENGULOD_PACKAGE_DIR"/
cp assets/pengulod-256.png "$PENGULOD_PACKAGE_DIR"/pengulod.png
cp tools/run-pengulod.sh tools/run-anvillod.sh tools/install-desktop.sh tools/repair-pglite-paths.py "$PENGULOD_PACKAGE_DIR"/
cp packaging/README-LINUX.md "$PENGULOD_PACKAGE_DIR"/README-LINUX.md
mkdir -p "$PENGULOD_PACKAGE_DIR/docs"
# Ship user documentation; contributor and release-maintenance guides stay in source.
for guide in USER-GUIDE SETTINGS TOOLS GRASS TEXTURES TERRAIN TREES OBJECTS RESOURCE-PACKS MODEL-SUPPORT MATERIALS SEASONS ATLASES PIPELINE CLI DIAGNOSTICS TROUBLESHOOTING COMPUTE PACKAGING PROVENANCE ROADMAP; do
    cp "docs/$guide.md" "$PENGULOD_PACKAGE_DIR/docs/"
done
mkdir -p "$PENGULOD_PACKAGE_DIR"/backends/fasterngio
cp -a .tools/fasterngio-runtime/. "$PENGULOD_PACKAGE_DIR"/backends/fasterngio/
chmod +x "$PENGULOD_PACKAGE_DIR"/run-pengulod.sh "$PENGULOD_PACKAGE_DIR"/run-anvillod.sh "$PENGULOD_PACKAGE_DIR"/install-desktop.sh
ANVILLOD_SDK_DIR="$(dirname -- "$(readlink -f "$DOTNET")")" python3 tools/collect-licenses.py
"$PENGULOD_PACKAGE_DIR/PenguLOD" --version
"$PENGULOD_PACKAGE_DIR/PenguLOD.Desktop" --version
tar -czf "dist/PenguLOD-$version-linux-x64.tar.gz" -C "$(dirname -- "$PENGULOD_PACKAGE_DIR")" linux-x64
printf '%s\n' "Desktop: $PENGULOD_PACKAGE_DIR/run-pengulod.sh" "CLI: $PENGULOD_PACKAGE_DIR/PenguLOD" "Archive: $ANVILLOD_ROOT/dist/PenguLOD-$version-linux-x64.tar.gz"
