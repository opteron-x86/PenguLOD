#!/usr/bin/env bash
# Build the native Linux CLI and Avalonia desktop application.
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/dotnet-env.sh"
bash tools/build-fasterngio.sh
"$DOTNET" publish src/AnvilLOD.Cli/AnvilLOD.Cli.csproj -c Release -r linux-x64 \
    --self-contained true -o dist/linux-x64 --disable-build-servers \
    -m:1 -nr:false -p:UseSharedCompilation=false "${RESTORE_ARGS[@]}"
"$DOTNET" publish src/AnvilLOD.Desktop/AnvilLOD.Desktop.csproj -c Release -r linux-x64 \
    --self-contained true -o dist/linux-x64 --disable-build-servers \
    -m:1 -nr:false -p:UseSharedCompilation=false "${RESTORE_ARGS[@]}"
cp LICENSE.txt dist/linux-x64/LICENSE.txt
cp assets/anvillod-256.png dist/linux-x64/anvillod.png
cp tools/run-anvillod.sh tools/install-desktop.sh tools/repair-pglite-paths.py dist/linux-x64/
cp docs/LINUX-PORT.md dist/linux-x64/README-LINUX.md
mkdir -p dist/linux-x64/backends/fasterngio
cp -a .tools/fasterngio-runtime/. dist/linux-x64/backends/fasterngio/
chmod +x dist/linux-x64/run-anvillod.sh dist/linux-x64/install-desktop.sh
ANVILLOD_SDK_DIR="$(dirname -- "$(readlink -f "$DOTNET")")" python3 tools/collect-licenses.py
tar -czf dist/AnvilLOD-linux-x64.tar.gz -C dist linux-x64
printf '%s\n' "Desktop: $ANVILLOD_ROOT/dist/linux-x64/run-anvillod.sh" "CLI: $ANVILLOD_ROOT/dist/linux-x64/AnvilLOD" "Archive: $ANVILLOD_ROOT/dist/AnvilLOD-linux-x64.tar.gz"
