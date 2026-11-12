# Reports and output management

Keep a generation that looks good and performs well in its own folder. Export the
settings used for it. Generate experiments into different folders so you can
compare them and return to the working version.

## Measure a generation

On **Reports & outputs**, choose an **Output folder to measure**, then click
**Measure output**. This reads the folder without changing its contents. The
report is saved in the run folder; **Export report** saves another copy.

The report covers every saved block, including blocks reused from earlier runs:

- Triangles and drawable mesh groups, grouped by world, level, season and layer.
- The largest blocks, to help locate expensive areas.
- Block sizes and local DDS texture sizes, formats and mip counts.
- Files that could not be measured. Missing measurements are not counted as zero.

Mesh groups help identify batching costs. They are not measured draw calls, and
neither triangle counts nor file sizes predict FPS. Texture bytes describe disk
storage, not resident VRAM. Textures supplied by other mods or archives are outside
the report's scope. Legacy `.btt` files have storage measurements but no geometry
counts. Terrain `.btr` and object `.bto` files have geometry counts when readable.

Successful object generation also writes `PenguLOD.costs.json` automatically. It
includes the generation's numeric, Boolean and enum choices without source paths.
Reports made from older folders may have no saved settings; they remain useful
for comparing geometry.

## Compare runs

Choose the **Before report** and **After report**, then click **Compare cost
reports**. Negative changes mean fewer bytes, triangles or mesh groups.

Geometry deltas use matching block paths, including world, level, coordinates and
season. Added or removed blocks are listed separately. A Tamriel-only run is not
treated as a whole-game improvement. Texture comparisons follow the same rule.
Settings changes are shown when both reports contain settings.

Check in-game FPS at the same place, direction, weather and resolution. Change one
setting at a time. Keep the reference generation until the new result is confirmed.

## Verify a matching output set

New pipeline runs write `PenguLOD.output-set.json` in each stage folder. It records
the completed stage, owned files, generated scope and any generated inputs needed
by object LOD. Preview or filtered output is marked incomplete.

Add the stage folders under **Stage output folders**, then click **Verify output
set**. Include matching texture, grass and terrain folders if object LOD used
them. Verification checks file contents, missing dependencies, changed files,
duplicate stages and conflicting paths. It reads every recorded file and can take
several minutes for a large output.

This checks the selected folders. It does not inspect a mod manager's deployment
or resolve the game's complete winning asset order. For installed-output checks,
select the actual combined package or installed stage folders.

Output folders may sit below a symbolic link, such as `~/.steam/steam`. The stage or
package folder itself must be a real folder, and links inside it are refused, because
publication renames the folder and must not follow a link elsewhere.

Older outputs without receipts remain usable in game and can be measured. They
cannot be adopted as managed output automatically: regenerate into a new folder
when you want to use managed updates. Keep your working generation as it is.

## Prepare or update an installation package

1. Add the complete stage outputs you want to install. Select one version of each
   stage, including dependencies required by object LOD.
2. Choose a **Package destination** outside the source folders. Start with a new
   folder, or use a package previously created here.
3. Click **Preview package update** and review the replacements and any errors.
4. Click **Publish reviewed package**. Source folders remain unchanged. The new
   package is prepared separately, checked again and moved into place. An existing
   destination is retained in a sibling `.previous-…` folder.
5. Install the resulting package with your mod manager, replacing its previous
   version. Enable generated plugins and install the separate game plugin if needed.

Stages omitted from an update are retained. Replacing textures while retaining
object LOD built against different textures is blocked. Unknown files in an
existing package are also blocked rather than deleted. If a source or destination
changes after preview, create a new plan. Cancellation before publication leaves
the destination intact.

This tool does not activate mods, change load order or modify a game installation
automatically. The previous package is kept until you choose to remove it.

## Partial-world generation

Generated masters, dynamic data and some atlases are shared across worlds. A
partial set cannot safely replace those files while retaining unrelated old blocks.

Use **Generation Overview → Run options → Review output scope** before updating an existing generation
folder. If more worlds or levels are needed, enable **Keep existing worlds and levels**.
The run includes the union of the old and requested
scope. Your saved world selection is unchanged. Expansion is off by default.

Scope review only reads the selected output folders. It can inspect older AnvilLOD
output and reports its stage, path and identifying files. Before generating into
that location, choose an empty folder or clear the complete old generated output.
The review does not delete files or validate the game installation.

For a quick isolated test, use a new output folder. Managed package updates reject
a replacement that omits worlds or levels from the previous stage. They do not
merge ESM files or shared atlases. Regenerate the required scope before updating
the complete package.

## CLI equivalents

```sh
PenguLOD cost-report --input "Working Object LOD" --report baseline-costs.json
PenguLOD cost-report --input "Test Object LOD" --report test-costs.json
PenguLOD compare-costs --before baseline-costs.json --after test-costs.json --report comparison.json
PenguLOD verify-output --input "Textures" --input "Object LOD" --report verification.json
PenguLOD plan-update --input "Textures" --input "Object LOD" --output "Install Package" --report update-plan.json
PenguLOD publish-update --plan update-plan.json
PenguLOD plan-generation --settings preset.json --report scope-plan.json
PenguLOD pipeline --settings preset.json --expand-output-scope
```

Use `cost-report --settings saved-preset.json` to attach a known generation preset
to an older folder's report. Only use the preset that produced that output. Save
reports outside the folder being measured. Update plans and pipeline requests
contain local paths; review them before sharing.
