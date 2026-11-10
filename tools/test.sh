#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/dotnet-env.sh"
if [[ "${1:-}" == "--smoke-settings" ]]; then
    export PENGULOD_SMOKE_SETTINGS="$(realpath "$2")"
    shift 2
fi
"$DOTNET" restore tests/PenguLOD.Core.Tests/PenguLOD.Core.Tests.csproj "${RESTORE_ARGS[@]}" \
    --disable-parallel --disable-build-servers -m:1 -nr:false
"$DOTNET" test tests/PenguLOD.Core.Tests/PenguLOD.Core.Tests.csproj -c Release --no-restore \
    --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false \
    --logger 'trx;LogFileName=linux.trx' --results-directory .artifacts/test-results "$@"

"$DOTNET" restore tests/PenguLOD.Desktop.Tests/PenguLOD.Desktop.Tests.csproj "${RESTORE_ARGS[@]}" \
    --disable-parallel --disable-build-servers -m:1 -nr:false
export PENGULOD_SCREENSHOTS="$PENGULOD_ROOT/.artifacts/screenshots"
"$DOTNET" test tests/PenguLOD.Desktop.Tests/PenguLOD.Desktop.Tests.csproj -c Release --no-restore \
    --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false \
    --logger 'trx;LogFileName=desktop.trx' --results-directory .artifacts/test-results "$@"
