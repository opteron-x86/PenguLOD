# Persistent generation

Generation Overview selects grass, textures, terrain/water and object LOD stages. Each stage needs its own output
folder. Run the same selection again to resume after cancellation, failure or an application
restart. A completed stage is reused only when its settings, input inventory, dependencies
and every recorded output file still match. Changing a stage invalidates its dependents.

Plugins and configuration files are content hashed. By default, large source assets use
size and filesystem timestamps; enable **Verify all source files** when sources
may have been replaced while preserving metadata. Published outputs are always content
hashed before reuse. Generated output folders are excluded from the source inventory.

Jobs live under `<cache>/jobs/<plan-id>/`: `job.json` records state, timings and owned files;
`request.json` contains the reusable configuration; scan reports and logs survive failed
LOD attempts. These local files contain paths from your computer. Review them before sharing.
Output location locks prevent two pipeline jobs writing the same destination.

Every stage (grass, textures, terrain and LOD) writes to a sibling staging directory and
publishes only after generation and output verification succeed. The output receipt is
written into the staged directory, so it is published together with the files. On Linux
the old and new directories are swapped in one atomic step; elsewhere, two renames are
used, and if the process stops between them the next run reports the leftover copy.
The replaced directory is kept with a `.previous-` suffix. **Previous outputs to keep**
(Generation Overview > Run options, `--keep-previous` in the CLI, default 1) sets how many
are kept; older ones are removed after each publication. `PenguLOD clean` lists and removes
them on request. A concurrent change to the original output's generated files prevents
publication. `meta.ini` and `SKSE/Plugins/PenguLOD.ini` belong to your installation: they
are never hashed or replaced, and changes to them during a run are kept. Failed stages can be retried. Terrain saves each complete mesh/diffuse/normal tile set,
including seasonal tiles, under the job's `terrain-tiles` directory. A retry verifies the
cached file hashes while copying into staging. Missing, changed or malformed checkpoints
are rebuilt individually. Cancellation before a tile finishes leaves it uncached. Settings,
source or application-version changes invalidate its tile cache. Heightmaps and the final
manifest are regenerated when the terrain stage resumes; the old published directory stays
intact until the complete replacement is ready. Tile caches made for earlier settings and
unfinished cache entries are removed when terrain generation starts. Grass and textures keep
their manifest checks; texture generation keeps files it never wrote in its output folder.

Object blocks scheduled for rebuild or deletion are not copied into staging. Unchanged
and unselected blocks are copied independently; the previous directory remains intact.
Hashes recorded during completed block writes and retained-file copies are used for the
new stage's snapshot while those staged files retain their recorded metadata. Other files
are read and hashed normally. Resume always rechecks published file contents, including
changes that preserve size and timestamps. Retained blocks that disagree with the hashes
in the output's receipt are rebuilt; a damaged block outside the selected scope stops
publication. The receipt describes what was last published to the folder, so separate jobs
(for example the desktop's per-stage buttons) can take turns on one output folder.

Before generation, disable the previously installed **object LOD** mod. The scan excludes
PenguLOD/AnvilLOD-owned references and terrain support meshes even when previous output is
enabled, and preflight reports installed output plugins independently of the destination.
After generation, install and enable the matching texture, terrain and object outputs.
Generating into a separate folder does not update an installed mod automatically. Purple
tree cards after an HD-texture run can indicate that the installed texture output is older
than the object LOD that references it.

The CLI runs the same engine:

```sh
PenguLOD pipeline --request /path/to/request.json --cache-folder /path/to/cache
```

Use `--no-resume` to force stages and terrain tiles to run or `--verify-source-contents` for full input hashes.
An explicit clean LOD request always runs. Timings are measurements: preparation, processing,
verification and publication. Summed write-worker time can overlap processing; it is not
an elapsed-time estimate. Reused stages report current verification separately from the
original build's measurements in the journal.

Terrain reports split mesh building, material sampling, DDS encoding, file writing,
heightmap export, tile verification and cache writing. They count newly built and restored
tiles separately. A reused whole stage shows its current verification time; its saved
result still describes the original generation. Keep the cache outside game files, input
mods and generated output folders. Deleting a job's `terrain-tiles` directory while no job
is running frees its tile cache; the next incomplete terrain stage rebuilds those tiles.

A synthetic benchmark is included for comparing source builds without game assets:

```sh
dotnet build tools/benchmarks/TerrainBenchmark.csproj -c Release
dotnet tools/benchmarks/bin/Release/net10.0/TerrainBenchmark.dll 512 5
dotnet tools/benchmarks/bin/Release/net10.0/TerrainBenchmark.dll 512 5 fallback
```

It uses four CPU workers, one warmup, then five measured runs of a 512-pixel LOD4 tile.
The default fixture has 16 LAND cells; `fallback` uses an installed BTR instead. JSON
includes phase timings and a generated tile digest for checking identical output. CI
publishes three-run reports in `Linux-test-results`. These small fixtures isolate sampling
costs; they do not predict complete modlist runtimes. Optional map availability, selected
mips and up to 65,536 fallback height vertices are cached to avoid repeated sampling work.
