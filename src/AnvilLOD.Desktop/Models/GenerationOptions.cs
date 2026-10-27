using System.Text.Json;

namespace AnvilLOD.Desktop.Models;

public sealed record OptionSpec(string Key, string Group, string Label, string Help, object Default,
    decimal Min = 0, decimal Max = 100, string[]? Choices = null);

public sealed class OptionValue(OptionSpec spec, DesktopSettings settings, Action changed) : ObservableObject
{
    public OptionSpec Spec => spec;
    public bool Flag { get => Read<bool>(); set => Write(value); }
    public decimal Number { get => Read<decimal>(); set => Write(value); }
    public string Choice { get => Read<string>(); set => Write(value); }
    private T Read<T>() => settings.Options.TryGetValue(spec.Key, out var v) ? v.Deserialize<T>()! : JsonSerializer.SerializeToElement(spec.Default).Deserialize<T>()!;
    private void Write<T>(T value) { settings.Options[spec.Key] = JsonSerializer.SerializeToElement(value); Changed(); changed(); }
}

public static class GenerationOptions
{
    // Stable keys make presets portable between releases; every option is validated before a worker starts.
    public static readonly OptionSpec[] All =
    [
        new("preset", "Objects", "Mesh rules preset", "Rule selection when DynDOLOD rules are available.", "High", Choices: ["Low", "Medium", "High"]),
        new("lod4", "Objects", "LOD 4", "Closest object LOD blocks.", true),
        new("lod8", "Objects", "LOD 8", "Middle-distance object LOD blocks.", true),
        new("lod16", "Objects", "LOD 16", "Distant object LOD blocks.", true),
        new("lod32", "Objects", "LOD 32", "World map blocks where the worldspace supports them.", true),
        new("mapLevel", "Objects", "Map object mesh", "Auto detects A Clear Map of Skyrim; -1 uses only explicit LOD32 rules.", -2m, -2, 2),
        new("removeBuried", "Objects", "Remove buried triangles", "Read terrain heights and omit hidden geometry.", true),
        new("removeUnderwater", "Objects", "Remove underwater triangles", "Omit geometry completely below the water surface.", true),
        new("matchTextures", "Objects", "Match full-model textures", "Respect alternate textures and snow variants.", true),
        new("opaqueAlpha", "Objects", "Repair non-opacity alpha", "Create opaque texture copies for solid objects such as ships.", true),
        new("detailedShips", "Objects", "Detailed ships", "Simplify full ship models for LOD4 and LOD8.", true),
        new("objectBrightness", "Objects", "Object brightness", "Vertex colour multiplier; source textures stay untouched.", 1m, 0, 4),
        new("iceInset", "Objects", "Ice inset", "Game units to move glacier LOD inside the full model.", 6m, 0, 100),
        new("mountainFit", "Objects", "Fit mountain surfaces", "Requires a positive mountain inset.", true),
        new("mountainInset", "Objects", "Mountain inset", "Reduce overlap during the LOD transition.", 12m, 0, 100),
        new("objectInset", "Objects", "Other object inset", "Inset buildings, walls and rocks at LOD4/8.", 0m, 0, 100),
        new("snowMode", "Objects", "Snow shader", "0: HD at LOD4; 1: plain everywhere; 2: HD everywhere.", 0m, 0, 2),
        new("snowCover", "Objects", "Snow coverage", "0: mesh colours; 1: full alpha; 2: full alpha and lighter colour.", 1m, 0, 2),
        new("trees", "Trees & grass", "Tree billboards", "Generate tree LOD from installed or rendered billboards.", true),
        new("renderBillboards", "Trees & grass", "Render missing billboards", "Draw missing tree billboards from the full model on the CPU.", true),
        new("treeBrightness", "Trees & grass", "Tree brightness", "Billboard colour multiplier.", 1m, 0, 4),
        new("tree3d", "Trees & grass", "3D tree LOD", "Use matching installed 3D tree LOD models.", false),
        new("tree3d8", "Trees & grass", "3D trees at LOD 8", "Extend 3D tree LOD to the next distance band.", false),
        new("treePlain", "Trees & grass", "Allow plain-name tree models", "Use tree models without the source mesh checksum.", false),
        new("treeNamed", "Trees & grass", "Prefer named tree LOD", "Prefer explicitly named LOD meshes over checksum matches.", false),
        new("mapTrees", "Trees & grass", "Trees on world map", "Include 3D trees in map LOD.", true),
        new("grass", "Trees & grass", "Grass billboard LOD", "Needs an installed grass cache and grass billboards.", true),
        new("grassDensity", "Trees & grass", "Grass density", "Fraction of cached grass instances retained (0–1).", .08m, 0, 1),
        new("grassSize", "Trees & grass", "Grass size", "Scale retained grass tufts.", 1m, .1m, 10),
        new("grassTop", "Trees & grass", "Grass tip brightness", "Vertex colour at tuft tips.", .85m, 0, 4),
        new("grassBottom", "Trees & grass", "Grass root brightness", "Vertex colour at tuft roots.", .5m, 0, 4),
        new("csGrass", "Trees & grass", "Community Shaders grass", "Generate grass meshes when Grass Optimizations is installed.", true),
        new("dynamic", "World & runtime", "Dynamic objects", "Quest-toggled LOD requires AnvilLOD's Windows SKSE plugin in the game.", true),
        new("grid", "World & runtime", "Animated grid objects", "Waterfalls, windmills and fires; requires the SKSE plugin unless backed by native references.", true),
        new("nativeGrid", "World & runtime", "Use game references for grid objects", "Write animated real references to AnvilLOD.esp.", true),
        new("nativeEffects", "World & runtime", "Use game references for effects", "Write fires, torches and streams to effects plugins.", false),
        new("animatedShips", "World & runtime", "Animated Ships support", "Requires native grid objects and the Animated Ships mod.", true),
        new("parentCopies", "World & runtime", "Parent-to-city copies", "SKSE-driven copies of objects visible from walled cities.", false),
        new("childWorlds", "World & runtime", "Include child worldspaces", "Copy walled-city geometry into parent world LOD.", true),
        new("glowWindows", "World & runtime", "Night-time window glow", "Uses installed glow models and the SKSE controller.", false),
        new("candles", "World & runtime", "Candle rules", "Load additional DynDOLOD candle rules.", false),
        new("fxGlow", "World & runtime", "Glow effect rules", "Load additional DynDOLOD fire and light glow rules.", false),
        new("water", "World & runtime", "Water stand-ins", "Lit fake water for stream and pond planes.", false),
        new("seasons", "World & runtime", "Seasonal LOD", "Generate seasonal variants for Seasons of Skyrim.", false),
        new("disabled", "World & runtime", "Include initially disabled objects", "Include static references initially disabled by the game.", false),
        new("parented", "World & runtime", "Include enable-parented objects", "Include references controlled by another reference.", true),
        new("skse", "World & runtime", "SKSE DLL installation", "None: install the matching Windows DLL separately in your mod manager.", "None", Choices: ["None", "Auto", "1.5.97–1.6.1170", "1.7.x", "VR"]),
        new("esl", "World & runtime", "ESL record limit", "Auto reads the game version; 4096 requires Skyrim 1.6.1130 or newer.", "Auto", Choices: ["Auto", "2048", "4096"]),
        new("largeRefs", "World & runtime", "Large-reference master", "Write missing large references to AnvilLOD.esm.", false),
        new("largeEsl", "World & runtime", "Flag large-reference master as ESL", "Avoid using a full plugin slot.", true),
        new("pbr", "PBR & terrain", "PBR object LOD", "Use matching PBR albedo textures and TexGen twins.", false),
        new("pbrBrightness", "PBR & terrain", "PBR brightness", "Multiplier on the default PBR conversion scale.", 1m, 0, 4),
        new("pbrSize", "PBR & terrain", "PBR texture size", "Maximum side length of converted textures.", "512", Choices: ["128", "256", "512", "1024", "2048"]),
        new("pbrWaterfalls", "PBR & terrain", "PBR waterfalls", "Use the full model when a waterfall is PBR-patched.", false),
        new("underside", "PBR & terrain", "Terrain underside", "Add an underside mesh for volumetric lighting. This does not generate terrain LOD.", false),
        new("undersideStep", "PBR & terrain", "Underside sample step", "Smaller steps produce more geometry.", "16", Choices: ["4", "8", "16", "32", "64", "128"]),
        new("occlusion", "Occlusion", "Generate Occlusion.esp", "Calculate visibility with the built-in CPU solver.", false),
        new("occRadius", "Occlusion", "Visibility radius (cells)", "Maximum distance considered visible.", 100m, 1, 256),
        new("occClearance", "Occlusion", "Block clearance", "Larger values hide fewer blocks behind terrain.", 3072m, 0, 16384),
        new("occParent", "Occlusion", "Parent clearance", "Clearance for 8- and 16-cell quads.", 3072m, 0, 16384),
        new("occSamples", "Occlusion", "Samples per cell side", "Higher values increase work and precision.", 2m, 1, 4),
        new("occEye", "Occlusion", "Observer height offset", "Additional eye height above the terrain.", 0m, 0, 4096),
        new("occParentTargets", "Occlusion", "Parent target points", "Number of high sample points tried for parent quads.", 16m, 1, 64),
        new("occBlockTargets", "Occlusion", "Block target points", "0 tries every sample point.", 8m, 0, 64),
        new("occEyes", "Occlusion", "Observer sample points", "0 tries every observer sample.", 2m, 0, 16)
    ];

    public static T Get<T>(DesktopSettings settings, string key)
    {
        var spec = All.Single(o => o.Key == key);
        return (settings.Options.TryGetValue(key, out var value) ? value : JsonSerializer.SerializeToElement(spec.Default)).Deserialize<T>()!;
    }

    public static void Validate(DesktopSettings settings)
    {
        foreach (var spec in All)
        {
            try
            {
                if (spec.Default is bool) _ = Get<bool>(settings, spec.Key);
                else if (spec.Default is decimal)
                {
                    var value = Get<decimal>(settings, spec.Key);
                    if (value < spec.Min || value > spec.Max) throw new ArgumentException($"must be between {spec.Min} and {spec.Max}");
                }
                else if (!spec.Choices!.Contains(Get<string>(settings, spec.Key))) throw new ArgumentException("has an unknown choice");
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
            { throw new ArgumentException($"{spec.Label}: {e.Message}", e); }
        }
        if (settings.Edition is not ("SE / AE" or "VR")) throw new ArgumentException("Choose a supported game edition.");
        if (settings.SourceMode is not ("Mod Organizer 2" or "Data folder / Vortex")) throw new ArgumentException("Choose a supported game source.");
        if (settings.MaxThreads < 0 || settings.MaxThreads > 1024 || settings.MaxThreads != decimal.Truncate(settings.MaxThreads))
            throw new ArgumentException("Worker threads must be a whole number from 0 to 1024.");
    }
}
