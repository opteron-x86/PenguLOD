#!/usr/bin/env bash
# Shared environment for the Linux build and test scripts.
set -euo pipefail
ANVILLOD_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ANVILLOD_ROOT"

PENGULOD_ROOT="$ANVILLOD_ROOT"
if [[ -n "${PENGULOD_DOTNET:-}" ]]; then
    DOTNET="$PENGULOD_DOTNET"
elif [[ -n "${ANVILLOD_DOTNET:-}" ]]; then
    DOTNET="$ANVILLOD_DOTNET"
elif [[ -x "$ANVILLOD_ROOT/.tools/dotnet/dotnet" ]]; then
    DOTNET="$ANVILLOD_ROOT/.tools/dotnet/dotnet"
else
    DOTNET="$(command -v dotnet || true)"
fi
if [[ -z "$DOTNET" || ! -x "$DOTNET" ]]; then
    echo "Install the .NET 10 SDK, or set PENGULOD_DOTNET to its dotnet executable." >&2
    exit 1
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$ANVILLOD_ROOT/.tools/cli-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$ANVILLOD_ROOT/.tools/nuget}"
RESTORE_ARGS=()
if [[ -n "${PENGULOD_NUGET_SOURCE:-}" ]]; then
    RESTORE_ARGS+=(--source "$PENGULOD_NUGET_SOURCE")
elif [[ -n "${ANVILLOD_NUGET_SOURCE:-}" ]]; then
    RESTORE_ARGS+=(--source "$ANVILLOD_NUGET_SOURCE")
fi
