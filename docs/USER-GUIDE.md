# PenguLOD walkthrough

Use this guide for a normal generation run. The [settings reference](SETTINGS.md)
explains the controls by page; the [tool reference](TOOLS.md) covers mesh authoring,
texture repair and custom rules.

LOD is the simpler scenery Skyrim draws beyond the nearby, fully loaded area.
PenguLOD can generate its textures, landscape, objects, trees and grass. Choose the
stages you need; a usable existing grass cache or terrain output can be reused.

## 1. Prepare the game and output folders

Finish installing and ordering the mods you want to use. Include any LOD models,
billboards and resource packs supplied for those mods. Disable previous generated
**object LOD** outputs, including their generated plugins, before a new run. Keep
the source mods and LOD resource packs enabled.

Give each stage its own folder outside the game and installed mods. For example:

| Stage | Example output folder | What it supplies |
| --- | --- | --- |
| Grass cache | `PenguLOD-output/Grass` | Saved grass placements |
| Texture generation | `PenguLOD-output/Textures` | Distant textures and images of trees and grass |
| Terrain | `PenguLOD-output/Terrain` | Distant landscape, water and terrain textures |
| Object / tree / grass LOD | `PenguLOD-output/LOD` | Distant scenery and any selected generated plugins |

Keep a working generation intact when testing new settings. Use a separate set of
test folders. Each stage's output must also be separate from the other stages.

Download and extract a package from [Releases](https://github.com/opteron-x86/PenguLOD/releases).
On Linux, run `linux-x64/run-pengulod.sh`; on Windows, run
`win-x64/PenguLOD.Desktop.exe`. Keep the extracted folder together. See
[package instructions](PACKAGING.md) for AppImage and desktop shortcuts.

## 2. Select the correct source

On **Overview**, choose your game edition and source:

- **Fluorine or Mod Organizer 2:** select **Mod Organizer 2**, browse to the instance
  containing `ModOrganizer.ini`, click **Read profiles**, and choose your profile.
  This is the instance folder, not the manager's executable.
- **Deployed Data folder or Vortex:** select **Data folder / Vortex**, then choose the
  game/Data folder and its `plugins.txt`. Only mods deployed there are available.

For a Linux setup whose manager stores Windows paths, use **Advanced source paths →
Wine / Proton prefix**. Select the game's `pfx` folder or its containing compatdata
folder. Use the individual path overrides only when the saved locations are wrong.

Set **Output mod folder** to the object LOD folder. Choose **Worldspaces**:
`Tamriel` for the main Skyrim world, a comma-separated list for selected worlds,
or leave it blank for every world with LOD settings. In alpha-14, `*` works for
object LOD only; use a blank field when the run includes all-world terrain.

The world selection applies to terrain and object LOD. **Grass cache has its own
worldspace selection. Texture generation uses the selected load order and is not
limited by Overview's Worldspaces field.**

## 3. Choose a starting point

On **Generation settings**, apply **Balanced preset** for a first comparison. It
sets Medium mesh rules, grass LOD density to `0.05`, and turns Detailed ships off.
It also resets the other generation options to their defaults. It keeps source
paths and the separate texture-generation and grass-cache settings; the shared
Seasonal LOD switch turns off for both terrain and objects.

Then make the choices your setup needs:

- Enable **3D tree LOD** if you want 3D trees in the nearest LOD band. Balanced leaves
  this off. Leave **3D trees at LOD 8** off for the initial test.
- Enable **Grass billboard LOD** only when you have a suitable grass cache. This is
  separate from **Community Shaders grass**, which generates simpler grass models.
- Leave **Build missing object LOD models** off initially if performance is the
  priority. It can add objects that previously had no distant model.
- Enable seasonal, PBR and runtime features only when they match your installed setup.

Leave all four LOD levels selected for a complete object LOD output. Lowering a
mesh rule preset is a better first quality adjustment than omitting whole levels.
The levels are distance bands, not direct view-distance sliders.

Use **Preferences → Export settings…** to save the configuration before comparing
it with another run. Changing several options at once makes an FPS difference
hard to explain.

## 4. Configure the stages you need

| Stage | Normal setup |
| --- | --- |
| Grass cache | Choose its output and worldspaces. Leave the bundled generator selected. Use an existing valid cache if grass placement and the relevant load order have not changed. |
| Texture generation | Choose its output. Enable Stitched object textures and Tree billboards; add Grass billboards when using grass LOD. Use Hybrid tree trunk textures for installed hybrid tree models that need them. Leave the model filter blank for a full run. |
| Terrain | Choose its output. Start with the current size and brightness defaults. Enable seasonal or shader-specific output only when needed. |
| Object LOD | Review tree, grass and object choices on Generation settings. Keep texture matching and hidden-geometry removal enabled unless diagnosing a specific problem. |

Compatible recipes and render resources in enabled mods are found automatically.
Use **Primary resource folder** or **Additional resource folders** for resources
kept elsewhere. An existing DynDOLOD resource folder is accepted; its executables
are not required. See [resource packs](RESOURCE-PACKS.md) for supported layouts.

If reusing completed output, enable its **Use generated…** option and check the
folder path. The combined pipeline passes newly completed stages to later stages
automatically. Reading a texture folder during generation does not install it into
the game.

## 5. Check and generate

On **Pipeline**, select the required stages. They run in this order:
**grass → textures → terrain → object LOD**. For textures and object LOD only,
select **Textures** and **Object / tree / grass LOD**.

Use **Diagnostics → Run diagnostics** to review missing inputs and output conflicts.
**Scan load order** on Overview shows which objects and trees can receive LOD.
For a large setup, both checks can take time.

Click **Run / resume pipeline**. Leave reuse enabled so completed stages can be
verified and reused after a restart or a later-stage failure. After a failed run,
correct the reported problem and repeat the same selection. A cancelled or failed
LOD stage preserves the previously published output.

Review the results before installing. A warning can mean a model uses a billboard
fallback or an existing texture; it can also identify missing assets that need
attention. Use **Open run folder** for the full report.

## 6. Install the completed outputs

Install each completed stage as a separate mod. Replace the previous output of
that type instead of merging old and new files. Keep the matching generated texture
output enabled alongside the object LOD output.

Make generated textures win conflicts with their source textures, and make the
object LOD output win conflicts with the texture output. Terrain and grass-cache
outputs should replace older outputs of the same type. Enable generated plugins
that the report asks you to use; place `Occlusion.esp` last if you generated it.

`AnvilLOD.esm`, when present, contains additional large-reference lists.
`AnvilLOD.esp` and any effects plugins support selected placed objects and effects.
Some configurations need none of these. Read the report before treating an absent
plugin as a failure.

Dynamic LOD and the in-game quality controls need the separate **AnvilLOD SKSE
plugin** for your Skyrim runtime. **SKSE DLL installation → None** means you install
that DLL yourself. See [runtime dependencies](PROVENANCE.md#game-runtime).

## 7. Compare in game

Restart Skyrim after replacing generated files. Compare the same location, camera
direction, weather and time of day. Check tree silhouettes, building transitions,
terrain colour, shorelines and grass brightness, then record FPS.

Restore any hidden layers in the AnvilLOD in-game menu before judging coverage.
Hiding **3D trees → LOD4** does not substitute tree cards for that band. Saved layer
settings can remain active when you change generator versions.

For a **Tamriel-only performance test**, generate into new folders and install the
result as a separate test mod, with the previous object LOD mod disabled. Test in
Tamriel, then switch back to the complete output when finished. Do not merge the
whole test folder into the complete output: generated plugins, runtime data and
the shared grass atlas may affect worlds outside the test. Texture generation
still covers the load order even when object LOD is restricted to Tamriel.

## What to rerun after a change

| Change | Stages to reconsider |
| --- | --- |
| Object/tree LOD detail, brightness, atlases or model selection | Object LOD |
| Billboard size, texture brightness or baked materials | Textures, then object LOD |
| Terrain texture size, brightness, water or shadow heightmaps | Terrain; also object LOD if its terrain input changes |
| Grass placement, grass mods, landscape or cache settings | Grass cache, then object LOD; textures too if the grass models or textures changed |
| Object placement, tree removers or relevant plugin changes | Object LOD; check whether textures, terrain or the cache also depend on the changed mods |
| New seasonal swaps | The terrain, object/tree/grass and texture stages affected by those swaps |

See [troubleshooting](TROUBLESHOOTING.md) for failed runs and
[diagnostics](DIAGNOSTICS.md) for repeatable visual checks.
