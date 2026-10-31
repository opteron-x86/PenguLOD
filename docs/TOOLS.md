# PenguLOD tool reference

These optional pages create or repair source assets. You can complete a normal
generation without using them. Follow the [walkthrough](USER-GUIDE.md) for that
workflow; see the [settings reference](SETTINGS.md) for generation controls.

## Mesh maker

Use Mesh maker to create simpler LOD models from full NIF models. Install the
result as a source mod, inspect it, then regenerate object LOD.

| Setting | What it does |
| --- | --- |
| Input NIF files or folders · one per line | Models to simplify. **Add folder** and **Add NIF files** fill this list. |
| Mesh output folder | Destination for the new models and optional rules. Keep it separate from the source. |
| Group name | Name used to group the generated assets. Use a simple name without folder separators. |
| Owner plugin | Plugin filename, such as `MyMod.esp`, associated with the generated rule file. |
| Mesh levels | Comma-separated model levels to create. `0,1,2` normally serve LOD4, LOD8 and LOD16; mesh rules can choose different mappings. |
| Detail reduction | Simplification multiplier. Default `1`; lower retains more detail, higher allows more reduction. |
| Minimum model size | Skips models whose largest dimension is below this many game units. `0` disables the size filter. |
| Triangle budget | **Recommended** chooses limits suited to the model. **Off** removes the triangle cap. **Custom** uses the three limits below. |
| Custom budgets | Three triangle limits for levels 0, 1 and 2. `0` means unlimited at that level. Applies only with Custom selected. |
| Write mesh rules | Saves rules that tell generation to use the new models. On by default. |
| Replace existing generated files | Allows this tool to replace files already in its output folder. Off by default. |

**Make LOD meshes** runs the tool. Review the result for missing parts, poor
silhouettes and texture problems before using or distributing it. A low triangle
count alone does not make a suitable LOD model.

### Convert Oldrim meshes to SSE

This converts supported older Skyrim meshes for SE/AE use. It does not convert a
whole mod's plugins, scripts or other files.

| Setting | What it does |
| --- | --- |
| Source mod folder | Folder containing the older meshes, loose or in supported archives. |
| Converted output folder | Destination for converted copies. Keep separate from the source mod. |
| Convert all meshes (otherwise LOD meshes only) | Broadens conversion beyond LOD models. Off by default. |
| Replace existing converted files | Allows replacement of converted files in the destination. Off by default. |

Choose **Convert meshes**, review the results, then install the converted output
so it wins conflicts with the older meshes.

## Mod author

Mod author finds assets through the game source selected on Overview. Use it to
fill gaps in a particular mod's LOD resources.

| Setting | What it does |
| --- | --- |
| Plugin | Plugin filename to inspect, or `*` to find missing tree billboards across the load order. |
| Authoring output folder | Destination for generated source assets. Keep it separate from the final LOD output. |
| Minimum object size | Skips objects below this size in game units. Default `400`. |
| Minimum tree height | Skips billboard rendering for trees below this height in game units. Default `256`. |
| Create LOD meshes | Creates suitable missing object LOD models. On by default. |
| Render tree billboards | Creates missing tree images from source models. On by default. |
| Write mesh rules | Saves rules for the generated models. On by default. |
| Create stand-ins for missing tree models | Allows substitute tree assets where source models are missing. Off by default. Review these carefully: they cannot reproduce a missing original. |
| Replace generated files | Allows replacement of existing files in the authoring output. Off by default. |

**Analyze only** reports coverage without generating assets. **Generate assets**
creates the selected outputs. Install suitable results as a source mod before
the next generation. See [model support](MODEL-SUPPORT.md) for supported inputs.

## Texture tools

Use this page when a source mod contains BC6H DDS textures that need a compatible
LOD copy. It creates an override mod and leaves the originals in place.

| Setting | What it does |
| --- | --- |
| Input mod folders · one per line | Folders to scan for loose DDS files. **Add folder** adds a location. |
| Also scan all enabled MO2 mod folders | Adds the enabled mods from the selected instance. Off by default. |
| Repaired texture output | Destination for repaired texture copies. Keep separate from the scanned mods. |

**Scan textures** lists problems. **Repair BC6H textures** converts supported
files to BC7 while preserving their dimensions and mip levels. Mip levels are the
smaller versions of a texture used at a distance. HDR colour values outside the
normal range are clipped during conversion.

The repair scans loose files; it does not repair textures inside BSA archives or
unsupported cube, array or volume textures. Install the repair mod so its files
win conflicts with the originals, then regenerate affected outputs.

## Custom mesh rules

Rules choose which model appears at each distance. Custom rules take precedence
over the normal rule packs. Rules for a specific FormID take priority over model
path matches; otherwise the first matching rule wins. The catch-all `\` rule is
saved last.

| Column | What it does |
| --- | --- |
| Mask / FormID | A matching part of a model path, such as `\architecture\mymod\`, or a specific object ID such as `MyMod.esp;00000800`. A broad path can affect many models. |
| LOD4 | Model choice for the nearest LOD band. |
| LOD8 | Model choice for the next band. |
| LOD16 | Model choice for distant LOD. |
| LOD32 | Model choice for world-map LOD. |
| Grid | Distance behaviour for an animated object whose static LOD columns are all empty. See choices below. |
| Reference | Preserves imported reference-handling text. Alpha-14 does not apply these reference-edit actions; leave **Unchanged** for a new rule. |
| Flags | Preserves imported flags: `1` VWD, `2` NoGlow, `4` NoMATO, `8` Dynamic, `16` TREE. Alpha-14 does not apply these flags as generation overrides; leave `0` for a new rule. |
| Description | Your note explaining the rule. Does not affect output. |

For the four LOD columns:

- **None** omits the object from that band.
- **Level0–Level3** requests a supplied or generated LOD model level.
- **Full model** uses the full source model. Apply narrowly: repeated detailed
  models can be expensive at a distance.
- **Billboard1–Billboard6** are accepted billboard choices, but alpha-14 treats
  them as the same billboard request. They do not select six distinct view styles;
  usable billboard resources are still needed.

For **Grid**, **None** adds no animated grid object. **Near LOD** uses the nearby
distant grid; **Far LOD** extends it farther; **Far Full** requests the full model
there; **Never Fade LOD** requests visibility at any distance. These choices depend
on suitable resources and the selected runtime features.

Use **Load rule file** to inspect an existing file, **Add rule** to create a row,
and **Remove selected** to remove one. **Save rules as…** saves the edited list and
selects it as the Custom rule file for generation. Only LOD rule entries are
written, so retain the original if it also contains other settings.

To remove an object from static LOD, use **None** in its LOD columns or correct
its placement in the source load order. A Reference value such as Disable or
Delete is not a substitute for that in alpha-14.
