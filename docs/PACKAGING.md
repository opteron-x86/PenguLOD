# Linux packages and compatibility

PenguLOD ships a portable `linux-x64.tar.gz` and an AppImage containing the same desktop,
CLI, .NET runtime, native grass backend and optional Vulkan texture helper. Official builds
use Ubuntu 24.04. The target is x64 glibc Linux with glibc 2.39 or newer; Alpine/musl and
older glibc systems need a separate source build and are not covered by these binaries.

The compatibility workflow exercises the portable and AppImage packages on Ubuntu 24.04,
Debian 13 and Fedora 43: CLI/version startup, the bundled grass backend, optional compute
probing and desktop startup. These checks do not establish full modlist or GPU-driver
compatibility. Use the [in-game checks](DIAGNOSTICS.md#repeatable-checks) for generated output.

## Portable installation

Extract into a new version directory. Run `linux-x64/run-pengulod.sh`; optionally run
`linux-x64/install-desktop.sh` after moving it to its final location. Keep earlier versions
for rollback. Presets and caches live under the user's XDG configuration/cache directories.
Updating the package does not move a mod output folder or change its profile priority.

## AppImage

```sh
chmod +x PenguLOD-*-x86_64.AppImage
./PenguLOD-0.2.0-alpha.3-x86_64.AppImage
./PenguLOD-0.2.0-alpha.3-x86_64.AppImage --cli --help
```

Where mounting is unavailable, use `--appimage-extract-and-run` or extract with
`--appimage-extract` and launch `squashfs-root/AppRun`. AppImage does not backport glibc or
replace the host graphics/Vulkan driver.

## Verify downloads

Download `SHA256SUMS` alongside the release archives. In that directory, run:

```sh
sha256sum --ignore-missing -c SHA256SUMS
```

Each downloaded archive should report `OK`. Releases include PenguLOD source, patched
backend source and dependency license notices. See [dependencies](PROVENANCE.md).

Portable archives and AppImage are the supported distribution formats. No Flatpak or
Windows package is published. Build and release procedures are in the source repository's
[contributor guide](https://github.com/opteron-x86/PenguLOD/blob/main/CONTRIBUTING.md).
