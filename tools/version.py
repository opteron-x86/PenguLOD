#!/usr/bin/env python3
"""Print/set PenguLOD's SemVer or verify a release tag. Never changes the SKSE version."""
import argparse
import pathlib
import re

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('version', nargs='?', help='new version, e.g. 0.1.0-alpha.2')
parser.add_argument('--check-tag', help='require an exact vVERSION tag')
args = parser.parse_args()
file = pathlib.Path(__file__).resolve().parents[1] / 'VERSION'
version = args.version or file.read_text().strip()
number = r'(?:0|[1-9][0-9]*)'
identifier = rf'(?:{number}|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
pattern = rf'{number}\.{number}\.{number}(?:-{identifier}(?:\.{identifier})*)?'
if not re.fullmatch(pattern, version):
    parser.error('Use SemVer without a v prefix or build metadata, e.g. 0.1.0-alpha.2')
if args.check_tag and args.check_tag != 'v' + version:
    parser.error(f'Tag {args.check_tag!r} does not match VERSION ({version})')
if args.version:
    file.write_text(version + '\n')
print(version)
