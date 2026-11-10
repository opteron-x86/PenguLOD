# Dependencies, credits and compatibility

PenguLOD is based on [AnvilLOD by CageTV](https://github.com/CageTV/AnvilLOD) and retains
its copyright notices and GPL-3.0-only license. The application version is recorded in
`VERSION` for both the generator and PenguLOD runtime. `UPSTREAM_VERSION` records the
AnvilLOD revision from which the project was derived; it does not version PenguLOD releases.

## Generator dependencies

| Component | Use | Distribution |
| --- | --- | --- |
| .NET and Avalonia | Managed runtime, desktop UI and rendering | Included in Linux and Windows packages |
| Mutagen and texture libraries | Plugin/archive access and texture processing | Included; versions and notices accompany each package |
| [FasterNGIO](https://github.com/panthuncia/FasterNGIO) | Offline CPU grass generation | Included as a separate native executable, with corresponding patched source and licenses |
| Vulkan texture helper | Optional BC7 encoding | Included; requires a host Vulkan driver |
| Game/mod assets and DynDOLOD resources | Source meshes, textures and generation recipes | Supplied by the user |

FasterNGIO is pinned to `5a1bff6ad3e44b5b9c4b5acdd0663ae20860ca40`. Checksums,
dependency pins and maintained patches are under `tools/fasterngio` in the source
checkout. Both packages include these changes and dependency submodules in
`backends/fasterngio/FasterNGIO-source.tar.gz`.

Windows native dependencies use the vcpkg baseline pinned in `tools/windows/vcpkg.json`
and the `x64-windows-static` triplet. Exact installed versions and license notices ship
with the backend. The Windows headless build embeds the upstream UTF-8 application
manifest, preserving non-ASCII paths without changing system locale settings.

`THIRD-PARTY-PACKAGES.txt`, published `.deps.json` files and packaged license notices
identify managed dependencies and versions. Native runtime notices accompany the backend.
Generated output can contain assets belonging to installed mods; those assets are not
licensed by this repository. Game assets and generated mod output are not distributed.

## Game runtime

Dynamic LOD and runtime quality controls require the separate PenguLOD SKSE package
matching the Skyrim executable. The DLL runs inside Skyrim, including under Proton/Wine,
and is installed separately from the Linux or Windows generator.

PenguLOD's runtime is derived from AnvilLOD 0.2.70. It has its own product name, data paths
and release version. It reads older AnvilLOD runtime data when no PenguLOD output is present;
the older AnvilLOD DLL cannot read new PenguLOD output. Do not enable both DLLs together.
See [upgrading safely](UPGRADING.md).

CommonLibSSE-NG dependencies are pinned in `skse/vcpkg-configuration.json` and
`skse/cmake`. The runtime package includes each build's dependency notices and CommonLib
source, plus the matching PenguLOD source and build instructions.

Game formats and third-party resource paths remain unchanged. In particular, `Data/DynDOLOD`
recipes and model packs are compatibility inputs, not PenguLOD branding or an executable dependency.

## AppImage components

Packaging uses [appimagetool 1.9.1](https://github.com/AppImage/appimagetool/releases/tag/1.9.1),
verified with SHA-256 `ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0`.
Its embedded [type2 runtime at caf24f9](https://github.com/AppImage/type2-runtime/tree/caf24f9)
is reused. Runtime license notices accompany binary and source packages. Host graphics
and Vulkan drivers are supplied by the Linux distribution.
