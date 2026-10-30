# Diagnostics and transition validation

Pipeline preflight runs before generation by default. It checks the selected profile,
missing enabled plugins, generated-input manifests and load-order compatibility,
conflicting outputs, runtime dependencies and available staging space. A failed check
leaves the published output untouched. Warnings require review but do not block a run.

The **Diagnostics** page can also scan model and billboard coverage, audit the textures
in up to 1,000 selected models, verify every recorded generated file, and identify the
winning loose/archive source of a Data-relative asset. File verification can take several
minutes with a large grass cache. Counts and geometry costs are estimates; elapsed time
is measured and is never presented as a predicted full-generation time.

Exported JSON contains metadata and replaces known home, game, profile and output paths.
It does not bundle assets, presets or logs. Review reports before sharing: plugin names,
relative asset names and diagnostic messages remain useful identifying information.
Each pipeline job saves its own `diagnostics.json` alongside its journal.

The CLI uses the same checks:

```sh
PenguLOD diagnose --request pipeline.json --report diagnostics.json --deep-scan --verify-outputs
PenguLOD diagnose --request pipeline.json --report winner.json --inspect-asset 'meshes/landscape/trees/tree.nif'
```

DLL presence and a valid x64 PE header do not establish SKSE/runtime compatibility. The
report identifies this distinction. The game-side plugin still runs inside Skyrim under
Wine/Proton; only the generator runs natively on Linux.

## Base Object Swapper

The scan report's `baseObjectSwaps` section lists the active configuration count,
supported removal-rule count, excluded exterior references and unsupported entries.
Each exclusion includes its reference/base FormKeys, worldspace, source filename and
line. The count covers exterior references, including ones that did not have usable LOD.

PenguLOD reads winning root `*_SWAP.ini` files from the selected Data view. Disabled mods
and hidden files do not participate. Files are ordered by case-sensitive filename, with
later entries taking precedence. Deterministic reference and base swaps to Skyrim's
`00003B` XMarker remove those references from LOD; a reference-specific swap takes
precedence over a base swap. Missing BOS runtime files produce a warning and no exclusions.

This is a removal subset, not full BOS emulation. Visible replacement models, transforms,
property-only flag edits, conditional rules, seeded/random chance selection and
MergeMapper remapping are not applied. A later ambiguous swap blocks an earlier removal
for that source; a conditional base swap blocks an unconditional base removal. These
cases remain in the report rather than silently removing potentially visible objects.
Legacy `flag(...)` in a swap's fourth field is not interpreted as an initially-disabled
flag; supported removers work because their replacement is the invisible marker.

After changing a remover, run the LOD stage again and replace the installed LOD output.
The pipeline invalidates saved stages when swap INI contents change, and regeneration
removes obsolete tree/object blocks. No source plugin or mod configuration is rewritten.
For format and runtime semantics, see [BOS source](https://github.com/powerof3/BaseObjectSwapper)
and [LOD integration notes](https://dyndolod.info/Mods/Base-Object-Swapper).

## Repeatable checks

`TerrainFixture` creates a small world from synthetic records and textures. Automated
tests cover BTR structure, water segments, shared and mixed-level edges, heightmap
orientation, input invalidation, cancellation/resume, failure recovery and stale caches.
The desktop suite exercises the same pipeline with that fixture. No game assets are
distributed with tests.

Automated structure checks cannot establish an in-game visual match. After installing
generated output, use a disposable save and keep the same weather, time and camera for
comparisons. Inspect all of the following and record location/worldspace and settings:

- Walk across the full-model/LOD boundary and the LOD4/8/16/32 distances. Check holes,
  floating objects, repeated trees, silhouette changes and visible ground seams.
- Look across water shorelines and tile boundaries from above and near water level.
- Compare grass tips, roots and terrain under direct sunlight and overcast skies.
- Inspect tree leaf coverage and trunk alignment at near, middle and far distances.
- Repeat with Terrain Shadows enabled, then disabled, and check shadow height alignment.
- With Seasons of Skyrim, repeat in each configured season; confirm swaps and distant
  assets agree with the loaded-cell appearance.

Attach the diagnostic export, generation report and paired screenshots when reporting
a problem. A passing fixture is a format/regression check, not a substitute for these
game-side transition checks.
