# Desktop and CLI parity

The CLI and desktop share the preset schema, request validation and persistent pipeline.
The shared configuration assembly has no Avalonia dependency. Export a preset from
Preferences, or use **Export pipeline request** on Pipeline. Both formats retain the full
grass, texture/material, terrain/seasons, tree budget, atlas, runtime and compute settings.

```sh
PenguLOD pipeline --settings my-preset.json --stages grass,textures,terrain,lod
PenguLOD texgen --settings my-preset.json
PenguLOD terrain --settings my-preset.json
PenguLOD grass --settings my-preset.json
PenguLOD generate --settings my-preset.json
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

CLI tests export desktop presets, generate textures, reuse verified output, inspect saved
jobs, and reject undefined stages/conflicting request formats. Unrelated reports beside
`plugins.txt` no longer invalidate the source fingerprint.
