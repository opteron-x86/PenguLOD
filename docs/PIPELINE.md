# Persistent generation

The Pipeline page selects grass, textures and LOD stages. Each stage needs its own output
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
output prevents publication. Failed stages can be retried; within-stage work is not a
durable checkpoint. Grass and textures retain their own publication and manifest checks.

The CLI runs the same engine:

```sh
PenguLOD pipeline --request /path/to/request.json --cache-folder /path/to/cache
```

Use `--no-resume` to force stages to run or `--verify-source-contents` for full input hashes.
An explicit clean LOD request always runs. Timings are measurements: preparation, processing,
verification and publication. Summed write-worker time can overlap processing; it is not
an elapsed-time estimate. Reused stages report current verification separately from the
original build's measurements in the journal.
