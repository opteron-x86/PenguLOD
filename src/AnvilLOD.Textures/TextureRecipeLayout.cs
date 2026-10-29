using System.Numerics;
using System.Text.Json;
using AnvilLOD.Core.Textures;

namespace AnvilLOD.Textures;

public sealed record RecipePlacement(string Source, Vector2 Scale, Vector2 Offset, Vector2 Min, Vector2 Max, string Color);
public sealed record RecipeUvMapping(string Destination, Vector2 Scale, Vector2 Offset);

/// <summary>Source provenance through copy, conversion and nested stitching recipes, including cropped tiles.</summary>
public sealed class TextureRecipeLayout
{
    private readonly TextureRecipeSet _set;
    private readonly IReadOnlyDictionary<string, TextureRecipePlan> _plans;
    private readonly Func<string, bool> _exists;
    private readonly Dictionary<string, IReadOnlyList<RecipePlacement>> _cache = new(StringComparer.OrdinalIgnoreCase);
    public TextureRecipeLayout(TextureRecipeSet set, Func<string, bool> exists)
    { _set = set; _plans = set.Compile(); _exists = exists; }

    public IReadOnlyList<RecipePlacement> Placements(string destination) => Flatten(destination, new(StringComparer.OrdinalIgnoreCase));

    private IReadOnlyList<RecipePlacement> Flatten(string path, HashSet<string> active)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;
        if (active.Count > 256) throw new InvalidDataException("Texture recipe dependency depth exceeds 256: " + path);
        if (!active.Add(path)) throw new InvalidDataException("Cyclic texture recipes: " + path);
        try
        {
            if (!_plans.TryGetValue(path, out var plan)) return [new(path, Vector2.One, Vector2.Zero, Vector2.Zero, Vector2.One, "")];
            IReadOnlyList<RecipePlacement> Input(string alternatives)
            {
                string? chosen = alternatives.Split('|').FirstOrDefault(p => _plans.ContainsKey(p) || _exists(p));
                if (chosen is null) throw new FileNotFoundException("Source texture missing: " + alternatives);
                return chosen.Equals(path, StringComparison.OrdinalIgnoreCase)
                    ? [new(chosen, Vector2.One, Vector2.Zero, Vector2.Zero, Vector2.One, "")] : Flatten(chosen, active);
            }
            var result = new List<RecipePlacement>();
            if (plan.Copy is { } copy) result.AddRange(Input(copy.Source));
            else if (plan.Conversion is { } conversion)
                result.AddRange(Input(conversion.Source).Select(p => p with { Color = p.Color + JsonSerializer.Serialize(conversion with { Source = "", Destination = "" }) }));
            else foreach (var tile in plan.Tiles)
            {
                var size = new Vector2(TextureImages.Size(tile.AtlasWidth, _set.BaseSize), TextureImages.Size(tile.AtlasHeight, _set.BaseSize));
                var scale = new Vector2(MathF.Round(tile.Width * _set.BaseSize), MathF.Round(tile.Height * _set.BaseSize)) / size;
                var offset = new Vector2(MathF.Round(tile.X * _set.BaseSize), MathF.Round(tile.Y * _set.BaseSize)) / size;
                foreach (var input in Input(tile.Source))
                {
                    var min = Vector2.Max(Vector2.Zero, input.Min * scale + offset);
                    var max = Vector2.Min(Vector2.One, input.Max * scale + offset);
                    if (min.X >= max.X || min.Y >= max.Y) continue;
                    result.Add(new(input.Source, input.Scale * scale, input.Offset * scale + offset, min, max,
                        input.Color + FormattableString.Invariant($"|{tile.Brightness}:{tile.Red:R}:{tile.Green:R}:{tile.Blue:R}:{tile.Alpha}")));
                }
            }
            return _cache[path] = result;
        }
        finally { active.Remove(path); }
    }

    public IReadOnlyList<RecipeUvMapping> Find(string source, Vector2 min, Vector2 max)
    {
        var result = new List<RecipeUvMapping>();
        foreach (var destination in _plans.Keys.Where(p => p.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase)))
        {
            if (!References(destination, source, new(StringComparer.OrdinalIgnoreCase))) continue;
            var placements = Placements(destination);
            foreach (var anchor in placements.Where(p => p.Source.Equals(source, StringComparison.OrdinalIgnoreCase)))
            {
                var lo = min * anchor.Scale + anchor.Offset; var hi = max * anchor.Scale + anchor.Offset;
                if (lo.X < -1e-5 || lo.Y < -1e-5 || hi.X > 1.00001 || hi.Y > 1.00001) continue;
                // Split at every tile edge. The LAST covering tile must be the same source,
                // with the same color operation and an integer repeat of the same UV scale.
                // This also rejects overwritten portions and gaps in a repeated layout.
                var xs = Cuts(placements.SelectMany(p => new[] { p.Min.X, p.Max.X }), lo.X, hi.X);
                var ys = Cuts(placements.SelectMany(p => new[] { p.Min.Y, p.Max.Y }), lo.Y, hi.Y);
                bool valid = true;
                for (int y = 0; y < ys.Length - 1 && valid; y++)
                    for (int x = 0; x < xs.Length - 1 && valid; x++)
                    {
                        var center = new Vector2((xs[x] + xs[x + 1]) / 2, (ys[y] + ys[y + 1]) / 2);
                        var tile = placements.LastOrDefault(p => center.X >= p.Min.X && center.Y >= p.Min.Y && center.X <= p.Max.X && center.Y <= p.Max.Y);
                        valid = tile is not null && tile.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && tile.Color == anchor.Color
                            && Vector2.DistanceSquared(tile.Scale, anchor.Scale) < 1e-10
                            && Integer((tile.Offset.X - anchor.Offset.X) / anchor.Scale.X) && Integer((tile.Offset.Y - anchor.Offset.Y) / anchor.Scale.Y);
                    }
                if (valid) result.Add(new(destination, anchor.Scale, anchor.Offset));
            }
        }
        return result.Distinct().ToArray();
    }
    private static bool Integer(float value) => MathF.Abs(value - MathF.Round(value)) < 1e-5;
    private static float[] Cuts(IEnumerable<float> edges, float min, float max) => min == max ? [min, max]
        : edges.Where(v => v > min && v < max).Append(min).Append(max).Distinct().Order().ToArray();
    private bool References(string path, string source, HashSet<string> visited)
    {
        if (path.Equals(source, StringComparison.OrdinalIgnoreCase)) return true;
        if (!visited.Add(path) || !_plans.TryGetValue(path, out var plan)) return false;
        if (visited.Count > 256) return false;
        var inputs = plan.Copy is { } copy ? new[] { copy.Source } : plan.Conversion is { } conversion ? [conversion.Source] : plan.Tiles.Select(t => t.Source);
        return inputs.SelectMany(p => p.Split('|')).Any(p => References(p, source, visited));
    }
}
