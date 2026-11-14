#!/usr/bin/env python3
"""Collect the OpenVR license notice from the CommonLib source a runtime build compiled.

The runtime package carries license notices only. Source is published in the repository:
the plugin at the release tag, CommonLib through the pins and patches under skse/cmake.
"""
import argparse
import pathlib
import shutil


def collect_licenses(source_root: pathlib.Path, destination: pathlib.Path):
    # vcpkg stores OpenVR beside CommonLib as well as inside its extern/openvr tree.
    # CMakeLists.txt alone therefore cannot identify the library we compiled.
    candidates = [p for p in source_root.iterdir() if (p / 'include/RE/Skyrim.h').is_file()]
    if len(candidates) != 1:
        raise ValueError(f'Expected one CommonLib source tree, found {len(candidates)} in {source_root}')
    openvr = candidates[0] / 'extern/openvr'
    if not (openvr / 'headers/openvr.h').is_file():
        return
    licenses = sorted(p for p in openvr.iterdir() if p.is_file() and p.name.lower().startswith(('license', 'copying')))
    if not licenses:
        raise ValueError('OpenVR source is present without its license notice.')
    (destination / 'licenses').mkdir(parents=True, exist_ok=True)
    shutil.copyfile(licenses[0], destination / 'licenses/OpenVR.txt')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_root', type=pathlib.Path)
    parser.add_argument('destination', type=pathlib.Path)
    args = parser.parse_args()
    collect_licenses(args.source_root, args.destination)
