# PenguLOD for Linux

Run `./run-pengulod.sh` for the desktop or `./PenguLOD --help` for the CLI.
Keep the package folder together. The .NET runtime and native grass backend are bundled.
Use `./install-desktop.sh` after moving this directory to its permanent location if you want
a menu entry. Existing settings are imported once; generation outputs stay at their configured paths.

- [Setup and generation guide](docs/USER-GUIDE.md)
- [Grass caching](docs/GRASS.md), [textures](docs/TEXTURES.md) and [terrain](docs/TERRAIN.md)
- [Persistent pipeline](docs/PIPELINE.md) and [CLI commands](docs/CLI.md)
- [Diagnostics and in-game checks](docs/DIAGNOSTICS.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md) and [roadmap](docs/ROADMAP.md)
- [Package compatibility](docs/PACKAGING.md)
- [Dependencies and compatibility](docs/PROVENANCE.md), [license](LICENSE.txt) and [dependency inventory](THIRD-PARTY-PACKAGES.txt)

Official releases target x64 glibc Linux, built on Ubuntu 24.04. Game-side dynamic features
require the separately installed AnvilLOD SKSE plugin matching your Skyrim runtime.
PenguLOD's application version and that game-plugin version are independent.
