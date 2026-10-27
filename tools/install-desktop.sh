#!/usr/bin/env bash
# Optional per-user application-menu entry. Run after moving the portable folder to its final location.
set -euo pipefail
app_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
python3 - "$app_dir" <<'PY'
import os, pathlib, sys
app = pathlib.Path(sys.argv[1])
applications = pathlib.Path(os.environ.get('XDG_DATA_HOME', str(pathlib.Path.home() / '.local/share'))) / 'applications'
applications.mkdir(parents=True, exist_ok=True)
def quoted(value):
    # Desktop Entry Exec escaping, including its separate percent-field expansion.
    return '"' + str(value).replace('\\', '\\\\\\\\').replace('"', '\\\\"').replace('`', '\\\\`').replace('$', '\\\\$').replace('%', '%%') + '"'
entry = applications / 'anvillod.desktop'
entry.write_text('[Desktop Entry]\nType=Application\nName=AnvilLOD\nComment=Native Skyrim LOD generator\n'
                 + 'Exec=' + quoted(app / 'AnvilLOD.Desktop') + '\n'
                 + 'Icon=' + str(app / 'anvillod.png') + '\nTerminal=false\nCategories=Utility;\nStartupWMClass=AnvilLOD.Desktop\n')
print(f'Installed application-menu entry: {entry}')
PY
