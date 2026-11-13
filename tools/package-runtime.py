#!/usr/bin/env python3
"""Package the three compiled game-runtime builds as one mod-manager FOMOD."""
import hashlib
import pathlib
import shutil
import tempfile
import zipfile

LINES = ('1.5.97-1.6.1170', '1.7.x', '1.4.15-VR')


def package(root: pathlib.Path) -> pathlib.Path:
    version = (root / 'VERSION').read_text().strip()
    output = root / 'dist' / f'PenguLOD-{version}-SKSE.zip'
    with tempfile.TemporaryDirectory() as tmp:
        stage = pathlib.Path(tmp)
        for line in LINES:
            source = root / 'dist/runtime' / line
            dll = source / 'SKSE/Plugins/PenguLOD.dll'
            if not dll.is_file() or dll.read_bytes()[:2] != b'MZ':
                raise SystemExit(f'Missing or invalid Windows DLL: {dll}')
            # Only the DLL and license notices are shipped. Mod sites reject uploads that
            # contain source archives; the source is published in the repository instead.
            (stage / line / 'SKSE/Plugins').mkdir(parents=True)
            shutil.copyfile(dll, stage / line / 'SKSE/Plugins/PenguLOD.dll')
            shutil.copytree(source / 'licenses', stage / line / 'licenses')
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
        with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            for file in sorted(stage.rglob('*')):
                if file.is_file():
                    archive.write(file, file.relative_to(stage))
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    (root / 'dist/SHA256SUMS-RUNTIME').write_text(f'{digest}  {output.name}\n')
    return output


if __name__ == '__main__':
    print(package(pathlib.Path(__file__).resolve().parents[1]))
