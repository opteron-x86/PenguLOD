using System.Text;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Textures;

namespace AnvilLOD.Plugins;

public static partial class TextureGeneration
{
    private sealed record TrunkResource(string Source, Func<byte[]> Load);
    private sealed class TrunkCards
    {
        public Dictionary<int, TrunkView> Views { get; } = [];
        public HashSet<string> Models { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Error { get; set; }
    }

    private static void GenerateTrunks(TextureGenerationRequest request, AssetIndex assets, ResourceCatalog resources,
        Func<string, byte[]?> read, Action<string, byte[]> write, List<TextureGenerationItem> items,
        Action<string> warn, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Finding hybrid tree trunk render resources and matching cards...");
        var models = new Dictionary<string, TrunkResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var (relative, resource) in resources.RenderModels("billboards"))
        {
            ct.ThrowIfCancellationRequested();
            if (relative.EndsWith("_trunk.nif", StringComparison.OrdinalIgnoreCase))
                models[@"textures\" + relative[..^4]] = new(resource.Origin, resource.Read);
        }

        var cards = new Dictionary<string, TrunkCards>(StringComparer.OrdinalIgnoreCase);
        var hybridPaths = assets.EnumeratePaths(@"meshes\dyndolod\lod\trees\")
            .Where(p => p.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        int indexed = 0;
        foreach (var path in hybridPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (indexed++ % 100 == 0) progress?.Report($"Reading hybrid tree cards: {indexed:N0}/{hybridPaths.Length:N0}");
            try
            {
                var mesh = NifGeometryReader.Read(path, read(path) ?? throw new FileNotFoundException(path), passthru: true, renderEffects: true);
                foreach (var part in mesh.Parts)
                {
                    if (part.Material.Textures.Count == 0) continue;
                    string texture = GamePath.Normalize(GamePath.NormalizeTexture(part.Material.Textures[0]));
                    int view = texture.EndsWith("_trunk_1.dds", StringComparison.Ordinal) ? 1
                        : texture.EndsWith("_trunk_2.dds", StringComparison.Ordinal) ? 2 : 0;
                    if (view == 0) continue;
                    string stem = texture[..^6]; // remove _1.dds / _2.dds
                    if (request.ModelFilter.Length > 0 && !stem.Contains(request.ModelFilter, StringComparison.OrdinalIgnoreCase)
                        && !path.Contains(request.ModelFilter, StringComparison.OrdinalIgnoreCase)
                        && !(models.TryGetValue(stem, out var resource) && resource.Source.Contains(request.ModelFilter, StringComparison.OrdinalIgnoreCase))) continue;
                    if (!cards.TryGetValue(stem, out var entry)) cards[stem] = entry = new();
                    entry.Models.Add(path);
                    try
                    {
                        var projection = TrunkView.FromCard(part, view);
                        if (entry.Views.TryGetValue(view, out var previous) && !previous.SameProjection(projection))
                            throw new InvalidDataException("Hybrid models sharing this trunk texture have conflicting crops: " + string.Join(", ", entry.Models));
                        entry.Views[view] = projection;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { entry.Error = ex.Message; }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { warn("Cannot inspect hybrid tree cards in " + path + ": " + ex.Message); }
        }

        int index = 0;
        var loader = TextureLoader(read);
        foreach (var (stem, card) in cards.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            string destination = stem + "_1.dds";
            if (!models.TryGetValue(stem, out var resource))
            {
                bool installed = card.Views.Count > 0 && card.Views.Keys.All(v => read(stem + $"_{v}.dds") is not null && read(stem + $"_{v}_n.dds") is not null);
                string reason = installed ? "No trunk render resource; using installed trunk textures"
                    : "No matching _trunk.nif render resource; install the tree mod's render resources or prebuilt trunk textures";
                items.Add(new("Tree trunk", string.Join("; ", card.Models), destination, "Skipped", reason));
                if (!installed) warn(stem + ": " + reason);
                continue;
            }
            progress?.Report($"Rendering tree trunk {++index}/{cards.Count}: {resource.Source}");
            try
            {
                if (card.Error is not null) throw new NotSupportedException(card.Error);
                if (!card.Views.ContainsKey(1) || !card.Views.ContainsKey(2))
                    throw new InvalidDataException("Both front and side cards are required in the installed hybrid model.");
                var source = NifGeometryReader.Read(resource.Source, resource.Load(), passthru: true, renderEffects: true);
                if (source.Warnings.Count > 0 || source.Parts.Count == 0)
                    throw new NotSupportedException("Incomplete trunk geometry: " + string.Join("; ", source.Warnings.DefaultIfEmpty("no supported shapes")));
                var metadata = new StringBuilder("[LOD]\r\nScale=1.000000\r\n");
                foreach (var (view, frame) in card.Views.OrderBy(p => p.Key))
                {
                    ct.ThrowIfCancellationRequested();
                    var image = frame.Render(source, loader, request.BillboardMaxSize, request.UnitsPerPixel, request.Brightness, request.Materials);
                    string output = stem + $"_{view}.dds";
                    write(output, TextureImages.Encode(new(image.Rgba, image.Width, image.Height), alpha: true));
                    write(stem + $"_{view}_n.dds", TextureImages.Encode(new(image.NormalRgba!, image.Width, image.Height), normal: true));
                    metadata.Append(FormattableString.Invariant($"Width_{view}={frame.Width:F6}\r\nHeight_{view}={frame.Height:F6}\r\nLeft_{view}={frame.Left:F6}\r\nBottom_{view}={frame.Bottom:F6}\r\nRightX_{view}={frame.Right.X:F6}\r\nRightY_{view}={frame.Right.Y:F6}\r\n"));
                    items.Add(new("Tree trunk", resource.Source, output, "Written",
                        (view == 1 ? "Front" : "Side") + " view aligned to installed hybrid cards; geometry normals", image.Width, image.Height));
                }
                metadata.Append("Generator=AnvilLOD native trunk renderer\r\nModel=" + resource.Source + "\r\n");
                write(stem + ".txt", Encoding.UTF8.GetBytes(metadata.ToString()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { items.Add(new("Tree trunk", resource.Source, destination, "Failed", ex.Message)); progress?.Report("ERROR: " + ex.Message); }
        }
        if (cards.Count == 0)
            items.Add(new("Tree trunk", "", "", "Skipped", "No matching hybrid trunk cards found in installed tree LOD models"));
        progress?.Report($"Hybrid trunks: {items.Count(i => i.Kind == "Tree trunk" && i.Status == "Written"):N0} views rendered from {models.Count:N0} available render resources.");
    }
}
