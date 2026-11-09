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

`--cache-folder`, `--threads`, `--texture-compute Cpu|Auto|Vulkan`, `--no-resume`,
`--verify-source-contents`, and `--no-preflight` provide explicit overrides. Preset cache
and worker settings are honored when overrides are absent. A saved job stores stage
receipts, verification hashes, logs and its concrete request. The optional `--report`
emits compact run status and the journal. These private run reports can contain source
paths; use the Diagnostics export for sharing.

Existing flag-based `generate` also uses transactional publication and checkpoints;
`scan` remains read-only. Legacy author, mesh maker, conversion and repair commands remain
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
