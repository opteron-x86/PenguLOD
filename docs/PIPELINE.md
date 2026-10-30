# Persistent generation

The Pipeline page selects grass, textures, terrain/water and object LOD stages. Each stage needs its own output
folder. Run the same selection again to resume after cancellation, failure or an application
restart. A completed stage is reused only when its settings, input inventory, dependencies
and every recorded output file still match. Changing a stage invalidates its dependents.

Plugins and configuration files are content hashed. By default, large source assets use
size and filesystem timestamps; enable **Verify all source asset contents** when sources
may have been replaced while preserving metadata. Published outputs are always content
hashed before reuse. Generated output folders are excluded from the source inventory.

Jobs live under `<cache>/jobs/<plan-id>/`: `job.json` records state, timings and owned files;
`request.json` contains the reusable configuration; scan reports and logs survive failed
LOD attempts. These local files contain paths from your computer. Review them before sharing.
Output location locks prevent two pipeline jobs writing the same destination.

LOD generation writes to a sibling staging directory. It publishes only after generation
and output verification succeed. The previous directory is retained with a `.previous-`
suffix; remove old backups yourself when satisfied. A concurrent change to the original
output prevents publication. Failed stages can be retried. Terrain saves each complete mesh/diffuse/normal tile set,
including seasonal tiles, under the job's `terrain-tiles` directory. A retry verifies the
cached file hashes while copying into staging. Missing, changed or malformed checkpoints
are rebuilt individually. Cancellation before a tile finishes leaves it uncached. Settings,
source or application-version changes invalidate its tile cache. Heightmaps and the final
manifest are regenerated when the terrain stage resumes; the old published directory stays
intact until the complete replacement is ready. Grass and textures retain their own
publication and manifest checks.

Object blocks scheduled for rebuild or deletion are not copied into staging. Unchanged
and unselected blocks are copied independently; the previous directory remains intact.
Hashes recorded during completed block writes and retained-file copies are used for the
new stage's snapshot while those staged files retain their recorded metadata. Other files
are read and hashed normally. Resume always rechecks published file contents, including
changes that preserve size and timestamps. Retained blocks that disagree with a saved
output hash are rebuilt; a damaged block outside the selected scope stops publication.

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
