#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
"${CXX:-c++}" -std=c++23 -Wall -Wextra -Werror -I skse/src -include skse/tests/RuntimeTestSupport.h skse/tests/IdentityTests.cpp skse/src/IniFile.cpp skse/src/DataFile.cpp -o "$work/runtime-tests"
"$work/runtime-tests" "$work"
