"""Bundle runtime libraries and the corresponding upstream sources/build changes."""
import argparse
import json
import pathlib
import re
import shutil
import subprocess
import tarfile

# Exclude local tooling and assistant instructions from corresponding-source archives.
# Source code, build configuration, patches and license notices remain included.
excluded_dirs = {'.git', 'build', '.anvillod-headless', '.codex', '.claude', '.agents', '__pycache__'}
excluded_files = {'agents.md', 'agents.override.md', 'claude.md', 'claude.local.md', 'gemini.md'}


def distributable(path):
    return path.is_file() and path.name.casefold() not in excluded_files and not any(
        part in excluded_dirs for part in path.parts)


root = pathlib.Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--platform', choices=['linux-x64', 'win-x64'], default='linux-x64')
windows = parser.parse_args().platform == 'win-x64'
runtime = root / ('.tools/fasterngio-runtime-win-x64' if windows else '.tools/fasterngio-runtime')
source = root / ('.tools/FasterNGIO-win' if windows else '.tools/FasterNGIO')
libs = runtime / 'lib'
library_lines = [] if windows else subprocess.check_output(
    ['ldd', str(root / '.tools/fasterngio-build/bin/FasterNGIO')], text=True).splitlines()
if not (source / 'CMakeLists.txt').is_file():
    raise FileNotFoundError('Build FasterNGIO before packaging its runtime and source.')
if libs.exists():
    shutil.rmtree(libs)
if not windows:
    libs.mkdir(exist_ok=True)
for line in library_lines:
    match = re.search(r'(\S+) => (/\S+)', line)
    if not match:
        continue
    name, path = match.groups()
    # glibc and its loader belong to the host. Bundle the compiler/dependency runtime.
    if '/' in name or name in {'libc.so.6', 'libm.so.6', 'libpthread.so.0', 'libdl.so.2', 'librt.so.1'}:
        continue
    if pathlib.Path(path).resolve() != (libs / name).resolve():
        shutil.copy2(path, libs / name)
with tarfile.open(runtime / 'FasterNGIO-source.tar.gz', 'w:gz') as tar:
    for item in source.rglob('*'):
        if distributable(item):
            tar.add(item, 'FasterNGIO/' + item.relative_to(source).as_posix())
    for item in (root / 'tools/fasterngio').rglob('*'):
        if distributable(item):
            tar.add(item, 'anvillod-build/tools/fasterngio/' + item.relative_to(root / 'tools/fasterngio').as_posix())
    tar.add(root / 'tools/build-fasterngio.sh', 'anvillod-build/tools/build-fasterngio.sh')
    for name in ['tools/build-native-windows.ps1', 'tools/windows-env.ps1', 'tools/windows/vcpkg.json']:
        tar.add(root / name, 'anvillod-build/' + name)
(runtime / 'backend.json').write_text(json.dumps({
    'name': 'FasterNGIO', 'revision': '5a1bff6ad3e44b5b9c4b5acdd0663ae20860ca40',
    'gpu': False, 'source': 'https://github.com/panthuncia/FasterNGIO',
    'license': 'GPL-3.0', 'build': 'PenguLOD native headless CPU BVH',
    'patches': ['headless', 'tesworld-pin', 'scheduler-progress', 'finite-blades'] + (['windows-manifest'] if windows else []),
    'platform': 'win-x64' if windows else 'linux-x64',
}, indent=2) + '\n', encoding='utf-8')
licenses = runtime / 'licenses'
licenses.mkdir(exist_ok=True)
for name in ([] if windows else ['LZ4.txt', 'oneTBB.txt', 'FasterNGIO-fmt.txt', 'FasterNGIO-spdlog.txt',
             'FasterNGIO-zlib-ng.txt', 'GCC-runtime-exception.txt']):
    shutil.copy2(root / 'assets/licenses' / name, licenses / name)
shutil.copy2(root / 'LICENSE.txt', licenses / 'GPL-3.0.txt')
if windows:
    installed = root / '.tools/windows-deps'
    for notice in (installed / 'x64-windows-static/share').glob('*/copyright'):
        shutil.copy2(notice, licenses / (notice.parent.name + '.txt'))
    shutil.copy2(installed / 'vcpkg/status', runtime / 'dependency-versions.txt')
    shutil.copy2(root / 'tools/windows/vcpkg.json', runtime / 'vcpkg.json')
    (runtime / 'THIRD-PARTY.txt').write_text(
        'FasterNGIO: https://github.com/panthuncia/FasterNGIO\n'
        'Pinned source, submodules, platform patches and build scripts: FasterNGIO-source.tar.gz\n'
        'Dependencies are built with the static MSVC runtime. License notices are in licenses/.\n'
        'Exact native versions: dependency-versions.txt; reproducible vcpkg baseline: vcpkg.json.\n', encoding='utf-8')
    raise SystemExit(0)
versions = subprocess.check_output(['pkg-config', '--modversion', 'zlib', 'liblz4', 'tbb', 'fmt', 'spdlog'], text=True).splitlines()
(runtime / 'THIRD-PARTY.txt').write_text('FasterNGIO: https://github.com/panthuncia/FasterNGIO\n'
    'Pinned source, dependency submodules, and local headless/build patches: FasterNGIO-source.tar.gz\n'
    'Unmodified dependency runtime libraries are included in lib/. Licenses are in licenses/.\n'
    'Build-host dependencies (zlib, lz4, TBB, fmt, spdlog): ' + ', '.join(versions) + '\n'
    'Dependency sources: https://github.com/zlib-ng/zlib-ng ; https://github.com/lz4/lz4 ;\n'
    'https://github.com/uxlfoundation/oneTBB ; https://github.com/fmtlib/fmt ; https://github.com/gabime/spdlog\n'
    'GCC runtime: https://gcc.gnu.org/ — GPL-3.0 with GCC Runtime Library Exception.\n'
    'The backend requires a glibc at least as new as its build target. Rebuild on an older distro for a lower minimum.\n', encoding='utf-8')
