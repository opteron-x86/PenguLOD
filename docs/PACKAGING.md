# Packages and compatibility

Linux and Windows packages are built from the same version and source revision.
Both include the Avalonia desktop, CLI, .NET runtime, native CPU grass backend and
optional Vulkan texture helper. No separate .NET installation is required.

## Linux

PenguLOD ships a portable `linux-x64.tar.gz` and an AppImage containing the same desktop,
CLI, .NET runtime, native grass backend and optional Vulkan texture helper. Official builds
use Ubuntu 24.04. The target is x64 glibc Linux with glibc 2.39 or newer; Alpine/musl and
older glibc systems need a separate source build and are not covered by these binaries.

The compatibility workflow exercises the portable and AppImage packages on Ubuntu 24.04,
Debian 13 and Fedora 43: CLI/version startup, the bundled grass backend, optional compute
probing and desktop startup. These checks do not establish full modlist or GPU-driver
compatibility. Use the [in-game checks](DIAGNOSTICS.md#repeatable-checks) for generated output.

### Portable installation

Extract into a new version directory. Run `linux-x64/run-pengulod.sh`; optionally run
`linux-x64/install-desktop.sh` after moving it to its final location. Keep earlier versions
for rollback. Presets and caches live under the user's XDG configuration/cache directories.
Updating the package does not move a mod output folder or change its profile priority.

### AppImage

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

For a Windows ZIP, compare `(Get-FileHash .\PenguLOD-0.4.0-alpha.2-win-x64.zip -Algorithm SHA256).Hash`
in PowerShell with its line in `SHA256SUMS`.

## Windows

Extract the complete `PenguLOD-<version>-win-x64.zip` into a new directory and run
`win-x64/PenguLOD.Desktop.exe`. Use `PenguLOD.exe` for CLI commands. Keep the executables,
runtime files and `backends` directory together. Upgrades can be extracted side by side.

The intended desktop targets are Windows 10 22H2 and Windows 11 x64. CI compiles with
Visual Studio 2026 on Windows Server 2025 and checks the extracted package on Windows
Server 2022, including paths with spaces and non-ASCII characters. Engine, rendered UI,
grass process/recovery and native backend tests run on Windows. These checks do not
establish full modlist, MO2 virtual-filesystem or physical GPU compatibility.

The native grass backend uses the static MSVC runtime. Optional Vulkan encoding needs
the graphics driver's Vulkan support; generation falls back to CPU if it is unavailable.
Settings are under `%APPDATA%\PenguLOD`, caches under `%LOCALAPPDATA%\PenguLOD`, unless
overridden. The AnvilLOD game plugin remains a separate installation.

Portable archives, the Windows ZIP and AppImage are the supported distribution formats.
No Flatpak or ARM package is published. Build and release procedures are in the source repository's
[contributor guide](https://github.com/opteron-x86/PenguLOD/blob/main/CONTRIBUTING.md).
