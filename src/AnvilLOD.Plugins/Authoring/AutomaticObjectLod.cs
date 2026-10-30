using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;

namespace AnvilLOD.Plugins.Authoring;

public sealed record AutomaticObjectSettings(bool Enabled = false, int NearBudget = 2500, int MiddleBudget = 1000,
    int FarBudget = 400, float MinSize = 128, float Detail = 1)
{
    public void Validate()
    {
        if (NearBudget is < 1 or > 1000000 || MiddleBudget is < 1 or > 1000000 || FarBudget is < 1 or > 1000000
            || !float.IsFinite(MinSize) || MinSize is < 0 or > 65536 || !float.IsFinite(Detail) || Detail is < .1f or > 10)
            throw new ArgumentException("Invalid automatic object LOD budgets, minimum size or detail.");
    }
}

public sealed record AutomaticObjectLevel(string Model, int Level, string? Mesh, string Status, int SourceTriangles,
    int Triangles, int DroppedParts, int Budget, string? Reason, string? Hash = null);

/// <summary>Author missing source levels into a verified private cache. Generated NIFs retain shape
/// names and full-model UVs, so the normal record-variant, recipe and atlas pipeline can consume them.</summary>
public sealed class AutomaticObjectLod
{
    private const int Version = 1;
    private readonly AssetIndex _assets;
    private readonly AutomaticObjectSettings _settings;
    private readonly string _root;
    private readonly IProgress<string>? _progress;
    private readonly CancellationToken _ct;
    private readonly Dictionary<string, IReadOnlyList<AutomaticObjectLevel>> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Model, int Level)> _requested = [];

    public AutomaticObjectLod(AssetIndex assets, AutomaticObjectSettings settings, string? cacheRoot = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        settings.Validate(); _assets = assets; _settings = settings; _progress = progress; _ct = ct;
        _root = cacheRoot ?? Path.Combine(Path.GetDirectoryName(TreeBillboardSynth.CacheRoot)!, "ObjectModels");
    }

    public IReadOnlyList<AutomaticObjectLevel> Report => _requested.OrderBy(p => p.Model, StringComparer.Ordinal).ThenBy(p => p.Level)
        .Select(p => _models[p.Model][Math.Min(p.Level, 2)] with { Level = p.Level }).ToArray();

    public string? Resolve(string model, int level)
    {
        if (!_settings.Enabled) return null;
        _ct.ThrowIfCancellationRequested(); RunLimits.Check();
        model = GamePath.Normalize(model);
        if (!model.StartsWith("meshes\\", StringComparison.Ordinal)) model = "meshes\\" + model;
        level = Math.Clamp(level, 0, 3);
        _requested.Add((model, level));
        if (!_models.TryGetValue(model, out var levels)) _models[model] = levels = Prepare(model);
        return levels[Math.Min(level, 2)].Mesh;
    }

    private IReadOnlyList<AutomaticObjectLevel> Prepare(string model)
    {
        int[] budgets = [_settings.NearBudget, Math.Min(_settings.NearBudget, _settings.MiddleBudget),
            Math.Min(_settings.NearBudget, Math.Min(_settings.MiddleBudget, _settings.FarBudget))];
        int sourceCount = 0;
        IReadOnlyList<AutomaticObjectLevel> Skip(string reason)
        {
            _progress?.Report($"WARNING: Automatic object LOD skipped {model}: {reason}");
            return Enumerable.Range(0, 3).Select(l => new AutomaticObjectLevel(model, l, null, "Skipped", sourceCount, 0, 0, budgets[l], reason)).ToArray();
        }
        try
        {
            if (!_assets.TryOpen(model, out var stream)) return Skip("Source model is missing.");
            byte[] bytes;
            using (stream) { using var copy = new MemoryStream(); stream.CopyTo(copy); bytes = copy.ToArray(); }
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"object{Version}|{model}|{Convert.ToHexString(SHA256.HashData(bytes))}|{JsonSerializer.Serialize(_settings)}"))).ToLowerInvariant();
            string stem = $@"meshes\pengulod\generated\objects\{key[..2]}\{key}";
            string manifest = GamePath.ToFileSystemPath(_root, stem + ".json");
            var mesh = NifGeometryReader.Read(model, bytes);
            sourceCount = mesh.Parts.Sum(p => p.TriangleCount);
            if (mesh.HasIncompleteGeometry) return Skip("Unsupported geometry/material: " + string.Join("; ", mesh.Warnings.Take(3)));
            if (mesh.Parts.Count == 0) return Skip("No usable geometry.");
            foreach (var part in mesh.Parts)
            {
                if (part.Positions.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                    || part.UVs.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) return Skip("Non-finite geometry or UV coordinates.");
                if (part.Material.Textures.Count == 0 || string.IsNullOrWhiteSpace(part.Material.Textures[0])) return Skip("A shape has no diffuse texture.");
                foreach (string texture in part.Material.Textures.Take(2).Where(t => !string.IsNullOrWhiteSpace(t)))
                    if (!_assets.Exists(GamePath.NormalizeTexture(texture))) return Skip("Missing source texture: " + texture);
            }
            var (min, max) = LodMeshAuthor.Bounds(mesh);
            var extent = max - min;
            if (MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)) < _settings.MinSize) return Skip("Below the configured minimum model size.");

            IReadOnlyList<AutomaticObjectLevel>? cached = null;
            if (File.Exists(manifest))
            {
                try
                {
                    var saved = JsonSerializer.Deserialize<AutomaticObjectLevel[]>(File.ReadAllText(manifest));
                    if (saved is { Length: 3 } && saved.Select((s, i) => s.Level == i && s.Model == model
                        && s.Mesh == stem + $"_lod_{i}.nif" && s.Hash is not null && File.Exists(GamePath.ToFileSystemPath(_root, s.Mesh))
                        && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(GamePath.ToFileSystemPath(_root, s.Mesh)))) == s.Hash).All(v => v))
                        cached = saved.Select(s => s with { Status = "Cached" }).ToArray();
                }
                catch (Exception e) when (e is IOException or JsonException) { /* Regenerate an incomplete cache entry. */ }
            }
            if (cached is not null) { Attach(cached); return cached; }
            _progress?.Report($"Generating missing object LOD: {model} ({sourceCount:N0} source triangles)");
            var requested = LodMeshAuthor.DefaultLevels.Select((l, i) => l with
                { MaxError = l.MaxError * _settings.Detail, MinPartSize = l.MinPartSize * _settings.Detail, MaxTriangles = budgets[i] }).ToArray();
            _ct.ThrowIfCancellationRequested(); RunLimits.Check();
            var result = LodMeshAuthor.Build(mesh, requested);
            var reports = new List<AutomaticObjectLevel>();
            for (int i = 0; i < 3; i++)
            {
                _ct.ThrowIfCancellationRequested(); RunLimits.Check();
                var built = result.FirstOrDefault(r => r.Level == i);
                if (built is null || built.Triangles == 0 || built.OverBudget)
                {
                    string reason = built?.OverBudget == true ? "Cannot meet triangle budget." : "No usable reduced geometry.";
                    reports.Add(new(model, i, null, "Skipped", sourceCount, 0, built?.DroppedParts ?? 0, budgets[i], reason));
                    _progress?.Report($"WARNING: Automatic object LOD {model}, level {i}: {reason}");
                    continue;
                }
                string path = stem + $"_lod_{i}.nif";
                byte[] nif = LodNifWriter.Write("PenguLOD object", built.Parts, "PenguLOD", preserveShapeNames: true);
                WriteAtomic(GamePath.ToFileSystemPath(_root, path), nif);
                reports.Add(new(model, i, path, "Generated", sourceCount, built.Triangles, built.DroppedParts, budgets[i], null, Convert.ToHexString(SHA256.HashData(nif))));
            }
            WriteAtomic(manifest, JsonSerializer.SerializeToUtf8Bytes(reports));
            Attach(reports); return reports;
        }
        catch (Exception e) when (e is not OperationCanceledException) { return Skip(e.Message); }
    }

    private void Attach(IEnumerable<AutomaticObjectLevel> levels) =>
        _assets.SetOverrides(_root, levels.Select(l => l.Mesh).OfType<string>(), ct: _ct);

    private static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
