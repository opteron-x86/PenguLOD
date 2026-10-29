"""Create a ZIP of the self-contained Windows publish and a checksum for release CI."""
import hashlib
import pathlib
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
version = (root / 'VERSION').read_text().strip()
package = root / 'dist/win-x64'
for name in ['PenguLOD.exe', 'PenguLOD.Desktop.exe', 'coreclr.dll', 'pengulod_compute.dll',
             'backends/fasterngio/FasterNGIO.exe', 'backends/fasterngio/FasterNGIO-source.tar.gz',
             'THIRD-PARTY-PACKAGES.txt', 'ThirdPartyNotices.txt']:
    if not (package / name).is_file():
        raise SystemExit('Incomplete Windows package: ' + name)
for item in package.rglob('*'):
    if item.is_file() and (item.suffix == '.so' or '.so.' in item.name or item.suffix == '.sh'):
        raise SystemExit('Linux file included in Windows package: ' + str(item))
archive = root / 'dist' / f'PenguLOD-{version}-win-x64.zip'
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as target:
    for item in sorted(package.rglob('*')):
        if item.is_file():
            target.write(item, item.relative_to(package.parent).as_posix())
with archive.open('rb') as stream:
    digest = hashlib.file_digest(stream, 'sha256').hexdigest()
(archive.parent / 'SHA256SUMS-WINDOWS').write_text(digest + '  ' + archive.name + '\n', encoding='utf-8')
print(archive)
