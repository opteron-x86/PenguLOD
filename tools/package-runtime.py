#!/usr/bin/env python3
"""Package the three compiled game-runtime builds as one mod-manager FOMOD."""
import hashlib
import pathlib
import shutil
import subprocess
import tempfile
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
version = (root / 'VERSION').read_text().strip()
output = root / 'dist' / f'PenguLOD-{version}-SKSE.zip'
lines = ('1.5.97-1.6.1170', '1.7.x', '1.4.15-VR')
with tempfile.TemporaryDirectory() as tmp:
    stage = pathlib.Path(tmp)
    for line in lines:
        source = root / 'dist/runtime' / line
        dll = source / 'SKSE/Plugins/PenguLOD.dll'
        if not dll.is_file() or dll.read_bytes()[:2] != b'MZ':
            raise SystemExit(f'Missing or invalid Windows DLL: {dll}')
        shutil.copytree(source, stage / line)
    shutil.copytree(root / 'packaging/fomod', stage / 'fomod')
    info = stage / 'fomod/info.xml'
    info.write_text(info.read_text().replace('@VERSION@', version))
    (stage / 'fomod/images').mkdir(exist_ok=True)
    shutil.copyfile(root / 'assets/pengulod-256.png', stage / 'fomod/images/pengulod.png')
    ini = stage / 'common/SKSE/Plugins'
    ini.mkdir(parents=True)
    shutil.copyfile(root / 'packaging/PenguLOD.ini', ini / 'PenguLOD.ini')
    shutil.copyfile(root / 'LICENSE.txt', stage / 'LICENSE.txt')
    shutil.copyfile(root / 'docs/UPGRADING.md', stage / 'README.md')
    # The matching plugin source and build inputs accompany the binary package.
    subprocess.run(['git', 'archive', '--format=tar.gz', '-o', str(stage / 'PenguLOD-source.tar.gz'), 'HEAD'], cwd=root, check=True)
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for file in sorted(stage.rglob('*')):
            if file.is_file():
                archive.write(file, file.relative_to(stage))
digest = hashlib.sha256(output.read_bytes()).hexdigest()
(root / 'dist/SHA256SUMS-RUNTIME').write_text(f'{digest}  {output.name}\n')
print(output)
