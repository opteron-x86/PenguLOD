#!/usr/bin/env bash
set -euo pipefail
app_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# Compatibility alias for existing launch commands.
exec "$app_dir/run-pengulod.sh" "$@"
