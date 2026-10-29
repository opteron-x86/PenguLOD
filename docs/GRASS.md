# Grass caching

The **Grass cache** page runs the bundled FasterNGIO CPU backend offline. Generation
does not launch Skyrim or Wine. Loading a cache in game still requires the appropriate
NGIO/runtime configuration; PenguLOD does not change game-side grass or shader settings.

## Generate a cache

1. Select the game source and profile on **Overview**.
2. On **Grass cache**, choose a separate output mod folder, such as
   `~/PenguLOD-output/Grass`. The application creates `grass/` inside it.
3. Leave worldspaces at `all`, or enter editor IDs such as `Tamriel`. This selection
   is independent of Overview's LOD worldspace selection.
4. Choose placement and collision settings. **Smooth** is the backend default;
   **Vanilla** follows the engine's placement algorithm. Automatic collision rejection
   uses the winning NGIO configuration. The bundled backend supports CPU rejection.
5. Generate, review warnings, then install the completed folder as a separate mod.
   Disable the previous cache mod and replace caches instead of merging their files.

The backend reads winning plugins, meshes, profile/plugin INIs, `GrassControl.ini`,
`*_NGIO.ini` and Seasons configuration through a temporary Data view. Missing enabled
plugins or mod folders stop preparation. Source files remain unchanged.

Windows uses hard links for staging where possible, without requiring administrator
rights. If files are on another drive and symlinks are unavailable, affected inputs
are copied temporarily; preparation reports this and remains cancellable. Place grass
output on the same NTFS volume as most source mods to reduce temporary disk usage.
Read-only source files are copied so cleanup never changes their attributes.

**Advanced** offers a GrassControl.ini override, minimum grass size override (0 inherits
settings), writer threads, experimental render-geometry rejection and an external native
FasterNGIO executable. Worker limits follow Preferences. A compatible external GPU build
can use Vulkan ray tracing; the bundled build is CPU only. Grass caching supports SE/AE;
VR grass caching is not supported.

## Preview and reuse

For a small preview, choose one worldspace, enable **Small-area preview**, and select
cell coordinates and a radius. Radius 0 selects one cell; radius 1 selects a 3×3 square.
Use a separate preview output. Previews cannot replace a full cache or feed a full LOD run.

After a full successful run, **Use generated grass cache for LOD** lets generation read
the new cache directly, suppressing older installed grass files. Changed plugin order,
grass models, landscape, placement settings or generated files require regeneration.

Grass LOD packs billboards for types that have instances in the selected cache cells.
When seasons are enabled, matching seasonal cells contribute their grass types too.
Unused grass-library records do not require textures. Missing textures needed by a used
model still stop generation; atlas preparation reports these before the occlusion pass.

## Progress and validation

Logs report preparation, cells processed, files written and elapsed time. The backend
warns after two minutes without progress during generation or final writes; the warning
does not abort the job. Total duration depends on worldspaces, assets, workers and storage.

Generated CGID files are checked for format, finite coordinates and complete contents.
Unrepresentable placements are omitted with a warning identifying the cell. Missing
models and fallback block sizes are also reported. Format validation does not establish
visual placement accuracy; inspect the cache in game.

Only validated output is published, with `AnvilLOD.grass.json`, settings, hashes and the
backend log. A previous owned cache is retained in a sibling `.previous-…` folder.
Failed or cancelled runs preserve published output. **Cancel job** stops the backend
and removes its staging files. See [pipeline recovery](PIPELINE.md) for interrupted jobs.
