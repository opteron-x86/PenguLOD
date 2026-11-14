using PenguLOD.Core.World;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace PenguLOD.Plugins;

/// <summary>One thing terrain generation lacks. A blocking issue stops the run before any tile is built;
/// the others say what a setting stands in for it.</summary>
public sealed record TerrainInputIssue(string Code, string Worldspace, string Message, bool Blocking);

internal readonly record struct LandTextures(string Diffuse, string? Normal, string? Occlusion);

/// <summary>What the selected worldspaces need, found in one pass over the landscape records before any tile is built:
/// landscape textures that are not installed, and tiles with neither landscape nor existing terrain LOD.</summary>
internal static class TerrainInputs
{
    public const string MissingTexture = "terrain-missing-texture";
    public const string UnresolvedTexture = "terrain-unresolved-texture";
    public const string UncoveredLand = "terrain-uncovered-land";
    public const string MissingDefaultTexture = "terrain-default-texture-missing";
    public const string FillOption = TerrainGeneration.FillUncoveredLandOption;
    public const string SubstituteOption = TerrainGeneration.SubstituteMissingTexturesOption;
    private static readonly LodLevel[] AllLevels = [LodLevel.Lod4, LodLevel.Lod8, LodLevel.Lod16, LodLevel.Lod32];
    // The Creation Kit's value for a worldspace without land defaults.
    private const float UnsetLandHeight = -2048;

    internal static string PathOf(string path)
    { path = GamePath.Normalize(path); return path.StartsWith("textures\\", StringComparison.Ordinal) ? path : "textures\\" + path; }

    /// <summary>The texture files of a landscape texture record, or null when the record, its texture set or its diffuse map is absent.</summary>
    internal static LandTextures? Resolve(ILinkCache cache, FormKey key)
    {
        if (!cache.TryResolve<ILandscapeTextureGetter>(key, out var ltex) || ltex.TextureSet?.FormKeyNullable is not { } set
            || !cache.TryResolve<ITextureSetGetter>(set, out var tx) || tx.Diffuse is not { IsNull: false } diffuse) return null;
        return new(PathOf(diffuse.DataRelativePath.Path),
            tx.NormalOrGloss is { IsNull: false } n ? PathOf(n.DataRelativePath.Path) : null,
            tx.EnvironmentMaskOrSubsurfaceTint is { IsNull: false } ao ? PathOf(ao.DataRelativePath.Path) : null);
    }

    internal static string ExistingMesh(QuadKey tile) => $@"meshes\terrain\{tile.Worldspace}\{tile.Worldspace}.{(int)tile.Level}.{tile.X}.{tile.Y}.btr";
    internal static string ExistingTexture(QuadKey tile) => $@"textures\terrain\{tile.Worldspace}\{tile.Worldspace}.{(int)tile.Level}.{tile.X}.{tile.Y}.dds";

    /// <summary>Tiles holding landscape, plus existing terrain tiles inside the grid when those may be sampled.</summary>
    internal static IEnumerable<QuadKey> PlanTiles(LodGrid grid, IEnumerable<CellCoord> cells, AssetIndex assets, IReadOnlyCollection<LodLevel> levels, bool existing)
    {
        string world = grid.Worldspace; var tiles = new HashSet<QuadKey>();
        foreach (var c in cells) foreach (var level in levels) tiles.Add(grid.QuadFor(c, level));
        if (existing)
            foreach (string path in assets.EnumeratePaths($@"meshes\terrain\{world}\"))
            {
                var parts = GamePath.GetFileName(path).Split('.');
                if (parts.Length == 5 && parts[4] == "btr" && parts[0].Equals(world, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(parts[1], out int level) && levels.Contains((LodLevel)level)
                    && int.TryParse(parts[2], out int x) && int.TryParse(parts[3], out int y) && grid.IsInsideGrid(new(x, y)))
                    tiles.Add(new(world, (LodLevel)level, x, y));
            }
        return tiles.OrderBy(t => (int)t.Level).ThenBy(t => t.X).ThenBy(t => t.Y);
    }

    /// <summary>The height the worldspace gives land without a landscape record, and the water level over it when that is higher.</summary>
    internal static (float Land, float? Water) Defaults(IWorldspaceGetter world)
    {
        static bool Usable(float? v) => v is { } f && float.IsFinite(f) && Math.Abs(f) < 1_000_000;
        float land = Usable(world.LandDefaults?.DefaultLandHeight) ? world.LandDefaults!.DefaultLandHeight : UnsetLandHeight;
        float? water = world.LandDefaults?.DefaultWaterHeight;
        return (land, Usable(water) && water > land ? water : null);
    }

    internal static LodSettings? ReadLodSettings(AssetIndex assets, string world)
    {
        if (!assets.TryOpen(LodSettings.RelativePathFor(world), out var s)) return null;
        using (s) { var bytes = new byte[16]; s.ReadExactly(bytes); return LodSettings.Parse(bytes); }
    }
    internal static bool ValidStride(LodSettings settings) => settings.Stride is >= 4 and <= 4096 && (settings.Stride & (settings.Stride - 1)) == 0;
    internal static bool ValidName(string world) => world.Length > 0 && world.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private sealed class World(string name, LodGrid grid, LodLevel[] levels, float landHeight)
    {
        public string Name => name; public LodGrid Grid => grid; public LodLevel[] Levels => levels; public float LandHeight => landHeight;
        public HashSet<CellCoord> Cells { get; } = [];
        public Dictionary<FormKey, (int Cells, CellCoord First)> Textures { get; } = [];
    }

    /// <summary>Checks every selected worldspace. Reads plugins and the asset index only; no texture or mesh is opened.</summary>
    internal static IReadOnlyList<TerrainInputIssue> Check(GameContext game, AssetIndex assets, TerrainGenerationRequest request, CancellationToken ct = default)
    {
        var materials = request.Materials ?? new();
        string defaultDiffuse = TerrainDefaultTexture.Resolve(request.Game, game.DataFolder);
        var seasons = request.Seasons ? SeasonalAssets.Read(game, assets, "LandTextures") : new Dictionary<string, Dictionary<FormKey, FormKey>>();
        var wanted = request.Worldspaces is { Count: > 0 } ? new HashSet<string>(request.Worldspaces, StringComparer.OrdinalIgnoreCase) : null;
        var worlds = new Dictionary<FormKey, World>();
        foreach (var world in game.LoadOrder.PriorityOrder.Worldspace().WinningOverrides())
        {
            // Worldspaces the run skips or rejects for their own reasons are reported there.
            if (world.IsDeleted || world.EditorID is not { } name || wanted is not null && !wanted.Contains(name) || !ValidName(name)
                || ReadLodSettings(assets, name) is not { } settings || !ValidStride(settings)) continue;
            var levels = (request.Levels ?? AllLevels).Where(settings.Supports).Distinct().ToArray();
            worlds[world.FormKey] = new(name, new(name, settings), levels, Defaults(world).Land);
        }
        if (worlds.Count == 0) return [];

        foreach (var ctx in game.LoadOrder.PriorityOrder.Landscape().WinningContextOverrides(game.LinkCache))
        {
            ct.ThrowIfCancellationRequested();
            if (ctx.Record.IsDeleted || ctx.Record.VertexHeightMap is null
                || !ctx.TryGetParentSimpleContext<ICellGetter>(out var parent) || parent.Record.Grid is not { } point
                || !ctx.TryGetParentSimpleContext<IWorldspaceGetter>(out var ws) || !worlds.TryGetValue(ws.Record.FormKey, out var world)) continue;
            var coord = new CellCoord(point.Point.X, point.Point.Y);
            if (!world.Grid.IsInsideGrid(coord)) continue;
            world.Cells.Add(coord);
            foreach (var key in ctx.Record.Layers.Where(l => l.Header is { Texture.IsNull: false }).Select(l => l.Header!.Texture.FormKey).Distinct())
                world.Textures[key] = world.Textures.TryGetValue(key, out var use)
                    ? (use.Cells + 1, Earlier(use.First, coord)) : (1, coord);
        }

        var issues = new List<TerrainInputIssue>();
        var resolved = new Dictionary<FormKey, LandTextures?>();
        bool defaultNeeded = false;
        foreach (var world in worlds.Values.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (season, swaps) in seasons.Select(s => ((string?)s.Key, (Dictionary<FormKey, FormKey>?)s.Value)).Prepend(((string?)null, (Dictionary<FormKey, FormKey>?)null)))
            {
                // Several records can name one file: report the file once, with every cell that needs it.
                var missing = new SortedDictionary<string, (int Cells, CellCoord First, string Code)>(StringComparer.Ordinal);
                foreach (var (key, use) in world.Textures)
                {
                    var target = swaps?.GetValueOrDefault(key, key) ?? key;
                    if (!resolved.TryGetValue(target, out var textures)) resolved[target] = textures = Resolve(game.LinkCache, target);
                    string? subject = textures is null ? "record " + target
                        : TerrainMaterials.DiffuseAvailable(assets, materials, textures.Value.Diffuse) ? null : textures.Value.Diffuse;
                    if (subject is null || reported.Contains(subject)) continue;
                    missing[subject] = missing.TryGetValue(subject, out var m)
                        ? (m.Cells + use.Cells, Earlier(m.First, use.First), m.Code)
                        : (use.Cells, use.First, textures is null ? UnresolvedTexture : MissingTexture);
                }
                string label = season is null ? world.Name : $"{world.Name} ({season})";
                foreach (var (subject, m) in missing)
                {
                    reported.Add(subject); defaultNeeded |= request.SubstituteMissingTextures;
                    string what = m.Code == UnresolvedTexture ? $"landscape texture {subject} cannot be resolved" : $"landscape texture {subject} is missing";
                    issues.Add(new(m.Code, world.Name, request.SubstituteMissingTextures
                        ? $"{label}: {what}; {Count(m.Cells, "cell")} use the default landscape texture instead (first cell {m.First.X},{m.First.Y})."
                        : $"{label}: {what} and {Count(m.Cells, "cell")} need it (first cell {m.First.X},{m.First.Y}). Install it or turn on \"{SubstituteOption}\".",
                        !request.SubstituteMissingTextures));
                }
            }

            var covered = new Dictionary<CellCoord, bool>(); var tiles = new HashSet<QuadKey>();
            foreach (var tile in PlanTiles(world.Grid, world.Cells, assets, world.Levels, request.UseExistingTerrain))
                for (int y = tile.Y; y < tile.Y + (int)tile.Level; y++)
                    for (int x = tile.X; x < tile.X + (int)tile.Level; x++)
                    {
                        var coord = new CellCoord(x, y);
                        if (world.Cells.Contains(coord)) continue;
                        if (!covered.TryGetValue(coord, out bool ok))
                            covered[coord] = ok = request.UseExistingTerrain
                                && AllLevels.Any(l => assets.Exists(ExistingMesh(world.Grid.QuadFor(coord, l))))
                                && AllLevels.Any(l => assets.Exists(ExistingTexture(world.Grid.QuadFor(coord, l))));
                        if (!ok) tiles.Add(tile);
                    }
            var uncovered = covered.Where(c => !c.Value).Select(c => c.Key).ToArray();
            if (uncovered.Length == 0) continue;
            var first = uncovered.Aggregate(Earlier);
            defaultNeeded |= request.FillUncoveredLand;
            string where = $"{world.Name}: {Count(uncovered.Length, "cell")} in {Count(tiles.Count, "tile")} have neither landscape records nor existing terrain LOD";
            issues.Add(new(UncoveredLand, world.Name, request.FillUncoveredLand
                ? $"{where}. They are filled flat at the worldspace's default land height ({world.LandHeight:0.#}), first cell {first.X},{first.Y}."
                : $"{where} (first cell {first.X},{first.Y}). Turn on \"{FillOption}\" or enable a mod that supplies terrain LOD for the area.",
                !request.FillUncoveredLand));
        }
        if (defaultNeeded && !TerrainMaterials.DiffuseAvailable(assets, materials, defaultDiffuse))
            issues.Add(new(MissingDefaultTexture, "", $"The default landscape texture {defaultDiffuse} is missing, so it cannot stand in for the missing inputs above. "
                + "Restore it or correct sDefaultLandDiffuseTexture in the game INI.", true));
        return issues;

        static CellCoord Earlier(CellCoord a, CellCoord b) => (a.X, a.Y).CompareTo((b.X, b.Y)) <= 0 ? a : b;
        static string Count(int n, string noun) => $"{n:N0} {noun}{(n == 1 ? "" : "s")}";
    }

    /// <summary>The message a run stops with when blocking issues exist: all of them, so one run finds every gap.</summary>
    internal static string Describe(IReadOnlyCollection<TerrainInputIssue> blocking, int limit = 40) =>
        "Terrain inputs are incomplete; nothing was generated." + Environment.NewLine
        + string.Join(Environment.NewLine, blocking.Take(limit).Select(i => "  " + i.Message))
        + (blocking.Count > limit ? Environment.NewLine + $"  and {blocking.Count - limit:N0} more." : "");
}
