using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Textures;

namespace AnvilLOD.Textures;

public sealed record RecipeTextureReplacement(string Diffuse, string? Normal);
public sealed record RecipeRenderedTexture(string Destination, byte[] Bytes, string Kind, string Sources, int Width = 0, int Height = 0);

/// <summary>Evaluates copies, conversions and atlas dependencies without writing to source assets.</summary>
public sealed class TextureRecipeRenderer
{
    private readonly TextureRecipeSet _recipes;
    private readonly IReadOnlyDictionary<string, TextureRecipePlan> _plans;
    private readonly Func<string, byte[]?> _read;
    private readonly Action<RecipeRenderedTexture> _write;
    private readonly Action<string> _warn;
    private readonly IReadOnlyDictionary<string, RecipeTextureReplacement> _replacements;
    private readonly Dictionary<string, byte[]> _built = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _saved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, byte[]?>? _readGenerated;
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _normalOwners = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, TextureRecipePlan> Plans => _plans;

    public TextureRecipeRenderer(TextureRecipeSet recipes, Func<string, byte[]?> read, Action<RecipeRenderedTexture> write,
        Action<string>? warn = null, IReadOnlyDictionary<string, RecipeTextureReplacement>? replacements = null,
        Func<string, byte[]?>? readGenerated = null)
    {
        _recipes = recipes; _plans = recipes.Compile(warn); _read = read; _write = write; _warn = warn ?? (_ => { }); _readGenerated = readGenerated;
        _replacements = replacements ?? new Dictionary<string, RecipeTextureReplacement>();
        foreach (var p in _plans.Values.Where(p => p.Tiles.Count > 0)) _normalOwners.TryAdd(Normal(p.Destination), p.Destination);
    }

    public byte[] Build(string destination)
    {
        RunLimits.Check();
        if (_built.TryGetValue(destination, out var saved)) return saved;
        if (_saved.Contains(destination)) return _readGenerated!(destination) ?? throw new IOException("Generated recipe output disappeared: " + destination);
        if (_active.Count > 256) throw new InvalidDataException("Texture recipe dependency depth exceeds 256: " + destination);
        if (!_active.Add(destination)) throw new InvalidDataException("Cyclic texture recipes: " + destination);
        try
        {
            if (!_plans.TryGetValue(destination, out var plan))
            {
                if (_normalOwners.TryGetValue(destination, out var owner)) { Build(owner); return Saved(destination); }
                throw new FileNotFoundException("Texture recipe is missing: " + destination);
            }
            if (plan.Copy is { } copy)
            {
                var input = Resolve(copy.Source, destination);
                Save(new(destination, input.Bytes, "Copy", input.Path));
            }
            else if (plan.Conversion is { } conversion)
            {
                var input = Resolve(conversion.Source, destination);
                var image = TextureImages.Decode(input.Bytes);
                Save(new(destination, TextureConversionRenderer.Encode(image, conversion, _recipes.Brightness), "Conversion", input.Path, image.Width, image.Height));
            }
            else
            {
                var first = plan.Tiles[0];
                int w = TextureImages.Size(first.AtlasWidth, _recipes.BaseSize), h = TextureImages.Size(first.AtlasHeight, _recipes.BaseSize);
                var image = TextureImages.Blank(w, h); var normal = TextureImages.Blank(w, h, true);
                var inputs = new List<string>();
                foreach (var tile in plan.Tiles)
                {
                    var source = Resolve(tile.Source, destination); inputs.Add(source.Path);
                    TextureImages.Blit(image, TextureImages.Decode(source.Bytes), tile, _recipes.BaseSize, false, _recipes.Brightness);
                    // Explicit normal-only TXST overrides take precedence over the diffuse filename convention.
                    string normalPath = source.NormalOverride ?? Normal(source.Path);
                    var normalBytes = source.NormalOverride is not null ? ReadInput(normalPath, Normal(destination))
                        : source.Path.Equals(destination, StringComparison.OrdinalIgnoreCase) ? _read(normalPath) : BuildNormal(source.Path);
                    if (normalBytes is null)
                    {
                        if (source.NormalOverride is not null) throw new FileNotFoundException("Replacement normal texture missing: " + normalPath);
                        _warn("Used a flat normal map for " + source.Path);
                    }
                    TextureImages.Blit(normal, normalBytes is null ? TextureImages.Blank(4, 4, true) : TextureImages.Decode(normalBytes), tile, _recipes.BaseSize, true);
                }
                bool alpha = plan.Tiles.Any(t => t.Alpha);
                if (!alpha) for (int i = 3; i < image.Pixels.Length; i += 4) image.Pixels[i] = 255;
                Save(new(destination, TextureImages.Encode(image, alpha: alpha), "Stitched", string.Join(", ", inputs.Distinct()), w, h));
                // An explicit normal producer wins regardless of evaluation order.
                if (!_plans.ContainsKey(Normal(destination)))
                    Save(new(Normal(destination), TextureImages.Encode(normal, normal: true), "Normal", string.Join(", ", inputs.Distinct()), w, h));
            }
            return Saved(destination);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new InvalidDataException($"Texture recipe {destination}: {ex.Message}", ex); }
        finally { _active.Remove(destination); }
    }

    public (string Path, byte[] Bytes, string? NormalOverride) Resolve(string alternatives, string current)
    {
        foreach (var path in alternatives.Split('|'))
        {
            var replacement = _replacements.GetValueOrDefault(path);
            if (replacement is not null)
            {
                var bytes = _read(replacement.Diffuse) ?? throw new FileNotFoundException("Replacement diffuse texture missing: " + replacement.Diffuse);
                return (replacement.Diffuse, bytes, replacement.Normal);
            }
            if (ReadInput(path, current) is { } input) return (path, input, null);
        }
        throw new FileNotFoundException("Source texture missing: " + alternatives);
    }

    private byte[]? ReadInput(string path, string current)
    {
        if (!path.Equals(current, StringComparison.OrdinalIgnoreCase) && (_plans.ContainsKey(path) || _normalOwners.ContainsKey(path))) return Build(path);
        // A self-conversion always starts from the installed input, never an earlier generated copy.
        return _read(path);
    }
    public byte[]? BuildNormal(string destination)
    {
        string normal = Normal(destination);
        if (_plans.ContainsKey(normal) || _normalOwners.ContainsKey(normal)) return Build(normal);
        return NormalFor(destination, new(StringComparer.OrdinalIgnoreCase));
    }
    private byte[]? NormalFor(string path, HashSet<string> active)
    {
        if (!active.Add(path)) throw new InvalidDataException("Cyclic normal texture recipes: " + path);
        try
        {
            if (_replacements.TryGetValue(path, out var replacement))
                return _read(replacement.Normal ?? Normal(replacement.Diffuse));
            string normal = Normal(path);
            if (_plans.ContainsKey(normal) || _normalOwners.ContainsKey(normal)) return Build(normal);
            if (_plans.TryGetValue(path, out var plan) && (plan.Copy?.Source ?? plan.Conversion?.Source) is { } alternatives)
            {
                var resolved = Resolve(alternatives, path);
                if (resolved.NormalOverride is { } explicitNormal)
                    return _read(explicitNormal) ?? throw new FileNotFoundException("Replacement normal texture missing: " + explicitNormal);
                string chosen = resolved.Path;
                if (!chosen.Equals(path, StringComparison.OrdinalIgnoreCase)) return NormalFor(chosen, active);
            }
            return _read(normal);
        }
        finally { active.Remove(path); }
    }
    private byte[] Saved(string path) => _readGenerated is null ? _built[path] : _readGenerated(path) ?? throw new IOException("Generated recipe output disappeared: " + path);
    private void Save(RecipeRenderedTexture texture)
    {
        _write(texture);
        if (_readGenerated is null) _built[texture.Destination] = texture.Bytes;
        else _saved.Add(texture.Destination);
    }
    public static string Normal(string diffuse) => diffuse[..^4] + "_n.dds";
}
