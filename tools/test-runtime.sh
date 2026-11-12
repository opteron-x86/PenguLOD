#!/usr/bin/env bash
# Builds and runs the SKSE runtime's CommonLib-free code on Linux: path selection, INI parsing and migration, the binary data
# readers (against the generator's own output in skse/tests/fixtures), the dynamic LOD show plan and the adaptive policy.
set -euo pipefail
cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
flags=(-std=c++23 -Wall -Wextra -Werror -I skse/src -include skse/tests/RuntimeTestSupport.h)
"${CXX:-c++}" "${flags[@]}" skse/tests/IdentityTests.cpp skse/src/IniFile.cpp skse/src/DataFile.cpp -o "$work/runtime-tests"
"${CXX:-c++}" "${flags[@]}" skse/tests/LogicTests.cpp skse/src/IniParse.cpp skse/src/IniFile.cpp skse/src/DataFile.cpp \
    skse/src/Settings.cpp -o "$work/runtime-logic-tests"
mkdir -p "$work/identity" "$work/logic"
"$work/runtime-tests" "$work/identity"
"$work/runtime-logic-tests" "$work/logic" "$PWD/skse/tests/fixtures"
