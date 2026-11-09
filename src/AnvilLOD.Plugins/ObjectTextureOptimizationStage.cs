using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Meshes;
using AnvilLOD.Textures;

namespace AnvilLOD.Plugins;

/// <summary>Creates private object-LOD texture copies without changing geometry, UVs, shader state or source files.</summary>
public static class ObjectTextureOptimizationStage
{
    public const int Version = 1;
    public sealed record TextureResult(string Source, string Output, long SourceBytes, long OutputBytes, string Method);
    public sealed record Report(int RewrittenMaterials, int RewrittenTextures, long SourceBytes, long OutputBytes,
        IReadOnlyList<TextureResult> Textures, IReadOnlyDictionary<string, int> Retained, IReadOnlyList<string> Warnings);
    public sealed record Result(Report Report, Func<LodMesh, LodMesh> Transform);

    public static Result Build(IEnumerable<LodMesh> meshes, AssetIndex assets, string output, int maxSize = 1024,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (maxSize is < 128 or > 4096 || (maxSize & (maxSize - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
        Directory.CreateDirectory(output);
        var source = new ObjectTextureAssets(assets, output);
        var replacements = new Dictionary<string, LodMaterial>(StringComparer.Ordinal);
        var done = new Dictionary<(string Path, LodTextureEncoding.Channels Channels, int? Cutoff, bool Lod), string?>();
        var results = new List<TextureResult>(); var warnings = new List<string>(); var retained = new Dictionary<string, int>();
        var materials = meshes.SelectMany(m => m.Parts).Select(p => p.Material).Distinct().OrderBy(m => m.Key, StringComparer.Ordinal).ToArray();
        int processed = 0;
        foreach (var material in materials)
        {
            ct.ThrowIfCancellationRequested(); RunLimits.Check();
            if (processed++ % 100 == 0) progress?.Report($"Object textures: {processed:N0}/{materials.Length:N0} materials...");
            if (material.Passthru is not null || material.RenderEffect is not null) { Retain("passthrough shader"); continue; }
            var textures = ShaderGuard.TextureSlots(material.Textures); bool changed = false;
            foreach (int slot in new[] { 0, 1, 2, 3, 5, 7, 8 })
            {
                string path = textures[slot]; if (path.Length == 0) continue;
                // Atlas pages have their own resolution and isolated mip chain; never resize them again.
                if (path.Contains(@"\pengulod\objects\", StringComparison.OrdinalIgnoreCase)) continue;
                var channels = slot == 0 ? material.HasAlpha ? LodTextureEncoding.Channels.Color : LodTextureEncoding.Channels.OpaqueColor : slot == 1 ? LodTextureEncoding.Channels.Normal : LodTextureEncoding.Channels.Data;
                int? cutoff = slot == 0 ? ObjectTextureAssets.AlphaCutoff(material) : null;
                var key = (path.ToLowerInvariant(), channels, cutoff, ObjectTextureAssets.IsLodTexture(material));
                if (!done.TryGetValue(key, out var mapped))
                {
                    mapped = null;
                    try
                    {
                        var bytes = source.Read(path);
                        var optimized = LodTextureEncoding.Optimize(bytes, maxSize, channels, cutoff);
                        if (optimized is not null)
                        {
                            string digest = Convert.ToHexString(SHA256.HashData(optimized)).ToLowerInvariant();
                            mapped = ObjectTextureAssets.Root(material, "object-textures") + digest[..32] + ".dds";
                            ObjectTextureAssets.Write(output, mapped, optimized);
                            results.Add(new(path, mapped, bytes.Length, optimized.Length,
                                AnvilLOD.Textures.Dds.DdsFile.ReadInfo(bytes).Uncompressed ? "BC7 compression" : "LOD mip sizing"));
                        }
                        else Retain("already suitable");
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
                    {
                        mapped = null;
                        warnings.Add(path + ": " + ex.Message); Retain("unavailable or unsupported texture");
                    }
                    done[key] = mapped;
                }
                if (mapped is not null) { textures[slot] = mapped; changed = true; }
            }
            if (changed) replacements[material.Key] = material with { Textures = textures };
        }
        var report = new Report(replacements.Count, results.Select(r => r.Output).Distinct().Count(),
            results.DistinctBy(r => r.Source, StringComparer.OrdinalIgnoreCase).Sum(r => r.SourceBytes),
            results.DistinctBy(r => r.Output).Sum(r => r.OutputBytes), results, retained, warnings);
        File.WriteAllText(Path.Combine(output, "PenguLOD.object-textures.json"), JsonSerializer.Serialize(report, PersistentPipeline.Json));
        progress?.Report($"Object textures: {report.RewrittenTextures:N0} private LOD copies; replaced source files {report.SourceBytes / 1048576d:N1} MiB → {report.OutputBytes / 1048576d:N1} MiB encoded. Full-detail textures are unchanged.");
        foreach (var warning in warnings.Take(20)) progress?.Report("Warning: object texture kept its source path: " + warning);
        var cache = new ConcurrentDictionary<LodMesh, LodMesh>(ReferenceEqualityComparer.Instance);
        return new(report, mesh => cache.GetOrAdd(mesh, m => new LodMesh
        {
            Path = m.Path, Warnings = m.Warnings, NoShapesInFile = m.NoShapesInFile,
            Parts = m.Parts.Select(p => replacements.TryGetValue(p.Material.Key, out var material) ? ObjectTextureAssets.With(p, material) : p).ToArray()
        }));
        void Retain(string reason) => retained[reason] = retained.GetValueOrDefault(reason) + 1;
    }
}
