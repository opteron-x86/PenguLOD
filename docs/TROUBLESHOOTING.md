# Troubleshooting

## A stage failed

Use **Open run folder** to read the full log and result report. Correct the first reported
error, then repeat the same pipeline selection. Verified completed stages can be reused;
failed stages rerun. Each stage publishes separately, so a later failure does not undo
earlier completed stages. See [persistent generation](PIPELINE.md).

Use **Diagnostics** to check missing plugins, source precedence, stale generated inputs
and runtime requirements. For missing models or textures, inspect which enabled mod or
archive supplies the named asset. Reinstall or correct the source mod when necessary.

Terrain logs identify the default landscape texture and report missing contributing
textures with world/cell context. The default is `textures/landscape/dirt02.dds`, subject
to the selected INI's `sDefaultLandDiffuseTexture`. See [terrain settings](TERRAIN.md).

## Output looks different from nearby terrain or trees

Use [material calibration](MATERIALS.md) to adjust the affected output's brightness.
Tree coverage reports explain when a tree uses an authored model, native reduction or
a billboard fallback. Regenerate the affected stages, install their output, then compare
at the same location, time and weather. Follow the [transition checks](DIAGNOSTICS.md#repeatable-checks).

## No large-reference master in the output

`AnvilLOD.esm` contains additional large-reference lists for Skyrim's full-detail
object grid. It is separate from object and tree LOD meshes. The generator omits it
when large references are disabled or no additional eligible references are found;
read `AnvilLOD Large References.txt` for the result.

Older builds counted entries from an enabled previous generated master as source data,
which could cause the replacement master to be omitted. Disable the previous object
output before regenerating with those builds. Current builds ignore previous generated
plugins during large-reference generation and rebuild the required lists from source
plugins. Replace the installed object output as a complete mod after generation.

## Trees or grass disappear after a quality comparison

The AnvilLOD runtime's **Quality Layers** controls persist in
`Data/SKSE/Plugins/AnvilLOD.ini` when saved. Restore the layers used for comparison before
judging new output. Disabling **3D trees → LOD4** hides that near LOD layer without
substituting billboard cards; the farther card layers start at their own block distances.
Changing the generator version does not reset these game-side settings.

## PGLite reports a missing drive such as `d:`

Malformed generated textures can contain a literal Windows drive directory such as
`textures/d:`. Check the installed output for that directory. Generate clean output
with the current application and replace the installed output mod so malformed files
are removed.

The packaged repair script can preview and quarantine verified malformed placeholder
folders from older output. Close Skyrim and run it from the package directory:

```sh
python3 repair-pglite-paths.py "/path/to/installed/output" "/path/to/generated/output"
```

Add `--apply` to quarantine matching files. The script requires the output manifest,
validates placeholder contents, refuses unknown files/symlinks and writes a backup receipt.
Regeneration is still needed to correct mesh references. The script is also available
under `tools/` in the source checkout.

## Reporting a problem

Include the PenguLOD version, Linux distribution, source mode, failed stage, reproducible
settings and a relevant log excerpt. [Diagnostics](DIAGNOSTICS.md) exports metadata with
known local paths replaced. Review reports and logs for private information before posting
to [GitHub issues](https://github.com/opteron-x86/PenguLOD/issues).
