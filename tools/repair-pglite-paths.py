#!/usr/bin/env python3
"""Quarantine AnvilLOD's invalid drive-name placeholder folders; preview unless --apply is given."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import tempfile


def stub_bytes(normal):
    data = bytearray(192)
    data[:4] = b"DDS "
    for offset, value in {4: 124, 8: 0x100F, 12: 4, 16: 4, 20: 16, 28: 1,
                          76: 32, 80: 0x41, 88: 32, 92: 0xFF0000, 96: 0xFF00,
                          100: 0xFF, 104: 0xFF000000, 108: 0x1000}.items():
        struct.pack_into("<I", data, offset, value)
    data[128:] = bytes((255, 128, 128, 255) if normal else (95, 105, 110, 255)) * 16
    return bytes(data)


def inventory(folder):
    hashes = {}
    for base, dirs, files in os.walk(folder, followlinks=False):
        for name in dirs + files:
            entry = Path(base) / name
            if entry.is_symlink():
                raise ValueError(f"Refusing a folder containing a symlink: {entry}")
        for name in files:
            entry = Path(base) / name
            if not entry.is_file() or entry.stat().st_size != 192:
                raise ValueError(f"Not a known AnvilLOD placeholder; leaving it alone: {entry}")
            content = entry.read_bytes()
            if entry.suffix.lower() != ".dds" or content != stub_bytes(name.lower().endswith("_n.dds")):
                raise ValueError(f"Not a known AnvilLOD placeholder; leaving it alone: {entry}")
            hashes[str(entry.relative_to(folder))] = hashlib.sha256(content).hexdigest()
    return hashes


def plan(roots):
    entries = []
    seen = set()
    for supplied in roots:
        root = supplied.resolve(strict=True)
        if root in seen:
            continue
        seen.add(root)
        names = {p.name.casefold() for p in root.iterdir()}
        if "anvillod.manifest.json" not in names:
            raise ValueError(f"Not an AnvilLOD output folder (no manifest): {root}")
        if names & {"data", "mods", "profiles", "modorganizer.ini", "skyrimse.exe", "skyrim.esm"}:
            raise ValueError(f"Refusing a game or mod manager root: {root}")
        for textures in root.iterdir():
            if textures.name.casefold() != "textures":
                continue
            if textures.is_symlink() or not textures.is_dir():
                raise ValueError(f"Expected a real textures directory: {textures}")
            for folder in textures.iterdir():
                if not re.fullmatch(r"[a-zA-Z]:", folder.name):
                    continue
                if folder.is_symlink() or not folder.is_dir():
                    raise ValueError(f"Expected a real drive-name directory: {folder}")
                entries.append((root, folder, inventory(folder)))
    return entries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("outputs", type=Path, nargs="+", help="AnvilLOD output or installed output mod folders")
    parser.add_argument("--apply", action="store_true", help="back up and remove the verified placeholder folders")
    parser.add_argument("--backup-dir", type=Path,
                        default=Path(__file__).resolve().parents[1] / ".artifacts" / "pglite-path-backups")
    args = parser.parse_args()
    entries = plan(args.outputs)  # Validate every target before modifying any of them.
    for _, folder, hashes in entries:
        print(f"{'Quarantine' if args.apply else 'Would quarantine'}: {folder} ({len(hashes)} placeholders)")
    if not entries:
        print("No malformed drive-name placeholder folders remain.")
        return
    if not args.apply:
        print("Preview only. Add --apply to back up these folders outside the mod and remove them.")
        return

    backup_parent = args.backup_dir.resolve()
    for root, _, _ in entries:
        if backup_parent == root or root in backup_parent.parents:
            raise ValueError("Backups must be outside every repaired output/mod folder.")
    backup_parent.mkdir(parents=True, exist_ok=True)
    backup = Path(tempfile.mkdtemp(prefix="repair-", dir=backup_parent))
    receipt = []
    # Finish and verify all backups before removing any source folder.
    for index, (root, folder, hashes) in enumerate(entries):
        target = backup / str(index) / folder.relative_to(root)
        shutil.copytree(folder, target)
        if inventory(target) != hashes or inventory(folder) != hashes:
            raise ValueError(f"Contents changed or backup verification failed: {folder}")
        receipt.append({"original": str(folder), "backup": str(target), "sha256": hashes})
    (backup / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    for _, folder, hashes in entries:
        if inventory(folder) != hashes:
            raise ValueError(f"Contents changed; leaving them alone: {folder}")
        shutil.rmtree(folder)
    print(f"Backups and original paths: {backup / 'receipt.json'}")
    print("Restart Skyrim through Fluorine and check PGLite.log. Regenerate with the fixed AnvilLOD build")
    print("using clean output, then replace the old output mod to correct its baked texture references too.")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError) as error:
        raise SystemExit(f"Repair stopped: {error}")
