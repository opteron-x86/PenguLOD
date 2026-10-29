using System.Text.Json;
using System.Text.Json.Serialization;
using AnvilLOD.Core.Pipeline;

namespace AnvilLOD.Core.Textures;

public sealed record TextureRecipeEntry(string File, int Line, TextureCopy? Copy = null,
    TextureTile? Tile = null, TextureConversion? Conversion = null)
{
    public string Destination => Copy?.Destination ?? Tile?.Destination ?? Conversion?.Destination
        ?? throw new InvalidDataException("Empty texture recipe.");
}

public sealed record TextureRecipePlan(string Destination, TextureCopy? Copy, TextureConversion? Conversion,
    IReadOnlyList<TextureTile> Tiles, string Origin);

/// <summary>Ordered resource instructions. The same program prepares textures and rebuilds record-specific atlases.</summary>
public sealed class TextureRecipeSet
{
    public List<TextureRecipeEntry> Entries { get; set; } = [];
    public int BaseSize { get; set; } = 256;
    public float Brightness { get; set; } = 1;
    [JsonIgnore] public string Fingerprint => OutputSnapshot.HashText(JsonSerializer.Serialize(this));

    public IReadOnlyDictionary<string, TextureRecipePlan> Compile(Action<string>? warn = null)
    {
        if (BaseSize is < 4 or > 1024 || !float.IsFinite(Brightness) || Brightness is < .1f or > 4 || Entries is null || Entries.Count > 100000)
            throw new InvalidDataException("Invalid saved texture recipe settings.");
        var plans = new Dictionary<string, TextureRecipePlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            if (entry is null || (entry.Copy is null ? 0 : 1) + (entry.Tile is null ? 0 : 1) + (entry.Conversion is null ? 0 : 1) != 1)
                throw new InvalidDataException("A texture recipe must have exactly one operation.");
            string destination = TextureRecipe.AssetPath(entry.Destination);
            string origin = entry.File + ":" + entry.Line;
            if (entry.Copy is { } copy)
            {
                copy = new(TextureRecipe.Sources(copy.Source), destination);
                plans[destination] = new(destination, copy, null, [], origin);
            }
            else if (entry.Conversion is { } conversion)
            {
                // Reuse the text validator for untrusted saved metadata as well.
                conversion = TextureRecipe.ParseConversion(FormattableString.Invariant($"{conversion.Brightness}\t{conversion.Red}\t{conversion.Green}\t{conversion.Blue}\t{conversion.Source}\tx\t{conversion.Filter}\t{conversion.Contrast}\t{conversion.MipBrightness}\t{conversion.Destination}\t{conversion.StartLevel}\tx"), conversion.Alpha);
                plans[destination] = new(destination, null, conversion, [], origin);
            }
            else if (entry.Tile is { } tile)
            {
                tile = TextureRecipe.ParseTile(FormattableString.Invariant($"{tile.Brightness}\t{tile.Red}\t{tile.Green}\t{tile.Blue}\t{tile.Source}\t{tile.Width}\t{tile.Height}\t{tile.X}\t{tile.Y}\t{tile.Destination}\t{tile.AtlasWidth}\t{tile.AtlasHeight}"), tile.Alpha);
                var previous = plans.GetValueOrDefault(destination);
                if (previous is { Tiles.Count: > 0 } && (previous.Tiles[0].AtlasWidth != tile.AtlasWidth || previous.Tiles[0].AtlasHeight != tile.AtlasHeight))
                    warn?.Invoke("Conflicting atlas sizes for " + destination + "; using the final recipe dimensions.");
                var tiles = previous is { Tiles.Count: > 0 } && previous.Tiles[0].AtlasWidth == tile.AtlasWidth && previous.Tiles[0].AtlasHeight == tile.AtlasHeight
                    ? previous.Tiles.Append(tile).ToArray() : [tile];
                plans[destination] = new(destination, null, null, tiles, origin);
            }
        }
        return plans;
    }
}
