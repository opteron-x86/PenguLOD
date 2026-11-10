using PenguLOD.Core.World;

namespace PenguLOD.Plugins;

public sealed record GenerationScopePlan(GenerationPipelineRequest Request, IReadOnlyList<string> Changes)
{
    public bool RequiresExpansion => Changes.Count > 0;
    public string Summary() => RequiresExpansion ? string.Join('\n', Changes)
        + "\nShared masters and atlases require the complete scope. Enable scope expansion or choose a new output folder."
        : "The selected scope covers the existing output. Other stage folders are unchanged.";
}

public static class GenerationScope
{
    public static GenerationScopePlan Review(GenerationPipelineRequest request, CancellationToken ct = default)
    {
        var changes = new List<string>(); var lod = request.Lod; var terrain = request.Terrain; var grass = request.Grass;
        if (grass is not null && Directory.Exists(grass.OutputFolder))
        {
            var scope = Inspect(grass.OutputFolder, "grass");
            var selected = GrassGeneration.SplitWorlds(grass.Worldspaces);
            if (!selected.Contains("all", StringComparer.OrdinalIgnoreCase))
                grass = grass with { Worldspaces = string.Join(",", ExpandWorlds(selected, scope.Worlds, "Grass cache") ?? ["all"]) };
        }
        if (lod?.OutputFolder is { } folder && Directory.Exists(folder))
        {
            var scope = Inspect(folder, "lod");
            var worlds = ExpandWorlds(lod.Scan.Worldspaces, scope.Worlds, "Object LOD");
            var levels = ExpandLevels(lod.Levels, scope.Levels, "Object LOD");
            lod = lod with { Scan = lod.Scan with { Worldspaces = worlds }, Levels = levels };
        }
        if (terrain is not null && Directory.Exists(terrain.OutputFolder))
        {
            var scope = Inspect(terrain.OutputFolder, "terrain");
            terrain = terrain with { Worldspaces = ExpandWorlds(terrain.Worldspaces, scope.Worlds, "Terrain"),
                Levels = ExpandLevels(terrain.Levels, scope.Levels, "Terrain") };
        }
        return new(request with { Lod = lod, Terrain = terrain, Grass = grass }, changes);

        OutputScope Inspect(string root, string stage)
        {
            if (OutputSetReceipt.Exists(root))
            {
                var receipt = OutputSetReceipt.Read(root);
                if (receipt.Stages.Count != 1 || receipt.Stages[0].Id != stage)
                    throw new InvalidDataException("Generate each stage into its own folder. Use Output management to update a combined package.");
                return receipt.Stages[0].Scope;
            }
            return OutputSetReceipt.Scope(OutputFiles.Enumerate(root, ct));
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
