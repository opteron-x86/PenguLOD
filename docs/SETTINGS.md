# PenguLOD settings reference

This reference follows the **alpha-14** desktop interface. Use the
[walkthrough](USER-GUIDE.md) for a normal run and the [tool reference](TOOLS.md) for
mesh authoring, texture repair and custom rules. Settings save automatically.

Defaults below describe a fresh configuration. A saved configuration or preset
can have different values. Changing a setting takes effect on the next run;
install the regenerated output to see the change in game.

## Find a page

- [Overview: source and output](#overview-source-and-output)
- [Pipeline](#pipeline)
- [Texture generation](#texture-generation)
- [Terrain](#terrain)
- [Grass cache](#grass-cache)
- [Generation settings](#generation-settings)
- [Diagnostics](#diagnostics)
- [Preferences](#preferences)
- [In-game quality controls](#in-game-quality-controls)

A **billboard** is an image of a tree or grass tuft placed on a few flat surfaces.
A **3D LOD model** has more geometry and usually costs more to draw. An **atlas**
combines several textures into one sheet so compatible surfaces can be drawn
together. **Game units** measure distance or size in Skyrim; they are not metres.

## Overview: source and output

| Setting | What it does |
| --- | --- |
| Source | **Mod Organizer 2** reads an MO2 or Fluorine instance and its enabled mods. **Data folder / Vortex** reads files already deployed to the game. |
| Game edition | Select **SE / AE** or **VR** to match the game. |
| MO2 instance | The folder containing `ModOrganizer.ini`. The mod manager does not need to be running. |
| Profile | The profile to read. Blank uses the instance's selected profile. **Read profiles** fills the list from the instance. |
| Game / Data folder | For a deployed installation, select the game folder or its `Data` folder. |
| plugins.txt | The enabled-plugin list for a deployed installation. Use an explicit file; an empty file is valid for a vanilla game. |
| Output mod folder | Destination for object, tree and grass LOD. Keep it separate from the game, enabled mods and the other stages' output folders. |
| Worldspaces | World editor IDs separated by commas, such as `Tamriel, DLC2SolstheimWorld`. Blank selects all worlds with LOD settings. Applies to terrain and object LOD; in alpha-14, `*` works for object LOD only. Grass cache has its own selection; texture generation uses the load order. |
| Clear previous AnvilLOD output before generation | Rebuilds the object LOD output without reusing its previous blocks. Off by default. Use a dedicated output folder in either mode. |

**Advanced source paths** are overrides for installations whose saved paths need
help. Leave them blank when the normal source selection works.

| Setting | What it does |
| --- | --- |
| Wine / Proton prefix | On Linux, resolves Windows drive letters stored by the mod manager. Select the game's `pfx` folder or its containing compatdata folder. |
| Game folder override (MO2) | Replaces the game location saved in the instance. |
| Mods folder override | Replaces the instance's mod-storage location. |
| Profiles folder override | Replaces the location used to find profiles. |
| Overwrite folder override | Replaces the instance's Overwrite location. Files there can override enabled mods. |
| Skyrim.ini override | Uses a specific game INI instead of the one found through the source. |

**Scan load order** checks coverage without generating the final LOD. **Generate
LOD** builds object, tree and grass LOD using Generation settings. It does not run
texture generation or terrain generation. **Open output** opens the destination;
**Open run folder** opens logs and reports for the current job.

## Pipeline

The pipeline runs the checked stages in order. Give each stage its own output
folder. Newly generated inputs are passed to later stages automatically.

| Setting | What it does |
| --- | --- |
| Cache grass | Saves grass placements using the Grass cache page's settings. |
| Textures | Generates the categories selected on Texture generation. |
| Terrain and water | Generates terrain and, if enabled on Terrain, water LOD. |
| Object / tree / grass LOD | Builds distant scenery using Generation settings. |
| Run preflight before generation | Checks the selected stages for missing inputs, conflicting output folders and other blocking problems. On by default. |
| Reuse completed stages after verifying their files | Skips successful stages when their settings, inputs and output checks still match. On by default; useful after a later stage fails. |
| Verify all source asset contents | Reads the contents of every source asset when deciding whether previous work can be reused. Off uses a faster size-and-date check for large assets. Plugins and configuration are always content-checked. |

**Run / resume pipeline** starts the selection or resumes a matching saved job.
**Export pipeline request** saves that selection for the [CLI](CLI.md).
**Open saved job** opens its records. Output files are verified before reuse even
when full source verification is off.

The results separate preparation, processing and verification. **Write worker s**
can overlap processing and add together work from several threads; it is not an
extra amount to add to total elapsed time.

## Texture generation

Texture generation reads the selected load order. Overview's Worldspaces field
does not limit it. Changes here normally need **textures, then object LOD** to be
regenerated and installed together. Each texture run replaces that folder's
generated set, so keep all the categories you want selected.

### Inputs and categories

| Setting | What it does |
| --- | --- |
| Primary resource folder (optional) | Supplies compatible recipes, rules and render models kept outside enabled mods. Existing DynDOLOD resource folders work without running DynDOLOD executables. |
| Additional resource folders · one per line, later folders win | Adds more resource packs. Later folders take precedence over earlier external folders; enabled mods take precedence over external packs. |
| Texture output mod folder | Destination for the generated texture mod. Keep it separate from object LOD output. |
| Use this generated texture output for subsequent LOD jobs | Makes later LOD jobs read this folder. You must still install it as a mod for the game. |
| Stitched object textures | Builds object texture sheets from supplied copy and stitching recipes. On by default. |
| Tree billboards | Renders distant tree images from the winning tree models. On by default. |
| Grass billboards | Renders grass images needed by grass billboard LOD. Off by default. |
| Hybrid tree trunk textures | Renders trunk images aligned to installed hybrid tree LOD models. On by default; requires matching render resources and LOD models. This is separate from authoring new native hybrid trees. |
| Rendered object textures (experimental) | Uses supported object templates to render new LOD textures. Off by default. Unsupported rendering can retain a valid supplied texture pair, with a warning. |

### Material calibration

These controls adjust the appearance baked into rendered textures. PBR means
physically based rendering; use its calibration controls for PBR source materials.
Keep defaults for the first comparison, then adjust one control at a time.

| Setting | What it does |
| --- | --- |
| Bake source normal maps | Carries fine surface shading from the source model into the render. On by default. |
| Calibrate explicit PBR albedo paths | Adjusts source textures already identified as PBR albedo for the distant shader. On by default. |
| PBR scale | Brightness scale for calibrated PBR colour. Default `0.65`; lower is darker. |
| PBR gamma | Adjusts PBR midtones. Default `1.15`; higher darkens midtones, lower brightens them. |
| PBR occlusion strength | Amount of shading taken from the PBR ambient-occlusion map. Default `0.5`; `0` ignores it, `1` uses its full effect. |
| Ambient light | Overall light used when rendering models. Default `0.7`; increasing it brightens shaded areas. |
| Directional light | Light from a direction, which brings out shape and shading differences. Default `0.3`; increasing it adds stronger directional shading. |

### Quality

| Setting | What it does |
| --- | --- |
| HD front and side billboards | Gives crossed tree and grass cards different front and side images. On by default. Can improve silhouettes and add drawing cost compared with sharing one image. |
| Size tree textures for the largest placed reference | Gives trees that are placed at a large scale enough texture detail, up to the maximum size. On by default. |
| Unplaced tree resolution scale | Resolution multiplier for trees without a placed reference to measure. Default `1`. Changes texture detail, not the tree's size in game. |
| Grass texture layout | **Auto** detects ordinary or packed grass textures. Use **Basic** or **Packed** only to correct detection for the grass rendered by this job. |
| Complex grass detection threshold | Sensitivity used by Auto layout detection. Default `0.1`; normally leave it alone and use the layout override for a known mismatch. |
| Stitch base size | Base texture resolution for recipes and rendered objects. Default `256`; recipes can combine it into a larger sheet. Higher values use more texture memory. |
| Maximum billboard size | Caps the width and height of rendered billboards. Default `512`. Higher values help large trees but use more texture memory. |
| World units per pixel | Detail per pixel before the size cap. Default `12`; lower values produce sharper, larger textures. |
| Brightness multiplier | Overall generated-texture brightness. Default `1`; below `1` darkens, above `1` brightens. |
| Minimum tree height | Skips billboard rendering for trees shorter than this height in game units. Default `256`. |
| Minimum grass height | Skips billboard rendering for grass shorter than this height in game units. Default `16`. |
| Model path contains (optional) | Restricts billboards and rendered objects to matching paths for a preview. Stitching still processes all matching recipes. Leave blank for a complete output. |

Supplied billboard rules can override automatic sizing and minimum-height filters.
Use a separate folder for a filtered preview; it is not a complete texture mod.

**Generate textures** runs only this stage. **Textures, then LOD** runs textures
and object LOD; it does not add terrain. After a successful run, select a written
or retained result and choose **Preview selected texture** to inspect it.

## Terrain

Terrain uses Overview's source and worldspaces. These settings affect the distant
landscape, separately from the object settings. Regenerate terrain after changing
them; also regenerate object LOD when its terrain input changes.

### Output and coverage

| Setting | What it does |
| --- | --- |
| Terrain output | Destination for the terrain mod. Keep it separate from the other outputs. |
| Fill missing landscape from existing terrain LOD | Uses existing terrain tiles where source landscape is unavailable. On by default. The run stops if it still cannot cover the required area. |
| Generate seasonal terrain from Seasons of Skyrim swaps | Builds seasonal variants from installed swaps. Off by default. This is the same switch as **Seasonal LOD** in Generation settings. |
| Generate water LOD | Builds distant water surfaces. On by default. |
| Use generated terrain in object LOD generation | Makes object LOD use this terrain output, including for hidden-geometry removal. A pipeline that generates both stages passes it on automatically. |

### Textures and materials

| Setting | What it does |
| --- | --- |
| Texture size | Size of terrain texture tiles. Default `512`. Use a power of two from `64` to `2048`; higher values use more texture memory. |
| Brightness | Terrain texture brightness multiplier. Default `1`; lower darkens distant ground. |
| Vertex colour strength | How strongly the landscape's painted colours affect the result. Default `1`; `0` ignores them. |
| Texture repeats per cell | How often the landscape texture pattern repeats. Default `24`; higher makes the pattern appear smaller. |
| Edge skirt depth | Extends terrain edges down to hide gaps between detail levels. Default `512` game units; `0` disables skirts. |
| Source normal strength | Strength of fine shading from landscape normal maps. Default `1`; `0` uses terrain slope alone. |
| Calibrate PBR albedo | Adjusts identified PBR landscape colour for distant terrain. On by default. |
| Look for textures/pbr equivalents | Searches for PBR replacements for ordinary landscape textures. Off by default. Enable only when the game uses those PBR materials. |
| PBR scale | Brightness scale for calibrated PBR terrain. Default `0.65`; lower is darker. |
| PBR gamma | PBR terrain midtone adjustment. Default `1.15`; higher darkens midtones. |
| PBR ambient occlusion | Amount of source ambient-occlusion shading. Default `0.5`; `0` ignores it, `1` uses its full effect. |

### Terrain shadows

| Setting | What it does |
| --- | --- |
| Export Community / Bottled Shaders heightmaps | Creates terrain-height textures for Terrain Shadows. On by default. Requires complete rectangular height coverage. Keep these generated DDS files loose when installing. |
| Maximum heightmap dimension | Caps heightmap resolution. Default `8192`; higher uses more memory and retains finer terrain detail. |

**Generate terrain** runs this stage alone. **Open terrain output** opens its
folder. Generating heightmaps does not change the game's shader settings.

## Grass cache

A grass cache records placements. Grass billboard textures and distant grass LOD
are separate outputs. The bundled generator creates the cache without launching
Skyrim; install it as a mod and keep the appropriate NGIO setup for the game.

| Setting | What it does |
| --- | --- |
| Grass output mod folder | Destination for the cache mod. PenguLOD creates its `grass` subfolder. |
| Worldspaces | `all`, or world editor IDs separated by commas. Independent of Overview's Worldspaces. |
| Use generated grass cache for LOD | Makes later LOD jobs read this cache folder. The combined pipeline passes a completed full cache automatically. |
| Placement | **Smooth** blends placement across cell and texture boundaries. **Vanilla** follows the game's placement style. Default Smooth. |
| Collision rejection | Prevents grass growing through obstructing geometry. **Auto** selects an available method; **CPU** uses the bundled method. **GPU** requires a compatible external FasterNGIO build. |
| Seasons | **Auto** detects seasonal configuration; **On** requests seasonal cache generation; **Off** generates the ordinary cache. Independent of the Seasonal LOD switch. |
| Small-area preview | Generates a limited test area in one selected worldspace. Off by default. Use a separate output; a preview cannot serve as the complete cache for a full LOD run. |
| Cell X | Horizontal cell coordinate at the centre of the preview. |
| Cell Y | Vertical cell coordinate at the centre of the preview. |
| Radius in cells | Preview extent around the centre. `0` is one cell; default `1` is a 3 × 3 area. |

### Advanced grass settings

| Setting | What it does |
| --- | --- |
| FasterNGIO executable override | Uses an external compatible grass generator. Leave blank to use the bundled one. |
| GrassControl.ini override | Uses a specific NGIO configuration instead of the one found in the selected profile. |
| Minimum grass size · 0 = profile | Placement-density control: lower values produce denser grass. Default `0` uses the profile's setting. Despite the name, it does not set blade height. |
| Writer threads | Number of workers writing cache files. Default `1`; increasing it can help on fast storage but does not add placement workers. |
| Use visible geometry for rejection (experimental) | Checks visible model surfaces as well as the normal collision data. Off by default; can avoid more intersections but takes more work. |

Use **Preferences → Worker threads** for general processing parallelism. Grass
cache generation currently supports SE/AE, not VR. Changing the cache settings
does not change game or NGIO configuration files.

**Generate grass cache** runs this stage alone. **Grass, textures, then LOD** also
runs texture and object LOD generation; it does not include terrain.

## Generation settings

These settings govern the object LOD stage, including its trees and grass.
Use **Find a setting…** to search labels. The resource-folder controls have the
same meaning as on Texture generation. **Custom rule file** selects an optional
saved [mesh rule file](TOOLS.md#custom-mesh-rules).

### Presets

Each button resets the Generation settings options before applying its choices.
It leaves source paths and the separate TexGen and grass-cache settings in place.
Terrain's seasonal switch is shared with this page and therefore resets too.

| Button | What it selects |
| --- | --- |
| Balanced preset | Medium mesh rules, grass density `0.05`, Detailed ships off, and the other Generation options at their defaults. 3D trees and automatic object models remain off. |
| High quality | Restores the Generation options' defaults, including High mesh rules. It does not enable every optional feature. |
| Objects only | Turns off tree and grass LOD, Community Shaders grass, dynamic/grid objects, native grid objects, Animated Ships support and missing-billboard rendering; other options return to defaults. |

### Objects

| Setting | What it does |
| --- | --- |
| Mesh rules preset | Chooses **Low**, **Medium** or **High** entries from available mesh rules. Higher usually includes more detail or objects; the supplied rules determine the difference. Default High. |
| LOD 4 | Generates the nearest object LOD band. On by default. |
| LOD 8 | Generates the next object LOD band. On by default. |
| LOD 16 | Generates distant object LOD. On by default. |
| LOD 32 | Generates world-map object blocks where supported. On by default. |
| Map object mesh | `-2` is Auto, including detection of A Clear Map of Skyrim. `-1` uses only explicit LOD32 rules. `0`, `1` or `2` supplies that model level when a rule has no LOD32 choice. |
| Remove buried triangles | Removes model surfaces hidden below terrain. On by default; generally reduces drawing cost without a visible loss. |
| Remove underwater triangles | Removes surfaces entirely below water. On by default; requires Remove buried triangles because they use the same terrain check. |
| Match full-model textures | Matches alternate textures and snow variants to the full objects. On by default. A model whose textures cannot be mapped keeps its supplied LOD textures and produces a warning. |
| Repair non-opacity alpha | Creates opaque LOD texture copies for solid surfaces whose source alpha has another purpose. On by default; helps prevent unintended transparency. |
| Detailed ships | Uses simplified full ship models at LOD4/8. On by default, off in Balanced; adds detail and geometry. |
| Object brightness | Multiplies object LOD colour. Default `1`; lower darkens it. Source textures are left intact. |
| Ice inset | Moves glacier LOD surfaces inward to reduce overlap with nearby full models. Default `6` game units; `0` disables it. |
| Fit mountain surfaces | Fits mountain LOD surfaces during the transition. On by default; needs a positive Mountain inset. |
| Mountain inset | Inward adjustment for mountain LOD. Default `12` game units. Too much can make transitions visible. |
| Other object inset | Inward adjustment for buildings, walls and rocks at LOD4/8. Default `0`, disabled. |
| Snow shader | `0`: HD snow at LOD4; `1`: plain snow everywhere; `2`: HD snow everywhere. Default `0`. |
| Snow coverage | `0`: follow mesh-painted coverage; `1`: full snow coverage; `2`: full coverage with lighter colour. Default `1`. |

The LOD numbers are distance bands, not view-distance sliders. Keep all levels for
a complete output. The game controls when each level is shown.

### Automatic object LOD

| Setting | What it does |
| --- | --- |
| Build missing object LOD models | Creates missing model levels requested by active rules. Off by default. Installed LOD models and exclusions take priority. Can substantially increase scenery coverage and drawing cost. |
| LOD 4 object triangle budget | Maximum triangles per newly generated near model. Default `2500`; a model that cannot fit the budget is skipped at that level. |
| LOD 8 object triangle budget | Limit for newly generated middle-distance models. Default `1000`; cannot exceed the nearer level's budget. |
| LOD 16 object triangle budget | Limit for newly generated far models. Default `400`; also used for a requested level 3 model. |
| Minimum object size | Skips automatic generation for models whose largest dimension is below this many game units. Default `128`; increasing it excludes smaller objects. |
| Object reduction multiplier | Amount of simplification. Default `1`; lower retains more detail, higher allows more reduction. Triangle budgets still apply. |

These limits apply to newly generated source models, not the whole world or all
installed LOD models. Many copies of a modest model can still be expensive.

### Trees

| Setting | What it does |
| --- | --- |
| Tree billboards | Enables tree LOD from installed or rendered billboards. On by default; keep enabled when using 3D trees too. |
| Render missing billboards | Creates tree billboards from full models when no suitable billboard is installed. On by default. |
| Tree brightness | Multiplies tree LOD colour. Default `1`; lower darkens trees. |
| 3D tree LOD | Uses matching 3D tree models in the nearest LOD band. Off by default. Improves tree shape but can add considerable geometry and drawing cost. |
| 3D trees at LOD 8 | Extends 3D trees into the next band. Off by default; usually a larger cost than limiting 3D trees to LOD4. Farther bands use billboards. |
| Allow plain-name tree models | Accepts models matched by name without an exact source-model match. Off by default; enable only when those models suit the installed tree replacer. |
| Prefer named tree LOD | Gives explicitly named LOD meshes priority over exact source-model matches. Off by default. |
| Build missing 3D tree models | Simplifies full trees when a matching installed LOD model is missing. On by default, but requires 3D tree LOD. Trees that cannot meet the limits use billboards. |
| Author native hybrid trunks | For suitable generated trees, replaces the trunk with aligned images while keeping a reduced 3D crown. Off by default. Installed hybrid models take priority. |
| Maximum native trunk texture size | Resolution cap for newly authored trunk images. Default `512`; higher uses more texture memory. |
| Native trunk world units per pixel | Detail of newly authored trunk images before the cap. Default `2`; lower means higher resolution. |
| Reduce redundant leaf cards | Removes overlapping foliage surfaces from selected 3D trees while limiting visible gaps. On by default. |
| Tree geometry target (%) | Percentage of source triangles to aim to retain. Default `65`; lower requests stronger reduction. Coverage limits can retain more. `100` reduces only as needed for the triangle budget. |
| LOD 4 foliage coverage loss (%) | Allowed loss of foliage coverage in sampled near views. Default `1`; higher permits more thinning. `0` removes only redundant coverage. |
| LOD 8 foliage coverage loss (%) | Same limit for the farther 3D band. Default `3`. |
| LOD 4 tree triangle budget | Maximum triangles per near LOD tree model. Default `4000`; `0` removes the cap. Trees that cannot fit within the coverage limits use billboards. |
| LOD 8 tree triangle budget | Maximum triangles per far LOD tree model. Default `1600`; `0` removes the cap. |
| LOD 4 trunk error | Allowed shape deviation when reducing near trunks, in game units. Default `4`; higher permits more simplification. |
| LOD 8 trunk error | Allowed deviation for far trunks. Default `24`. |
| Trees on world map | When using 3D tree LOD, adds billboard versions of those trees to the world map. On by default. |

Tree simplification and triangle budgets apply to selected 3D tree models,
including installed LOD assets. Native hybrid trunk controls apply only when
authoring a missing model. New trunk textures are included in the object LOD
output; original source files remain unchanged.

### Grass LOD

| Setting | What it does |
| --- | --- |
| Grass billboard LOD | Builds distant grass from a cache and grass billboards. On by default; both inputs must be available. |
| Grass density | Fraction of cached placements kept for billboard LOD. Default `0.08` means 8%; Balanced uses `0.05`. Lower reduces distant grass density and cost. |
| Grass size | Scale of retained billboard tufts. Default `1`; larger tufts cover more ground but can overlap more. |
| Grass tip brightness | Brightness at tuft tips. Default `0.85`; lower darkens the upper grass. |
| Grass root brightness | Brightness at tuft bases. Default `0.5`; lower darkens the roots. |
| Community Shaders grass | Generates simpler grass meshes when the Grass Optimizations feature is installed. On by default. This is separate from billboard grass and the game's shader settings. |

Changing Grass density does not rebuild or thin the source cache. It only changes
the grass included in this LOD output.

### World and runtime

| Setting | What it does |
| --- | --- |
| Dynamic objects | Generates LOD that can follow supported quest or enable-state changes. On by default; needs the AnvilLOD SKSE plugin in game. |
| Animated grid objects | Includes nearby distant animations such as windmills, waterfalls and fires. On by default. Uses the SKSE plugin unless supported by game references. |
| Use game references for grid objects | Writes supported animated objects to `AnvilLOD.esp` so the game can display them. On by default. |
| Use game references for effects | Writes supported fires, torches and streams to effects plugins. Off by default. |
| Animated Ships support | Supports the Animated Ships mod. On by default; requires that mod and Use game references for grid objects. |
| Parent-to-city copies | Makes suitable outside-world objects visible from walled cities using the SKSE controller. Off by default. |
| Include child worldspaces | Includes walled-city scenery in the surrounding world's LOD. On by default. |
| Night-time window glow | Adds nighttime windows using installed glow models and the SKSE controller. Off by default. |
| Candle rules | Loads additional supplied candle rules. Off by default; needs the corresponding resources. |
| Glow effect rules | Loads additional supplied fire and light-glow rules. Off by default; needs the corresponding resources. |
| Water stand-ins | Includes lit replacement surfaces for supported streams and ponds. Off by default. Separate from Terrain's water LOD. |
| Seasonal LOD | Builds variants using installed Seasons of Skyrim swaps. Off by default. Shared with Terrain's seasonal switch; grass-cache seasons are selected separately. |
| Include initially disabled objects | Includes objects initially disabled by plugins. Off by default. Enabling it can bake unwanted removed objects into LOD. |
| Include enable-parented objects | Includes objects whose visibility is controlled by another reference. On by default. |
| SKSE DLL installation | **None** means install the matching DLL separately; it does not disable runtime features. **Auto** or an explicit runtime selects a supplied compatible DLL when available. PenguLOD packages do not bundle that DLL. |
| ESL record limit | **Auto** detects the game's limit. `2048` is the older limit; `4096` requires Skyrim 1.6.1130 or newer. Normally leave Auto. |
| Large-reference master | Writes additional eligible large references to `AnvilLOD.esm`, allowing full models to remain visible farther away. Off by default; can add drawing cost. No master is needed when there are no additional eligible entries. |
| Flag large-reference master as ESL | Uses a light plugin slot when the generated master fits. On by default. |

Disable previous generated object LOD mods, including their plugins, before a new
generation. For runtime requirements, see [Provenance](PROVENANCE.md#game-runtime).

### Object atlases

| Setting | What it does |
| --- | --- |
| Pack object texture atlases | Combines compatible object textures into sheets so more surfaces can be drawn together. Off by default. Unsupported surfaces keep separate textures. |
| Atlas cell size | Texture detail allotted to each atlas entry, including its border. Default `256`; smaller values use less texture space but may blur surfaces. |
| Optimize object LOD textures | Creates compressed, size-limited LOD copies for surfaces outside atlases. Off by default. Leaves full-model source textures unchanged. |
| Maximum standalone LOD texture size | Resolution cap for those separate LOD copies. Default `1024`; smaller values save texture memory but can soften large or repeating surfaces. |

Atlas and standalone-texture changes rebuild the selected object blocks. Smaller
files or fewer texture groups do not guarantee higher FPS: visible geometry,
transparency and the rest of the scene also matter. Compare the same viewpoint
before lowering several quality controls together.

### PBR and terrain additions

| Setting | What it does |
| --- | --- |
| PBR object LOD | Uses suitable PBR albedo textures and generated equivalents for object LOD. Off by default. Enable when they match the materials used by the game. |
| PBR brightness | Multiplier on object PBR conversion brightness. Default `1`; separate from TexGen and Terrain calibration. |
| PBR texture size | Maximum side length for converted object PBR textures. Default `512`. |
| PBR waterfalls | Selects full models for PBR-patched waterfalls. In alpha-14, waterfall selection follows PBR object LOD; this checkbox does not independently override it. |
| Terrain underside | Adds a lower terrain surface for volumetric lighting. Off by default; this does not generate the landscape LOD itself. |
| Underside sample step | Spacing used for that surface. Default `16`; smaller values add geometry. |

### Occlusion

Occlusion tells the game which distant blocks terrain hides from a location. This
feature is experimental. Install its plugin last and check hills and valleys for
scenery disappearing too early.

| Setting | What it does |
| --- | --- |
| Generate Occlusion.esp | Builds the visibility plugin. Off by default. |
| Visibility radius (cells) | Maximum distance considered visible. Default `100`; reducing it can cut off distant scenery. |
| Block clearance | Height terrain must rise above the viewing line before it hides a block. Default `3072` game units; higher hides fewer blocks. |
| Parent clearance | The corresponding allowance for larger 8- and 16-cell blocks. Default `3072`. |
| Samples per cell side | Number of test positions along each side of a cell. Default `2`; higher checks more positions and takes longer. |
| Observer height offset | Raises the test viewpoint by this many game units. Default `0`; useful when checking visibility from elevated positions. |
| Parent target points | Number of high points tested on larger blocks. Default `32`; more takes longer and can find additional visible areas. |
| Block target points | Number of target points tested on ordinary blocks. Default `32`; `0` tests every available point. |
| Observer sample points | Number of observer positions used. Default `0` tests every available position; a cap reduces checking work. |

## Diagnostics

Diagnostics checks the stages selected on Pipeline. It does not generate their
outputs or predict game FPS.

| Setting | What it does |
| --- | --- |
| Scan model and billboard coverage | Checks up to 1,000 distinct models in the selected worlds for usable LOD assets. Off by default; adds scanning time. |
| Verify generated file contents | Reads every recorded output file to check integrity. Off by default; can take several minutes on a large generation. |
| Inspect asset precedence (optional) | Shows which mod or archive supplies a game-relative path, such as `meshes\landscape\trees\tree.nif`, and which sources it overrides. |

**Run diagnostics** performs the checks. **Open diagnostic report** opens the
result; **Export diagnostic report** saves a copy. Known personal paths are
replaced in the report, but review it before sharing.

## Preferences

| Setting | What it does |
| --- | --- |
| Texture compute | **Cpu** uses CPU texture encoding. **Auto** uses Vulkan only after a quality check and measured speed advantage. **Vulkan** requests it with a CPU fallback. Applies to supported large opaque textures; foliage and small images still use CPU. |
| Worker threads | Processing workers. Default `0` uses available processors minus one. Lower the count to leave more room for other work; higher does not always finish faster. |
| Run history and cache folder | Location of saved jobs, logs and reusable work. Blank uses the default shown by the app. Changing it can make previous saved jobs unavailable from the new location. |

**Check texture acceleration** tests optional Vulkan encoding. This affects
generation time, not game rendering. Grass collision rejection has its own setting.
**Detect Steam installation** looks for a local Skyrim installation.

**Export settings…** saves source paths and generation choices for comparison or
reuse. **Import settings…** replaces the current settings and backs them up first.
Check imported paths before running on another machine or profile.

The normal settings location is `~/.config/PenguLOD/settings.json` on Linux and
`%APPDATA%\PenguLOD\settings.json` on Windows. Preferences shows the actual path
in use, including a custom location.

## In-game quality controls

The separate AnvilLOD SKSE plugin provides these controls; available menus depend
on its version. They can help compare costs without regenerating.

| Control | What to expect |
| --- | --- |
| Tree, 3D-tree and grass layer boxes | Hide or show the generated layer at each LOD level. Hiding a 3D tree layer does not replace it with billboards. Hidden geometry can remain loaded in memory. |
| Grass density tiers | Show a subset of the already generated grass. They cannot add placements omitted during generation. |
| Dynamic LOD → Enabled | Controls the runtime controller. Turning it off does not remove the static distant world. |
| Save INI | Keeps runtime choices for later sessions. Restore hidden layers before saving if they were only part of a test. |

For a fair comparison, restore the layers you intend to use, restart after
replacing generated files, and test the same camera view and conditions. See the
[walkthrough's testing steps](USER-GUIDE.md#7-compare-in-game).
