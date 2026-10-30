using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Textures;

namespace AnvilLOD.Plugins.Authoring;

/// <summary>Matching native trunk cards and textures, in the full tree's coordinate system.</summary>
internal sealed class HybridTreeAuthor(AssetIndex assets, Tree3DSettings settings, string? cacheRoot = null)
{
    internal sealed record Frame(float Width, float Height, float Bottom);
    internal sealed record Entry(string Stem, Frame Front, Frame Side, IReadOnlyDictionary<string, string> Files);
    internal sealed record Result(LodMesh Crown, IReadOnlyList<LodMeshPart> Cards, Entry Resources);
    private readonly string _root = cacheRoot ?? Path.Combine(Path.GetDirectoryName(TreeBillboardSynth.CacheRoot)!, "HybridTrees");
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public Result? Build(LodMesh source, out string? reason)
    {
        reason = null;
        try
        {
            RunLimits.Check();
            var bark = source.Parts.Where(p => !p.Material.HasAlpha).ToArray();
            var crown = source.Parts.Where(p => p.Material.HasAlpha && (p.Material.AlphaFlags & 0x0200) != 0).ToArray();
            if (source.HasIncompleteGeometry || bark.Length == 0 || crown.Length == 0 || bark.Length + crown.Length != source.Parts.Count)
            { reason = "Hybrid authoring needs separate opaque trunk and alpha-tested crown shapes, without blended/effect parts."; return null; }
            var trunk = new LodMesh { Path = source.Path, Parts = bark };
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"hybrid1|{source.Path}|{settings.TrunkTextureSize}|{settings.TrunkUnitsPerPixel:R}")));
            hash.AppendData(Read(source.Path) ?? throw new FileNotFoundException(source.Path));
            foreach (var part in bark) hash.AppendData(Encoding.UTF8.GetBytes(part.Material.Key));
            foreach (var texture in bark.SelectMany(p => p.Material.Textures).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
                hash.AppendData(Read(texture) ?? throw new FileNotFoundException("Missing trunk texture: " + texture));
            string id = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            string stem = $@"textures\pengulod\hybrid\{id[..2]}\{id}";
            if (!_entries.TryGetValue(stem, out var entry))
            {
                string manifest = GamePath.ToFileSystemPath(_root, stem + ".json");
                if (File.Exists(manifest))
                {
                    try
                    {
                        var saved = JsonSerializer.Deserialize<Entry>(File.ReadAllText(manifest));
                        string[] expected = [stem + "_1.dds", stem + "_1_n.dds", stem + "_2.dds", stem + "_2_n.dds"];
                        if (saved is not null && saved.Stem == stem && Valid(saved.Front) && Valid(saved.Side)
                            && saved.Files is { Count: 4 } && expected.All(f => saved.Files.TryGetValue(f, out var checksum)
                            && File.Exists(GamePath.ToFileSystemPath(_root, f)) && Digest(File.ReadAllBytes(GamePath.ToFileSystemPath(_root, f))) == checksum)) entry = saved;
                    }
                    catch (Exception e) when (e is IOException or JsonException) { /* Repair partial cache entries below. */ }
                }
                if (entry is null)
                {
                    var loader = TextureGeneration.TextureLoader(Read);
                    BillboardImage Render(BillboardView view) => BillboardRenderer.Render(trunk, loader, settings.TrunkTextureSize,
                        unitsPerPixel: settings.TrunkUnitsPerPixel, view: view, originCentered: true)
                        ?? throw new InvalidDataException("Trunk has no visible surface in the " + view + " view.");
                    var front = Render(BillboardView.Front); var side = Render(BillboardView.Side);
                    var files = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (image, view) in new[] { (front, 1), (side, 2) })
                    {
                        RunLimits.Check();
                        Write(stem + $"_{view}.dds", TextureImages.Encode(new(image.Rgba, image.Width, image.Height), alpha: true));
                        Write(stem + $"_{view}_n.dds", TextureImages.Encode(new(image.NormalRgba!, image.Width, image.Height), normal: true));
                    }
                    entry = new(stem, new(front.WorldWidth, front.WorldHeight, front.ShiftZ), new(side.WorldWidth, side.WorldHeight, side.ShiftZ), files);
                    WriteAtomic(manifest, JsonSerializer.SerializeToUtf8Bytes(entry));
                    void Write(string relative, byte[] bytes) { WriteAtomic(GamePath.ToFileSystemPath(_root, relative), bytes); files[relative] = Digest(bytes); }
                }
                assets.SetOverrides(_root, entry.Files.Keys);
                _entries[stem] = entry;
            }
            return new(new LodMesh { Path = source.Path, Parts = crown }, [Card(entry, false), Card(entry, true)], entry);
        }
        catch (Exception e) when (e is not OperationCanceledException) { reason = "Hybrid trunk rendering unavailable: " + e.Message; return null; }
    }

    public void Use(Result result) => _used.Add(result.Resources.Stem);

    public int Publish(string output)
    {
        int count = 0;
        foreach (string stem in _used.Order(StringComparer.Ordinal))
        foreach (var (file, checksum) in _entries[stem].Files)
        {
            RunLimits.Check();
            byte[] bytes = File.ReadAllBytes(GamePath.ToFileSystemPath(_root, file));
            if (Digest(bytes) != checksum) throw new InvalidDataException("Generated hybrid texture changed during the job: " + file);
            WriteAtomic(GamePath.ToFileSystemPath(output, file), bytes); count++;
        }
        return count;
    }

    private static bool Valid(Frame? f) => f is not null && float.IsFinite(f.Width + f.Height + f.Bottom) && f.Width > 1 && f.Height > 1;
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private byte[]? Read(string path)
    {
        if (!assets.TryOpen(path, out var stream)) return null;
        using (stream) { using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }
    }
    private static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static LodMeshPart Card(Entry entry, bool side)
    {
        var f = side ? entry.Side : entry.Front; string stem = entry.Stem + (side ? "_2" : "_1");
        var across = side ? Vector3.UnitY : Vector3.UnitX; var normal = side ? Vector3.UnitX : -Vector3.UnitY;
        var bottom = Vector3.UnitZ * f.Bottom; var top = bottom + Vector3.UnitZ * f.Height; var half = across * (f.Width * .5f);
        return new()
        {
            Name = side ? "Native trunk side" : "Native trunk front",
            Material = new([stem + ".dds", stem + "_n.dds", "", "", "", "", "", "", ""], 0x80000300, 0x15, 3,
                true, GrassPatchBuilder.AlphaTestFlags, GrassPatchBuilder.AlphaThreshold, Vector3.Zero, 1),
            Positions = [bottom - half, bottom + half, top + half, top - half], UVs = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)],
            Normals = [normal, normal, normal, normal], Tangents = [across, across, across, across],
            Bitangents = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], Triangles = [0, 1, 2, 0, 2, 3]
        };
    }
}
