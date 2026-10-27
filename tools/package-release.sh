#!/usr/bin/env bash
# Package the committed source alongside an already built Linux archive.
set -euo pipefail
cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
version="$(python3 tools/version.py)"
if [[ -n "$(git status --porcelain)" ]]; then
    echo 'Commit the source tree before packaging a release.' >&2; exit 1
fi
test -s "dist/PenguLOD-$version-linux-x64.tar.gz"
git archive --format=tar.gz --prefix="PenguLOD-$version/" --output="dist/PenguLOD-$version-source.tar.gz" HEAD
python3 - "$version" <<'PY'
import pathlib, sys
version = sys.argv[1]
changelog = pathlib.Path('CHANGELOG.md').read_text()
heading = f'## [{version}]'
if heading not in changelog:
    raise SystemExit('Missing release changelog: ' + version)
notes = changelog.split(heading, 1)[1].split('\n', 1)[1].split('\n## ', 1)[0].strip()
pathlib.Path('dist/RELEASE-NOTES.md').write_text(notes + '\n')
PY
cd dist
sha256sum "PenguLOD-$version-linux-x64.tar.gz" "PenguLOD-$version-source.tar.gz" > SHA256SUMS
