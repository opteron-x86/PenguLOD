#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$root/.artifacts/native-smoke"
timeout 20s "$root/dist/linux-x64/PenguLOD.Desktop" --settings "$root/.artifacts/native-smoke/settings.json" --smoke-test
