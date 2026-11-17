# Changelog

Versions follow SemVer. Alpha and beta versions are pre-releases.

## [Unreleased]

- Fixed a DynDOLOD preset used as the custom rule file giving every object its most detailed LOD mesh at LOD 32. Its LOD 32 rules are used only when the preset has Level32 ticked.
- Fixed buildings and other statics without object bounds disappearing in loaded cells while `Occlusion.esp` is active. `Occlusion.esp` gives them their model's bounds. Regenerate it.
- Changed `Occlusion.esp` to keep each cell's own max-height data. Cells without any get the bare landscape.
- Fixed every stage stopping with "Invalid stitching dimensions or colour settings" on a texture rule file it cannot read, such as DynDOLOD's Majestic Mountains rules. The rule is skipped with a warning.
- Fixed object LOD failing with "Merged LOD shape has conflicting full textures" or "Alternate texture is ambiguous". The shape keeps its LOD model's texture and a warning names it.
- Fixed a finished build being lost on Windows when the move into place is refused. The move is tried again for two minutes, and a build that still cannot be published is kept.
- Changed the error for a mod chosen as a resource folder to say which folder to choose: the DynDOLOD program folder, not the DynDOLOD Resources mod.
- Changed the resource pack guide to say what the primary resource folder is for.
- Fixed rebuilt recipe atlases being too bright with **PBR object LOD** on. A tile taken from a PBR albedo is converted like other PBR LOD textures. Regenerate object LOD.

## [0.6.0-beta.5] - 2026-11-16

- Changed **Pack object texture atlases** to cut surfaces at texture tile edges, so surfaces that a texture repeats over are packed too. This adds triangles to those surfaces.
- Changed the changelog to one line per change.

## [0.6.0-beta.4] - 2026-11-15

- Fixed seasonal object LOD stopping with "Unresolved seasonal alternate texture". The object keeps its model's texture and a warning names it.
- Changed **Pack object texture atlases** to pack surfaces mapped over several texture tiles, on pages of up to 256 materials. Still off by default.

## [0.6.0-beta.3] - 2026-11-15

Regenerate textures and object LOD, including `Occlusion.esp`.

- Fixed `Occlusion.esp` hiding distant objects and trees that are in view.
- Fixed `Occlusion.esp` overriding the persistent cell of each worldspace.
- Fixed blocky grass billboards. Added **Grass world units per pixel** (default 2).
- Changed **World units per pixel** to **Tree world units per pixel**, default 6 (was 12). Saved values are kept.
- Fixed dark, blurry hybrid tree trunk textures. Trees and trunks are lit the way TexGen lights them.
- Added **Trunk world units per pixel** (default 4) and ambient and direct light settings for trees, grass and trunks.
- Removed **Ambient light** and **Directional light**.
- Fixed grass LOD standing in rows of dots.
- Changed the grass LOD brightness defaults to tip 0.3 and root 0.18 (were 0.85 and 0.5). Saved values are kept.
- Changed tree cards from LOD8 on to use one shared atlas.
- Added **Seasonal LOD: winter**, **spring**, **summer** and **autumn** to choose which seasons get seasonal LOD (`--only-seasons`).
- Fixed output folders that differ only in case on Linux. **Verify output set** reports them.
- Fixed "This folder is not an empty or recognized PenguLOD output" when generating object LOD again into the same folder.
- Fixed stand-ins for missing textures not being written while a previous PenguLOD output is installed.
- Added the `compare-reference` command, which measures an output against DynDOLOD, TexGen or xLODGen output.

## [0.6.0-beta.2] - 2026-11-14

- Fixed the texture stage stopping when one model or recipe cannot be generated. Failed items are logged, listed in the report and left out.
- Changed an output folder that cannot be written to stop the run.
- Fixed grass meshes failing with "Expected a safe Data-relative game path".
- Fixed skinned models being rejected for skin data the game does not read.
- Fixed models failing on a data block that has no owning shape.
- Fixed LOD generation stopping on a grass type that cannot be rendered. It is left out with a warning.
- Fixed LOD generation stopping on a seasonal swap to a tree without a billboard. It is a warning unless **Strict seasonal swaps** is on.
- Fixed the "Could not read Skyrim.ccc" warning on Skyrim VR and on installs without that file.
- Added Skyrim 1.6.1179 (GOG) support to the game plugin.
- Removed third-party debug symbols from the Windows package.
- Changed the documentation to be shorter and organised by task.
- Added install instructions to the SKSE package README.

## [0.6.0-beta.1] - 2026-11-13

Regenerate terrain, textures, tree LOD and object LOD.

- Changed terrain generation to use every worker thread.
- Added **Fill uncovered land at the default height** (on by default). Land without landscape records or terrain LOD no longer stops terrain generation.
- Added **Use the default texture for missing landscape textures** (on by default).
- Changed missing land and landscape textures to be reported for every selected worldspace before the first tile and in the preflight.
- Fixed slow LOD16 and LOD32 terrain tiles that cover many landscape textures.
- Fixed the heightmap export re-reading terrain meshes for every row.
- Fixed "DDS pixel format (16-bit, flags 0x40) is not supported". 16-bit DDS textures are read.
- Added a preflight warning when the game is running or `Data` is a mod manager's virtual file system.
- Changed a read that fails because its storage went away to stop with an explanation, not a stack trace.
- Fixed mirrored normal-map lighting on shapes without stored tangents, tree and trunk cards, water stand-ins and billboards.
- Fixed simplified parts losing their shape name, tier and TexGen scale.
- Fixed the order Seasons of Skyrim INIs are applied in. A line for a record that is not in the load order is skipped and reported.
- Added **Strict seasonal swaps** (`--seasons-strict`).
- Fixed BC7 blocks encoding as one flat colour, and opaque blocks decoding with alpha below 255.
- Fixed block-compressed textures whose top level is not a multiple of four.
- Fixed every stage rebuilding after playing the game or running an animation tool.
- Added **Choose…** beside every worldspace field, and the `worldspaces` command.

## [0.6.0-alpha.5] - 2026-11-12

Regenerate if you used underside step 4 or have textures in legacy X8 format.

- Fixed output folders becoming unusable after tree LOD was generated without LOD4 selected.
- Added support for output folders below a symbolic link.
- Fixed the CLI rejecting options listed in `--help`.
- Changed the CLI to refuse generation options next to `--settings` or `--request`, and malformed or out-of-range numbers.
- Removed `--tree-3d-by-name` and `--tree-3d-own-lod`.
- Changed `scan` to write its report to the current folder when `--report` is not given.
- Fixed the stage page buttons ignoring the setup check, texture compute and scope expansion settings.
- Changed whole-number options to step by one.
- Fixed the seasonal checkboxes on Terrain and Object LOD getting out of sync.
- Changed the MO2 game-folder override to a separate setting. **Detect Steam installation** no longer replaces it.
- Fixed a malformed NIF, DDS or grass cache stopping texture repair, tree LOD, the mesh maker or conversion.
- Fixed mod folders that differ only in case hiding one another or stopping the scan.
- Fixed duplicate tree LOD blocks with different case on Linux.
- Fixed a previous `Occlusion.esp` being used as the source of cell data.
- Fixed large references not treating `.esm` and `.esl` files without the ESM flag as masters.
- Fixed reading of 24-bit and legacy X8 textures, worldspaces with unusual EditorIDs and cells with a placeholder water height.
- Removed underside step 4. Presets saved with it use step 8.
- Fixed changes to a file inside an archive going undetected when its size is unchanged.
- Changed grass, textures and terrain to publish the way object LOD does, receipt and files in one step.
- Changed publication on Linux to swap the old and new folders atomically.
- Fixed edits to `meta.ini` and `SKSE/Plugins/PenguLOD.ini` failing verification, resume or publication.
- Fixed separate jobs on one LOD folder treating each other's blocks as damaged.
- Fixed a stage that finished publishing not being recorded as complete when cancelled just afterwards.
- Added **Previous outputs to keep** (`--keep-previous`, default 1) and the `clean` command.
- Changed unfinished build folders, outdated terrain tile caches and old desktop run folders to be removed automatically.
- Changed relative paths in a CLI preset to resolve against the preset's folder.
- Fixed texture generation creating an empty output folder when nothing is published.
- Changed the game plugin to load at most `iMaxShowPerUpdate` (default 200) dynamic LOD models per update, nearest first.
- Changed the game plugin at `iMaxShown` to replace the farthest object drawn with a nearer one.
- Fixed native grid objects in `PenguLOD.esp` falling out of step when there is no other dynamic LOD.
- Fixed saving one menu page reloading `PenguLOD.ini` over values changed on another.
- Fixed adaptive quality stepping down to the lowest preset under a frame cap, and losing its preset on a settings reload.
- Fixed the quality layer checkboxes not appearing after starting from the main menu or an interior.
- Fixed water opacity with water animation turned off.
- Fixed the game plugin's settings parser misreading `no`, `off`, inline comments and locale-dependent numbers.
- Fixed a crash on a damaged data file.
- Fixed DynDOLOD.dll counting as present when it is not loaded.
- Changed the SKSE archive to hold only the installer, the three DLLs and licence notices.
- Removed the source archives from the SKSE archive.

## [0.6.0-alpha.4] - 2026-11-10

- Fixed **Review output scope** rejecting older AnvilLOD output before inspecting it.
- Changed old output to be reported by stage, folder and identifying files.
- Added the read-only scope review to CLI presets.
- Fixed scope reports being saved inside the output folders being inspected.

## [0.6.0-alpha.3] - 2026-11-10

- Changed app labels, diagnostics, setup instructions and package descriptions to name Mod Organizer 2 and Community Shaders without naming forks.
- Removed the additional attribution line from the About panel.

## [0.6.0-alpha.2] - 2026-11-10

- Changed the Overview and Pipeline screens into one **Generation Overview**.
- Changed shared inputs to **Game & resources**, object, tree and grass LOD options to **Object LOD**, and scan coverage to **Scan results**.
- Added a generate button to each stage page.
- Changed setting explanations to tooltips. Object LOD groups can be expanded or searched.
- Removed slogans and implementation paragraphs from the app.
- Changed the walkthrough and settings reference for the new layout.

## [0.6.0-alpha.1] - 2026-11-09

Use a fresh output set and the matching game plugin for the first switch from AnvilLOD.

- Changed all product names to PenguLOD: projects, generated plugins, runtime data, asset folders, manifests, logs and in-game menus.
- Added the PenguLOD SKSE package, with a build for each supported Skyrim runtime.
- Added upgrade checks that stop generation or publication over an AnvilLOD-named output set and detect the old game plugin.
- Added an upgrade walkthrough.

## [0.5.0-alpha.1] - 2026-11-09

- Added saved-output cost reports and comparisons to the desktop and CLI.
- Added output set management: recorded stages and file ownership, **Verify output set**, and a combined package update with preview and backup.
- Added a review of existing generation scope, and an option to expand a run to keep its worlds and levels.
- Fixed inconsistent terrain and object worldspace wildcards.
- Fixed **PBR waterfalls** depending on PBR object LOD.
- Changed unsupported imported rule actions to be labelled read-only.
- Added the settings reference, walkthrough and output-management guide to the packages.
- Added the large-reference fix and the optional material and texture optimizations from 0.4.0-alpha.14.

## [0.4.0-alpha.14] - 2026-10-30

Regenerate object LOD to use the new options.

- Added consolidation of shader-compatible object materials, including auxiliary texture pages, cutout thresholds and shifted wrapping UVs.
- Added optional object LOD texture sizing and compression. The standalone texture limit defaults to 1024 pixels.
- Changed an unsupported texture input to affect only its own material. The fallback reason is reported.
- Added material consolidation and texture byte counts to reports.
- Fixed large-reference lists not being rebuilt while previous generated output is enabled.

## [0.4.0-alpha.13] - 2026-10-30

- Fixed previous output feeding back into object LOD. Regenerate object LOD if an earlier automatic-object run included it.
- Changed static architecture to be kept when its glow, smoke, animated effects or shaderless layers cannot be drawn. Each omission is reported.
- Added foliage reduction for installed trees below their triangle budgets. The geometry target defaults to 65%.
- Changed staging to copy only retained object blocks and to record hashes while writing.

## [0.4.0-alpha.12] - 2026-10-30

- Added discovery of rules, recipes, mesh lookups, world exclusions, child-world settings and render models in enabled mods and resource-only folders.
- Fixed scan-only requests starting occlusion generation or terrain preparation.
- Added a priority order for multiple external packs. Reports name the selected files.
- Added additional resource folders to the desktop, presets, JSON and CLI.

## [0.4.0-alpha.11] - 2026-10-30

- Added optional native hybrid tree authoring: front and side trunk cards with normal textures, combined with reduced 3D crowns.
- Added hybrid authoring and trunk resolution settings.
- Changed tree coverage reports to distinguish native hybrids, geometry reduction and billboard fallback.

## [0.4.0-alpha.10] - 2026-10-30

- Added optional automatic object LOD for missing levels that active rules request, with per-level budgets, minimum size and reduction settings.
- Added object LOD coverage and costs to Overview, scan reports and `PenguLOD.objects.json`.
- Fixed CLI scans ignoring JSON LOD requests and preset worker and cache settings.

## [0.4.0-alpha.9] - 2026-10-30

Regenerate textures, then LOD.

- Added separate front and side diffuse and normal maps for tree and grass billboards.
- Changed tree texture resolution to follow the largest winning reference scale.
- Added detection and splitting of packed complex-grass colour and normals.
- Added settings for HD views, scale discovery, grass layout and the detection threshold.

## [0.4.0-alpha.8] - 2026-10-30

- Added Base Object Swapper removals to LOD scans. References and base objects swapped to an XMarker are left out of LOD.
- Changed `*_SWAP.ini` files to be processed in BOS filename order.
- Added removed references and unsupported entries to scan reports.

## [0.4.0-alpha.7] - 2026-10-29

- Changed a missing recipe texture replacement to keep the material's supplied LOD textures and UVs.
- Added fallback warnings that name the model, shape and missing file.

## [0.4.0-alpha.6] - 2026-10-29

- Fixed full-model texture matching for atlases made from one repeated source.
- Fixed cyclic normal-map recipe failures on shapes that already use an atlas.
- Fixed duplicate full-model shape names not being resolved by a matching LOD material.

## [0.4.0-alpha.5] - 2026-10-29

- Fixed grass atlas generation failing on unused grass records with missing textures.
- Changed grass atlas preparation to read cache headers without decoding every blade.
- Changed grass atlas preparation to run before occlusion generation.

## [0.4.0-alpha.4] - 2026-10-29

- Fixed grass meshes that use Bethesda's `\bNOR` normal-map marker.
- Changed automatic tree billboard discovery to report and skip trees with unsupported effect materials.

## [0.4.0-alpha.3] - 2026-10-29

Regenerate LOD.

- Added reading of triangle strips, static skin poses, SSE skin partition buffers and dynamic vertex positions.
- Fixed duplicate static and animated tree geometry.
- Added static effect textures, tint, UV transforms and lighting influence to texture rendering.
- Changed an incomplete visible model to block partial LOD or texture output.
- Changed native full-tree reduction to use plain diffuse and normal LOD materials.

## [0.4.0-alpha.2] - 2026-10-29

- Added a Windows x64 package with the desktop app, CLI, grass backend and optional Vulkan texture helper.
- Added Linux and Windows tests and package startup checks to releases.
- Changed Windows grass staging to hard links with a copy fallback.
- Added Steam discovery for the default Windows installation.
- Fixed two jobs targeting one folder through differently capitalised paths on Windows.

## [0.4.0-alpha.1] - 2026-10-29

- Added native TexGen `x` recipes, with copy and conversion dependencies.
- Changed object LOD to follow recipe UV layouts.
- Changed record and seasonal texture overrides to rebuild separate atlas variants.
- Added normal-only overrides on full textures and stitched atlases.
- Added validated recipes to texture manifests and recipe content to LOD fingerprints.

## [0.3.0-alpha.1] - 2026-10-28

- Added record-specific texture variants for native 3D trees and automatic billboards.
- Added optional foliage reduction within alpha-coverage limits (1% near, 3% far by default).
- Changed terrain normals to combine source normal detail with slope normals.
- Added PBR albedo and ambient-occlusion calibration for terrain, with opt-in texture twin lookup.
- Added seasonal diagnostics for resolved swaps and grass-cache gaps.
- Added resume for interrupted terrain jobs.
- Added terrain phase timings.
- Changed pipeline caches to be kept outside game files and input mods.

## [0.2.0-alpha.3] - 2026-10-28

- Changed the documentation into setup, grass, textures and troubleshooting guides.
- Removed profile-specific paths, local benchmark results and development notes from the documentation.
- Changed binary packages to include only user-facing guides.

## [0.2.0-alpha.2] - 2026-10-27

- Fixed terrain generation failing on unpainted land. The default texture is `landscape/dirt02.dds`, or `sDefaultLandDiffuseTexture` from the INI.
- Changed the base texture to be skipped where alpha layers cover it fully.
- Changed a missing painted texture to name the world, cell and quadrant.

## [0.2.0-alpha.1] - 2026-10-27

- Added portable archive and AppImage packages for Ubuntu 24.04, Debian 13 and Fedora 43.
- Added shared desktop and CLI presets, per-stage CLI commands, request export and saved-job inspection.
- Added optional Vulkan BC7 encoding with a CPU fallback.
- Added object texture atlases.
- Added seasonal terrain, tree LOD and 3D tree replacements from Seasons of Skyrim swaps.
- Changed billboard, trunk and object rendering to bake source normals, UV transforms and clamp modes, with PBR and light calibration.
- Fixed stale material grouping keys after texture replacement.
- Added the pipeline preflight and a Diagnostics page.
- Added the shared desktop and CLI pipeline with checkpoints, verified output reuse and cancellation recovery.
- Added isolated LOD publication with previous-output backups and job locks.
- Added the terrain and water stage.
- Added per-level tree budgets, far-model selection, trunk reduction and optional full-model fallback.

## [0.1.0-alpha.1] - 2026-10-27

Initial Linux release, based on CageTV/AnvilLOD 0.2.70.

- Added the desktop app and Linux CLI, with Mod Organizer 2 profile access.
- Added texture stitching, tree and grass billboards and hybrid tree trunk textures.
- Added bundled grass caching, and combined grass, textures and LOD jobs.
- Added incremental LOD generation, 3D tree model support, presets, previews, progress and cancellation.
- Added BC6H texture decoding and repair.
- Added a one-time import of AnvilLOD settings.
