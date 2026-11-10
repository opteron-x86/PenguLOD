#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
"${CXX:-c++}" -std=c++23 -Wall -Wextra -Werror -I skse/src skse/tests/IdentityTests.cpp skse/src/IniFile.cpp -o "$work/runtime-tests"
"$work/runtime-tests" "$work"
