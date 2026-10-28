#!/usr/bin/env bash
# Package the portable payload. The tool and its embedded runtime are SHA-256 pinned.
set -euo pipefail
cd "$(dirname -- "${BASH_SOURCE[0]}")/.."
version="$(python3 tools/version.py)"
payload="${PENGULOD_PACKAGE_DIR:-$PWD/dist/linux-x64}"
test -x "$payload/PenguLOD.Desktop"
mkdir -p .tools .artifacts/appimage-tool dist
tool="$PWD/.tools/appimagetool.AppImage"
if [[ ! -f "$tool" ]]; then
    curl -fL --retry 3 https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage -o "$tool"
fi
python3 - "$tool" <<'PY'
import hashlib,pathlib,sys
p=pathlib.Path(sys.argv[1])
assert hashlib.sha256(p.read_bytes()).hexdigest()=='ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0', 'AppImage tool checksum mismatch'
p.chmod(0o755)
PY
# Extract instead of mounting: packaging does not need FUSE or elevated privileges.
(cd .artifacts/appimage-tool && "$tool" --appimage-extract > extract.log)
offset="$($tool --appimage-offset)"
python3 - "$tool" "$offset" <<'PY'
import pathlib,sys
pathlib.Path('.artifacts/appimage-tool/runtime').write_bytes(pathlib.Path(sys.argv[1]).read_bytes()[:int(sys.argv[2])])
PY
appdir="$PWD/.artifacts/PenguLOD.AppDir"
rm -rf -- "$appdir"
mkdir -p "$appdir/usr/bin" "$appdir/usr/lib/PenguLOD" "$appdir/usr/share/metainfo" \
    "$appdir/usr/share/applications" "$appdir/usr/share/icons/hicolor/256x256/apps"
cp -a "$payload/." "$appdir/usr/lib/PenguLOD/"
cp packaging/AppRun "$appdir/AppRun"
cp packaging/io.github.opteron_x86.PenguLOD.desktop "$appdir/"
cp packaging/io.github.opteron_x86.PenguLOD.desktop "$appdir/usr/share/applications/"
cp packaging/io.github.opteron_x86.PenguLOD.metainfo.xml "$appdir/usr/share/metainfo/io.github.opteron_x86.PenguLOD.appdata.xml"
cp assets/pengulod-256.png "$appdir/pengulod.png"
cp assets/pengulod-256.png "$appdir/usr/share/icons/hicolor/256x256/apps/pengulod.png"
chmod +x "$appdir/AppRun"
ln -s ../../AppRun "$appdir/usr/bin/PenguLOD.Desktop"
# Validate the complete installed tree offline; appimagetool's single-file validator
# otherwise checks host-installed desktop entries and makes unnecessary network requests.
appstreamcli validate-tree --no-net "$appdir"
ARCH=x86_64 .artifacts/appimage-tool/squashfs-root/AppRun --no-appstream --runtime-file .artifacts/appimage-tool/runtime \
    --mksquashfs-opt -processors --mksquashfs-opt 2 "$appdir" "$PWD/dist/PenguLOD-$version-x86_64.AppImage"
