using PenguLOD.Core.Paths;
using PenguLOD.Core.World;

namespace PenguLOD.Plugins;

public sealed record GenerationScopePlan(GenerationPipelineRequest Request, IReadOnlyList<string> Changes)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool RequiresExpansion => Changes.Count > 0;
    public string Summary() => GenerationScopeReport.Describe(Changes, Warnings);
}

/// <summary>Only the output paths and coverage needed for a read-only scope review.</summary>
public sealed record GenerationScopeStage(string Id, string OutputFolder,
    IReadOnlyCollection<string>? Worldspaces = null, IReadOnlyCollection<LodLevel>? Levels = null);

public sealed record GenerationScopeReport(IReadOnlyList<GenerationScopeStage> Stages,
    IReadOnlyList<string> Changes, IReadOnlyList<string> Warnings)
{
    public bool RequiresExpansion => Changes.Count > 0;
    public string Summary() => Describe(Changes, Warnings);
    internal static string Describe(IReadOnlyList<string> changes, IReadOnlyList<string> warnings)
    {
        string scope = changes.Count > 0 ? string.Join('\n', changes)
            + "\nShared masters and atlases require the complete scope. Enable scope expansion or choose a new output folder."
            : "The selected worlds and levels cover the existing output.";
        return (warnings.Count > 0 ? string.Join('\n', warnings) + "\n\n" : "") + scope
            + "\nScope review does not change or clear output folders.";
    }
}

public static class GenerationScope
{
    public static GenerationScopePlan Review(GenerationPipelineRequest request, CancellationToken ct = default)
    {
        var stages = new List<GenerationScopeStage>();
        if (request.Grass is { } grass)
            stages.Add(new("grass", grass.OutputFolder, GrassGeneration.SplitWorlds(grass.Worldspaces)));
        if (request.Textures is { } textures) stages.Add(new("textures", textures.OutputFolder));
        if (request.Terrain is { } terrain) stages.Add(new("terrain", terrain.OutputFolder, terrain.Worldspaces, terrain.Levels));
        if (request.Lod?.OutputFolder is { } folder) stages.Add(new("lod", folder, request.Lod.Scan.Worldspaces, request.Lod.Levels));
        var report = ReviewOutputs(stages, ct);
        var expanded = request;
        foreach (var stage in report.Stages)
        {
            if (stage.Id == "grass") expanded = expanded with { Grass = request.Grass! with { Worldspaces = string.Join(",", stage.Worldspaces ?? ["all"]) } };
            if (stage.Id == "terrain") expanded = expanded with { Terrain = request.Terrain! with { Worldspaces = stage.Worldspaces, Levels = stage.Levels } };
            if (stage.Id == "lod") expanded = expanded with { Lod = request.Lod! with { Scan = request.Lod.Scan with { Worldspaces = stage.Worldspaces }, Levels = stage.Levels } };
        }
        return new(expanded, report.Changes) { Warnings = report.Warnings };
    }

    public static GenerationScopeReport ReviewOutputs(IReadOnlyList<GenerationScopeStage> stages, CancellationToken ct = default)
    {
        if (stages.Count == 0) throw new ArgumentException("Select at least one generation stage to review.");
        var changes = new List<string>(); var warnings = new List<string>(); var expanded = new List<GenerationScopeStage>();
        foreach (var stage in stages)
        {
            ct.ThrowIfCancellationRequested();
            string label = stage.Id switch { "grass" => "Grass cache", "textures" => "Textures", "terrain" => "Terrain", "lod" => "Object LOD", _ => throw new ArgumentException("Unknown generation stage: " + stage.Id) };
            if (string.IsNullOrWhiteSpace(stage.OutputFolder)) throw new ArgumentException("Choose the " + label + " output folder.");
            OutputFiles.RequireRealPath(stage.OutputFolder);
            if (File.Exists(stage.OutputFolder)) throw new IOException(label + " output points to a file, not a folder: " + stage.OutputFolder);
            if (!Directory.Exists(stage.OutputFolder)) { expanded.Add(stage); continue; }
            var legacy = ProductPaths.LegacyOutputFiles(stage.OutputFolder);
            if (legacy.Length > 0) warnings.Add(label + ": " + ProductPaths.LegacyOutputMessage(stage.OutputFolder, legacy));
            var scope = Inspect(stage.OutputFolder, stage.Id);
            if (stage.Id == "textures") { expanded.Add(stage); continue; }
            var selected = stage.Id == "grass" && stage.Worldspaces?.Contains("all", StringComparer.OrdinalIgnoreCase) == true ? null : stage.Worldspaces;
            expanded.Add(stage with { Worldspaces = ExpandWorlds(selected, scope.Worlds, label), Levels = ExpandLevels(stage.Levels, scope.Levels, label) });
        }
        return new(expanded, changes, warnings);

        OutputScope Inspect(string root, string stage)
        {
            if (OutputSetReceipt.Exists(root))
            {
                var receipt = OutputSetReceipt.Read(root);
                if (receipt.Stages.Count != 1 || receipt.Stages[0].Id != stage)
                    throw new InvalidDataException("Generate each stage into its own folder. Use Output management to update a combined package.");
                return receipt.Stages[0].Scope;
            }
            return stage == "textures" ? new([], [], []) : OutputSetReceipt.Scope(OutputFiles.Enumerate(root, ct));
        }
        IReadOnlyCollection<string>? ExpandWorlds(IReadOnlyCollection<string>? selected, string[] previous, string label)
        {
            if (selected is null || selected.Count == 0 || selected.Contains("*")) return null;
            var missing = previous.Except(selected, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length == 0) return selected;
            changes.Add(label + " also needs worlds: " + string.Join(", ", missing));
            return selected.Union(previous, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        IReadOnlyCollection<LodLevel>? ExpandLevels(IReadOnlyCollection<LodLevel>? selected, int[] previous, string label)
        {
            if (selected is null || previous.All(p => selected.Contains((LodLevel)p))) return selected;
            changes.Add(label + " also needs levels: " + string.Join(", ", previous.Where(p => !selected.Contains((LodLevel)p))));
            return selected.Union(previous.Select(p => (LodLevel)p)).Order().ToArray();
        }
    }
}
