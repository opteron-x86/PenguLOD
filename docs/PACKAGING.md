# Linux packages and compatibility

PenguLOD ships a portable `linux-x64.tar.gz` and an AppImage containing the same desktop,
CLI, .NET runtime, native grass backend and optional Vulkan texture helper. Official builds
use Ubuntu 24.04. The target is x64 glibc Linux with glibc 2.39 or newer; Alpine/musl and
older glibc systems need a separate source build and are not covered by these binaries.

The compatibility workflow exercises the portable payload on Ubuntu 24.04, Debian 13 and
Fedora 43: CLI/version startup, the bundled grass backend, optional compute probing and
an Xvfb desktop startup. These are compatibility smoke tests, not full modlist or GPU-driver
validation. The generated outputs still need the in-game checks in `DIAGNOSTICS.md`.

## Portable installation

Extract into a new version directory. Run `linux-x64/run-pengulod.sh`; optionally run
`linux-x64/install-desktop.sh` after moving it to its final location. Keep earlier versions
for rollback. Presets and caches live under the user's XDG configuration/cache directories.
Updating the package does not move a mod output folder or change its profile priority.

## AppImage

```sh
chmod +x PenguLOD-*-x86_64.AppImage
./PenguLOD-0.2.0-alpha.1-x86_64.AppImage
./PenguLOD-0.2.0-alpha.1-x86_64.AppImage --cli --help
```

Where mounting is unavailable, use `--appimage-extract-and-run` or extract with
`--appimage-extract` and launch `squashfs-root/AppRun`. AppImage does not backport glibc or
replace the host graphics/Vulkan driver.

`bash tools/package-appimage.sh` packages an existing portable build. The AppImage tool
is pinned to version 1.9.1 and SHA-256 checked. Its embedded runtime is extracted from that
verified tool (runtime source commit `caf24f9`), avoiding an unpinned runtime download.
Runtime licensing notices are included. Package source, backend source and dependency
provenance accompany releases.

For local development, `PENGULOD_PACKAGE_DIR="$PWD/dist/candidate/linux-x64" bash tools/build.sh --reuse-backend`
builds a separate candidate. Release CI always rebuilds the pinned grass backend. Both
packaging scripts accept that package-directory override; it must stay under `dist/`.

## Flatpak evaluation

Flatpak can package Avalonia, but this workflow reads interconnected Steam libraries,
Fluorine/MO2 profiles, mod trees and Wine prefixes, often spread across multiple mounts.
The current file model cannot work through individual document grants alone. A Flatpak
would need broad host access or a redesigned directory-grant workflow, plus deliberate
external-process handling for the grass backend. See the upstream
[sandbox permissions](https://docs.flatpak.org/en/latest/sandbox-permissions.html).

For this release the distribution choice is portable archives and AppImage. No Flatpak is
published or claimed tested. This avoids presenting an unvalidated sandbox manifest as a
working package; a future Flatpak effort should start with profile discovery and scoped
directory grants on a real installation.
