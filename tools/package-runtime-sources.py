#!/usr/bin/env python3
"""Bundle the CommonLib source used by a runtime build, including its OpenVR submodule."""
import argparse
import pathlib
import shutil
import tarfile


def package_sources(source_root: pathlib.Path, destination: pathlib.Path):
    # vcpkg stores OpenVR beside CommonLib as well as inside its extern/openvr tree.
    # CMakeLists.txt alone therefore cannot identify the library we compiled.
    candidates = [p for p in source_root.iterdir() if (p / 'include/RE/Skyrim.h').is_file()]
    if len(candidates) != 1:
        raise ValueError(f'Expected one CommonLib source tree, found {len(candidates)} in {source_root}')
    commonlib = candidates[0]
    (destination / 'source').mkdir(parents=True, exist_ok=True)
    with tarfile.open(destination / 'source/CommonLibSSE-NG.tar.gz', 'w:gz') as archive:
        archive.add(commonlib, arcname='.')
    openvr = commonlib / 'extern/openvr'
    if (openvr / 'headers/openvr.h').is_file():
        licenses = sorted(p for p in openvr.iterdir() if p.is_file() and p.name.lower().startswith(('license', 'copying')))
        if not licenses:
            raise ValueError('OpenVR source is present without its license notice.')
        (destination / 'licenses').mkdir(exist_ok=True)
        shutil.copyfile(licenses[0], destination / 'licenses/OpenVR.txt')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_root', type=pathlib.Path)
    parser.add_argument('destination', type=pathlib.Path)
    args = parser.parse_args()
    package_sources(args.source_root, args.destination)
