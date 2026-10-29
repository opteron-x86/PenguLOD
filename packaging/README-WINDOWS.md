# PenguLOD for Windows

Extract the entire ZIP into a new folder, then launch **PenguLOD.Desktop.exe**.
For the command line, run `PenguLOD.exe --help` from PowerShell or a terminal.
Keep the accompanying files and `backends` directory together.

This x64 package includes the .NET runtime and native grass backend. It does not
require a separate .NET SDK, Python, Wine, or DynDOLOD executable. Optional Vulkan
texture acceleration uses the graphics driver's Vulkan support; CPU generation
remains available when Vulkan is unavailable.

Choose your game, mod-manager instance and profile in the application. Native
Windows paths work directly. Each generation stage needs its own output folder.
See [the user guide](docs/USER-GUIDE.md) for configuration and installation of outputs.

Settings are stored in `%APPDATA%\PenguLOD`, and caches in `%LOCALAPPDATA%\PenguLOD`.
Use `--settings <file>` for a separate settings file. Extract upgrades into a new
folder; do not overwrite a running copy. Generation outputs remain where you selected.

Windows 10 22H2 and Windows 11 x64 are the intended desktop targets. Automated
tests use Windows Server runners; full modlist and physical GPU testing is still
needed. The optional in-game AnvilLOD SKSE plugin is a separate installation.

Checksums, platform notes and troubleshooting: [packages](docs/PACKAGING.md),
[troubleshooting](docs/TROUBLESHOOTING.md), [dependency notices](docs/PROVENANCE.md).
