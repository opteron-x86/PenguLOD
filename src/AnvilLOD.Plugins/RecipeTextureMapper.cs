using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnvilLOD.Core.Lod;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Textures;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Textures;

namespace AnvilLOD.Plugins;

/// <summary>Applies recipe UV layouts and builds isolated atlases for record/season texture sets.</summary>
public sealed class RecipeTextureMapper
{
    private readonly TextureRecipeSet _recipes;
    private readonly string _recipeFingerprint;
    private readonly IReadOnlyDictionary<string, TextureRecipePlan> _plans;
    private readonly TextureRecipeLayout _layout;
    private readonly IAssetSource _assets;
    private readonly Func<string, byte[]?> _read;
    private readonly string _output;
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Diffuse, string? Normal)> _variants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MappingReport> _report = new(StringComparer.Ordinal);
    public sealed record MappingReport(string Shape, string Source, string Destination, string Status, string Detail);
    public IReadOnlyList<MappingReport> Report { get { lock (_gate) return _report.Values.OrderBy(r => r.Source).ThenBy(r => r.Shape).ToArray(); } }

    public RecipeTextureMapper(TextureRecipeSet recipes, IAssetSource assets, string output, Func<string, byte[]?>? readSource = null)
    {
        _recipes = recipes; _plans = recipes.Compile(); _recipeFingerprint = recipes.Fingerprint; _assets = assets; _output = output;
        _read = readSource ?? ReadAsset;
        _layout = new(recipes, assets.Exists);
    }
    private byte[]? ReadAsset(string path)
    {
        if (!_assets.TryOpen(path, out var stream)) return null;
        using (stream) { using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray(); }
    }
    private string Hash(string path)
    {
        if (_hashes.TryGetValue(path, out var hash)) return hash;
        RunLimits.Check();
        return _hashes[path] = _read(path) is { } bytes ? Convert.ToHexString(SHA256.HashData(bytes)) : "missing";
    }

    // Content hashes, including missing alternatives, invalidate cached quads when a source
    // changes in place, appears, disappears, or is overridden by another archive/mod.
    public string InputFingerprint(IEnumerable<TextureVariant> variants)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _recipes.Entries)
            foreach (string source in (entry.Copy?.Source ?? entry.Tile?.Source ?? entry.Conversion!.Source).Split('|'))
            { paths.Add(source); paths.Add(TextureRecipeRenderer.Normal(source)); }
        foreach (var variant in variants)
        {
            paths.Add(GamePath.Normalize(variant.FullModel.StartsWith("meshes", StringComparison.OrdinalIgnoreCase) ? variant.FullModel : "meshes\\" + variant.FullModel));
            foreach (var alt in variant.Alternates)
                foreach (var texture in new[] { alt.Diffuse, alt.Normal }.OfType<string>().Where(p => p.Length > 0)) paths.Add(GamePath.NormalizeTexture(texture));
            if (ReadAsset(variant.FullModel) is { } model)
                try
                {
                    foreach (var texture in NifGeometryReader.Read(variant.FullModel, model).Parts.SelectMany(p => p.Material.Textures.Take(2)).Where(p => !string.IsNullOrWhiteSpace(p)))
                        paths.Add(GamePath.NormalizeTexture(texture));
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { /* The mesh loader reports malformed models. */ }
        }
        return OutputSnapshot.HashText(_recipeFingerprint + "\n" + string.Join('\n', paths.Order(StringComparer.OrdinalIgnoreCase).Select(p => p + "=" + Hash(p))));
    }

    public LodMeshPart? Map(LodMeshPart part, IReadOnlyList<TextureMatchInput> inputs)
    {
        lock (_gate)
        {
            string source = GamePath.NormalizeTexture(part.Material.Textures[0]);
            try { return MapCore(part, inputs, source); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { Record(part, source, "", "Failed", ex.Message); throw new InvalidDataException($"Recipe mapping for {part.Name} ({source}): {ex.Message}", ex); }
        }
    }
    private LodMeshPart? MapCore(LodMeshPart part, IReadOnlyList<TextureMatchInput> inputs, string source)
    {
        bool known = _plans.ContainsKey(source);
        string destination = source;
        Vector2 scale = Vector2.One, offset = Vector2.Zero;
        if (!known)
        {
            if (part.UVs.Length == 0 || part.UVs.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) return null;
            if (part.Material.Passthru is { } pt && (pt.UvScaleX != 1 || pt.UvScaleY != 1 || pt.UvOffsetX != 0 || pt.UvOffsetY != 0))
            { Record(part, source, source, "Retained", "Shader UV transform requires a separate full texture."); return null; }
            if (part.Material.Textures.Skip(2).Any(p => !string.IsNullOrWhiteSpace(p)))
            { Record(part, source, source, "Retained", "Additional texture channels require their original UVs."); return null; }
            var min = part.UVs.Aggregate(Vector2.Min); var max = part.UVs.Aggregate(Vector2.Max);
            RecipeUvMapping[] mappings;
            try { mappings = _layout.Find(source, min, max).Where(p => !p.Destination.Equals(source, StringComparison.OrdinalIgnoreCase)).ToArray(); }
            catch (FileNotFoundException ex)
            { Record(part, source, source, "Retained", ex.Message + "; retained the full texture."); return null; }
            if (mappings.Length == 0) return null;
            if (mappings.Length > 1)
            { Record(part, source, source, "Retained", "Several recipe layouts cover these UVs; kept the full texture to avoid an ambiguous remap."); return null; }
            destination = mappings[0].Destination; scale = mappings[0].Scale; offset = mappings[0].Offset;
        }
        var placements = _layout.Placements(destination);
        var contributors = placements.Select(p => p.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, RecipeTextureReplacement>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            string original = GamePath.NormalizeTexture(input.Diffuse);
            string diffuse = GamePath.NormalizeTexture(input.DesiredDiffuse);
            string? normal = string.IsNullOrWhiteSpace(input.DesiredNormal) ? null : GamePath.NormalizeTexture(input.DesiredNormal);
            bool changed = original != diffuse || !Same(input.Normal, normal);
            if (!contributors.Contains(original))
            {
                // A patched full model can name a new texture without a record TXST.
                // Associate it only when the full-texture input or the shape's complete
                // atlas UV rectangle identifies exactly one original source.
                string? inferred = !known && inputs.Count == 1 && contributors.Contains(source) ? source : null;
                if (inferred is null && inputs.Count == 1 && part.UVs.Length > 0)
                {
                    var min = part.UVs.Aggregate(Vector2.Min); var max = part.UVs.Aggregate(Vector2.Max);
                    var candidates = placements.Where(p => min.X >= p.Min.X && min.Y >= p.Min.Y && max.X <= p.Max.X && max.Y <= p.Max.Y)
                        .Where(p => _layout.Find(p.Source, (min - p.Offset) / p.Scale, (max - p.Offset) / p.Scale)
                            .Any(m => m.Destination == destination && m.Scale == p.Scale && m.Offset == p.Offset))
                        .Select(p => p.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (candidates.Length == 1) inferred = candidates[0];
                }
                if (inferred is not null) original = inferred;
                else
                {
                    if (changed && input.Alternate) throw new InvalidDataException("Alternate texture for " + input.Shape + " cannot be associated with a source tile in " + destination + ". Check the full-model and LOD shape names and recipe sources.");
                    Record(part, source, source, "Retained", "Full-model texture " + original + " cannot be associated with a unique atlas tile.");
                    continue;
                }
            }
            var replacement = new RecipeTextureReplacement(diffuse, normal);
            if (replacements.TryGetValue(original, out var previous) && previous != replacement)
                throw new InvalidDataException("Merged shapes assign different textures to the same atlas source " + original + ". Separate their LOD shapes or supply separate source tiles.");
            replacements[original] = replacement;
        }
        // Retain unchanged source normals as well: their names need not follow the _n convention.
        bool needsVariant = replacements.Any(p => !Same(p.Key, p.Value.Diffuse)
            || p.Value.Normal is not null && !Same(TextureRecipeRenderer.Normal(p.Key), p.Value.Normal));
        string? normalPath = _assets.Exists(TextureRecipeRenderer.Normal(destination)) ? TextureRecipeRenderer.Normal(destination) : null;
        if (needsVariant || !_assets.Exists(destination))
        {
            var built = Build(destination, replacements);
            destination = built.Diffuse; normalPath = built.Normal;
        }
        else if (known) return part;
        var textures = part.Material.Textures.ToList(); textures[0] = destination;
        if (normalPath is not null) { while (textures.Count < 2) textures.Add(""); textures[1] = normalPath; }
        var mapped = Copy(part, part.Material with { Textures = textures }, part.UVs.Select(p => p * scale + offset).ToArray());
        Record(part, source, destination, needsVariant ? "Variant" : "Mapped", known ? "Recipe atlas rebuilt; original UVs retained." : "Recipe UV layout applied.");
        return mapped;
    }

    private (string Diffuse, string? Normal) Build(string destination, IReadOnlyDictionary<string, RecipeTextureReplacement> replacements)
    {
        string key = destination + "|" + _recipeFingerprint + "|" + JsonSerializer.Serialize(replacements.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase));
        if (_variants.TryGetValue(key, out var existing)) return existing;
        string scratch = Path.Combine(_output, ".recipe-build-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        try
        {
            void Save(RecipeRenderedTexture texture)
            { string path = GamePath.ToFileSystemPath(scratch, texture.Destination); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, texture.Bytes); }
            byte[]? ReadGenerated(string path) { string file = GamePath.ToFileSystemPath(scratch, path); return File.Exists(file) ? File.ReadAllBytes(file) : null; }
            var renderer = new TextureRecipeRenderer(_recipes, _read, Save, replacements: replacements, readGenerated: ReadGenerated);
            byte[] diffuse = renderer.Build(destination); byte[]? normal = renderer.BuildNormal(destination);
            string stem = @"textures\pengulod\recipes\" + OutputSnapshot.HashText(key + Convert.ToHexString(SHA256.HashData(diffuse))
                + (normal is null ? "" : Convert.ToHexString(SHA256.HashData(normal)))).ToLowerInvariant()[..32];
            void Write(string relative, byte[] bytes)
            { string file = GamePath.ToFileSystemPath(_output, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes); }
            Write(stem + ".dds", diffuse); if (normal is not null) Write(stem + "_n.dds", normal);
            return _variants[key] = (stem + ".dds", normal is null ? null : stem + "_n.dds");
        }
        finally { Directory.Delete(scratch, true); }
    }
    private void Record(LodMeshPart part, string source, string destination, string status, string detail)
    { var row = new MappingReport(part.Name ?? "", source, destination, status, detail); _report[JsonSerializer.Serialize(row)] = row; }
    private static bool Same(string? a, string? b) => string.Equals(a?.Replace('/', '\\'), b?.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
    public void SaveReport()
    { Directory.CreateDirectory(_output); File.WriteAllText(Path.Combine(_output, "PenguLOD.recipes.json"), JsonSerializer.Serialize(Report, PersistentPipeline.Json)); }
    public void ThrowIfFailed()
    {
        SaveReport(); var failures = Report.Where(r => r.Status == "Failed").ToArray();
        if (failures.Length > 0) throw new InvalidDataException("Recipe texture mapping failed: " + string.Join("; ", failures.Take(5).Select(r => r.Shape + ": " + r.Detail)));
    }
    public static LodMeshPart Copy(LodMeshPart p, LodMaterial material, Vector2[] uvs) => new()
    {
        Name = p.Name, TexGenScale = p.TexGenScale, Tier = p.Tier, Material = material, Positions = p.Positions, UVs = uvs,
        Normals = p.Normals, Tangents = p.Tangents, Bitangents = p.Bitangents, Colors = p.Colors, Triangles = p.Triangles
    };
}
