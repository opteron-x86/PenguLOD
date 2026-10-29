"""Fetch checksum-pinned native sources and apply the maintained platform patches."""
import argparse
import hashlib
import pathlib
import shutil
import subprocess
import tarfile
import urllib.request

root = pathlib.Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--platform', choices=['linux-x64', 'win-x64'], default='linux-x64')
args = parser.parse_args()
destination = root / '.tools' / ('FasterNGIO-win' if args.platform == 'win-x64' else 'FasterNGIO')
sources = [
    ('fasterngio', 'panthuncia/FasterNGIO', '5a1bff6ad3e44b5b9c4b5acdd0663ae20860ca40', ''),
    ('BasicTelemetry', 'panthuncia/BasicTelemetry', '3bd82d9884cbc693a2cf98a566882e705f801425', 'external/BasicTelemetry'),
    ('ORGModuleServices', 'panthuncia/ORGModuleServices', '22993f32e418737fa823fcec5a4f6f3232bb0e30', 'external/ORGModuleServices'),
    ('tesworld', 'panthuncia/TESWorld', '763aee7622b336e05bf33893a372e3382b2431a4', 'external/tesworld'),
    ('nifly', 'ousnius/nifly', 'cca0a770094bb962fb28ea1fec5ea903e68fda8e', 'external/nifly'),
]
checksums = dict(line.split()[::-1] for line in (root / 'tools/fasterngio/sources.sha256').read_text().splitlines())
for name, repository, revision, subdir in sources:
    archive = root / '.tools' / (name + '-source.tar.gz')
    archive.parent.mkdir(parents=True, exist_ok=True)
    if not archive.exists():
        temporary = archive.with_suffix('.tmp')
        print('Downloading ' + repository, flush=True)
        urllib.request.urlretrieve(f'https://codeload.github.com/{repository}/tar.gz/{revision}', temporary)
        temporary.replace(archive)
    with archive.open('rb') as stream:
        if hashlib.file_digest(stream, 'sha256').hexdigest() != checksums[archive.name]:
            raise SystemExit('Source checksum mismatch: ' + str(archive))
    target = destination / subdir
    marker = target / '.anvillod-revision'
    if not marker.exists():
        target.mkdir(parents=True, exist_ok=True)
        with tarfile.open(archive) as tar:
            for member in tar.getmembers():
                member.name = '/'.join(member.name.split('/')[1:])
                if member.name:
                    tar.extract(member, target, filter='data')
        marker.write_text(revision + '\n', encoding='utf-8')
    if marker.read_text().strip() != revision:
        raise SystemExit('Unexpected source revision: ' + str(target))

patch = shutil.which('patch')
if not patch:
    import os
    candidate = pathlib.Path(os.environ.get('ProgramFiles', 'C:/Program Files')) / 'Git/usr/bin/patch.exe'
    if candidate.is_file():
        patch = str(candidate)
if not patch:
    raise SystemExit('Install patch (included with Git for Windows).')
patches = ['headless', 'tesworld-pin', 'scheduler-progress', 'finite-blades']
if args.platform == 'win-x64':
    patches.append('windows-manifest')
for name in patches:
    # Normalize checkout line endings so hashes and patch input match on both hosts.
    content = (root / 'tools/fasterngio' / (name + '.patch')).read_bytes().replace(b'\r\n', b'\n')
    checksum = hashlib.sha256(content).hexdigest()
    marker = destination / ('.anvillod-' + name)
    if not marker.exists():
        subprocess.run([patch, '--batch', '-p1'], cwd=destination, input=content, check=True)
        marker.write_text(checksum + '\n', encoding='utf-8')
    previous = marker.read_text().strip()
    if previous != checksum and not (not previous and name in {'headless', 'tesworld-pin'}):
        raise SystemExit(f'{name} changed; remove {destination} and rebuild the pinned sources.')
