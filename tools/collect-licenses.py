#!/usr/bin/env python3
"""Record the exact restored dependencies and retain licenses shipped inside their packages."""
import json
import os
import pathlib
import shutil
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[1]
out = root / 'dist/linux-x64'
packages = pathlib.Path(os.environ.get('NUGET_PACKAGES', str(root / '.tools/nuget')))
licenses = out / 'licenses'
licenses.mkdir(exist_ok=True)
for file in (root / 'assets/licenses').glob('*'):
    shutil.copyfile(file, licenses / file.name)
for runtime in (packages / 'microsoft.netcore.app.runtime.linux-x64').glob('*'):
    if (runtime / 'LICENSE.TXT').exists():
        shutil.copyfile(runtime / 'LICENSE.TXT', licenses / ('dotnet-' + runtime.name + '-LICENSE.txt'))
sdk_notices = pathlib.Path(os.environ.get('ANVILLOD_SDK_DIR', str(root / '.tools/dotnet'))) / 'ThirdPartyNotices.txt'
if sdk_notices.exists():
    shutil.copyfile(sdk_notices, out / 'ThirdPartyNotices.txt')
shutil.copyfile(root / 'assets/water/README.txt', licenses / 'Water-textures.txt')
lines = ['Third-party package inventory', '=============================', '',
         'AnvilLOD is GPL-3.0-only. Dependencies retain their own licenses.',
         'Exact runtime package versions are also recorded in the .deps.json files.',
         'Bundled .NET runtime notices are in ThirdPartyNotices.txt.', '']
assets = json.loads((root / 'src/AnvilLOD.Desktop/obj/project.assets.json').read_text())
for name, item in sorted(assets['libraries'].items()):
    if item['type'] != 'package':
        continue
    folder = packages / item['path']
    nuspec = next(folder.glob('*.nuspec'))
    meta = ET.parse(nuspec).getroot()
    def text(tag):
        e = next((e for e in meta.iter() if e.tag.split('}')[-1] == tag), None)
        return e.text if e is not None and e.text else ''
    lines += [name, 'License: ' + (text('license') or text('licenseUrl') or 'See project'), 'Copyright: ' + text('copyright'), 'Project: ' + text('projectUrl')]
    for file in folder.rglob('*'):
        if file.is_file() and (file.name.lower().startswith(('license', 'licence', 'thirdpartynotice', 'ofl.'))):
            dest = licenses / name.replace('/', '-') / file.relative_to(folder)
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(file, dest)
    lines.append('')
(out / 'THIRD-PARTY-PACKAGES.txt').write_text('\n'.join(lines))
