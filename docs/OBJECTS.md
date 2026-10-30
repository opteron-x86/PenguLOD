# Automatic object LOD

Enable **Build missing object LOD models** under **Generation settings → Automatic object
LOD** to create missing source meshes during a scan or LOD job. This is optional and off
by default. It uses the native mesh maker; no external executable is required.

The active rules still decide which objects and levels appear. Installed MNAM and named
LOD models take precedence. Explicit `None`, billboard and dynamic-only rules do not
trigger generation. Existing fallback from an installed farther source level is preserved.
Automatic generation fills only the remaining requested gaps. Deleted, disabled and Base
Object Swapper reference filtering still applies to the final reference list.

## Quality and cost

The default budgets are 2,500 triangles for source level 0, 1,000 for level 1 and 400 for
level 2. Rules usually select these for LOD 4, 8 and 16; a requested missing source level
3 reuses the far generated model. Farther generated levels cannot exceed nearer budgets.
**Minimum object size** defaults to 128 game units, measured along the largest model axis.

**Object reduction multiplier** scales surface simplification and removal of small parts.
Values below 1 retain more detail; values above 1 remove more. Meeting a triangle budget
can require further simplification or dropping small parts. These are cost limits, not a
guarantee of silhouette preservation. Review buildings, rocks, fences and thin shapes in
game before increasing coverage across a whole load order.

Generated meshes retain shape names, material boundaries and surviving UVs. Normal texture
matching applies record overrides and seasonal variants. **Pack object texture atlases**
can pack compatible materials; tiled UVs and unsupported atlas materials keep their source
textures. Generation does not reproduce animated or view-dependent materials.

## Reports and recovery

Overview's **Automatic object LOD** table, `scan-report.json` and the generated
`PenguLOD.objects.json` list requested source levels, triangle counts, budgets and fallback
reasons. Missing source models/textures, unsupported geometry, small models and unmet
budgets skip the affected level with a warning. Other installed or generated levels remain
available. This does not bypass errors in required installed LOD or invalid texture recipes.

Meshes are stored in the application's private cache and merged into the usual BTO output.
No source mods are edited and no separate generated mesh mod needs installation. Cache
entries are keyed by source content and settings and verified before reuse; damaged entries
are regenerated. Scans may prepare these cache files but do not publish LOD.

Saved presets and JSON requests retain all options (`Lod.AutomaticObjects`). Flag-based CLI
runs can use `--auto-objects`, `--object-near-budget`, `--object-middle-budget`,
`--object-far-budget`, `--object-min-size` and `--object-detail`. Changing these options
requires a LOD run; it does not require new grass placement caches or tree billboards.
