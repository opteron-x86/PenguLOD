# Command-line usage

The CLI and desktop share the preset schema, request validation and persistent pipeline.
Export a preset from Preferences, or use **Export pipeline request** on Pipeline.
Both formats retain the full
grass, texture/material, terrain/seasons, tree budget, atlas, runtime and compute settings.

```sh
PenguLOD pipeline --settings my-preset.json --stages grass,textures,terrain,lod
PenguLOD texgen --settings my-preset.json
PenguLOD terrain --settings my-preset.json
PenguLOD grass --settings my-preset.json
PenguLOD generate --settings my-preset.json
PenguLOD scan --settings my-preset.json --report scan.json
PenguLOD export-job --settings my-preset.json --stages textures,terrain,lod --output job.json
PenguLOD pipeline --request job.json --report run.json
PenguLOD diagnose --settings my-preset.json --report diagnostics.json --deep-scan
PenguLOD job-status --job-folder /path/to/cache/PenguLOD/jobs/job-id
```

Omit `--stages` to use the desktop's selected pipeline stages. Stage commands select exactly
one stage. Referenced generated inputs must already exist for a standalone LOD command;
a combined pipeline connects its new outputs automatically. `--request` and `--settings`
are mutually exclusive. A raw request must define every requested stage.

The preset or request defines the generation. Options that would change it, such as
`--output`, `--worldspace` or `--grass-density`, are refused next to `--settings` or
`--request` instead of being ignored; change the preset in the desktop instead. Run
options are accepted: `--stages`, `--job-folder`, `--cache-folder`, `--threads`,
`--report`, `--texture-compute`, `--no-resume`, `--no-preflight`, `--verify-source-contents`,
`--expand-output-scope`, `--deep-scan`, `--verify-outputs`, `--inspect-asset` and
`--wine-prefix`. `export-job` also takes `--output` for the request file it writes.

`--cache-folder`, `--threads`, `--texture-compute Cpu|Auto|Vulkan`, `--no-resume`,
`--verify-source-contents`, and `--no-preflight` provide explicit overrides. Preset cache
and worker settings are honored when overrides are absent. A saved job stores stage
receipts, verification hashes, logs and its concrete request. The optional `--report`
emits compact run status and the journal. These private run reports can contain source
paths; use the Diagnostics export for sharing.

Existing flag-based `generate` also uses transactional publication and checkpoints;
`scan` remains read-only. Numeric options must be plain numbers within the range shown
in `--help` (for example `--grass-density 15`, not `15%`); out-of-range values are errors.
Without `--report`, `scan` writes `PenguLOD.scan.json` in the current folder and
`generate` points to `scan-report.json` in its job folder. Legacy author, mesh maker, conversion and repair commands remain
available in `--help`. Exit status is 0 for success, 2 for argument/generation failure,
and 130 for cancellation. `diagnose` returns 1 for blocking findings.

Use `PenguLOD --help` for the complete command reference.

`scan --request job.json` reads the request's LOD stage and forces generation off. Scans
may prepare private caches, including [automatic object models](OBJECTS.md), but never
publish into the LOD output folder. Preset cache folders and worker limits apply to scans.

For flag-based `scan`, `generate` and `author`, repeat `--resources /path/to/pack` in
priority order. Later folders win; enabled mod resources win over external folders.
`--dyndolod` remains a compatible name for the primary resource folder. For stage commands
using presets or JSON, set `ResourceFolders` (one path per line in a desktop preset) or
`ResourcePacks` (an array on each texture/LOD request). See [resource packs](RESOURCE-PACKS.md).

Flag-based generation exposes `--object-atlas --atlas-cell 512` and the independent
`--optimize-object-textures --object-texture-size 1024`. Presets store `objectAtlas`,
`atlasCell`, `objectTextures` and `objectTextureSize` in `Options`; raw LOD requests use
`ObjectAtlas`, `AtlasCellSize`, `OptimizeObjectTextures` and `ObjectTextureSize`.
See [object materials and LOD textures](ATLASES.md) for quality tradeoffs and rebuild scope.

## Saved-output reports and packages

Use `cost-report`, `compare-costs`, `verify-output`, `plan-update` and
`publish-update` to inspect saved generations and prepare a verified package.
`plan-generation` reviews the scope needed to update existing stage folders;
`--expand-output-scope` permits that expansion for a generation run.
See [Reports and output management](REPORTS-AND-OUTPUTS.md) for commands and limits.

`--pbr-waterfalls` controls PBR waterfall model selection independently of
`--pbr-lod`.
