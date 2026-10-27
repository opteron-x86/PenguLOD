#!/usr/bin/env bash
set -euo pipefail
source "$(dirname -- "${BASH_SOURCE[0]}")/dotnet-env.sh"
if [[ "${1:-}" == "--smoke-settings" ]]; then
    export ANVILLOD_SMOKE_SETTINGS="$(realpath "$2")"
    shift 2
fi
"$DOTNET" restore tests/AnvilLOD.Core.Tests/AnvilLOD.Core.Tests.csproj "${RESTORE_ARGS[@]}" \
    --disable-parallel --disable-build-servers -m:1 -nr:false
"$DOTNET" test tests/AnvilLOD.Core.Tests/AnvilLOD.Core.Tests.csproj -c Release --no-restore \
    --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false \
    --logger 'trx;LogFileName=linux.trx' --results-directory .artifacts/test-results "$@"

"$DOTNET" restore tests/AnvilLOD.Desktop.Tests/AnvilLOD.Desktop.Tests.csproj "${RESTORE_ARGS[@]}" \
    --disable-parallel --disable-build-servers -m:1 -nr:false
export ANVILLOD_SCREENSHOTS="$ANVILLOD_ROOT/.artifacts/screenshots"
"$DOTNET" test tests/AnvilLOD.Desktop.Tests/AnvilLOD.Desktop.Tests.csproj -c Release --no-restore \
    --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false \
    --logger 'trx;LogFileName=desktop.trx' --results-directory .artifacts/test-results "$@"
