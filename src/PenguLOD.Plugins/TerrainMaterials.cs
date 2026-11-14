using System.Collections.Concurrent;
using System.Numerics;
using PenguLOD.Core;
using PenguLOD.Core.Pipeline;
using PenguLOD.Core.World;
using PenguLOD.Textures;

namespace PenguLOD.Plugins;

internal readonly record struct TerrainSample(Vector3 Color, Vector3 Detail, Vector3? ExistingNormal = null);

/// <summary>
/// Linear-light colour mips and unweighted data mips of landscape textures, shared by the workers that bake tiles at the
/// same time and by every worldspace of a run. A level holds the same pixels however it was reached, so sharing changes
/// no output. Alpha is not transparency in landscape textures.
/// </summary>
internal sealed class TerrainTextures(IAssetSource assets, CancellationToken ct)
{
    internal sealed record Image(Vector3[] Pixels, int Width, int Height);
    /// <summary>The retained levels of one texture: <see cref="First"/> is the level number of <c>Levels[0]</c>. Never changed once stored.</summary>
    private sealed record Chain(int BaseWidth, int First, Image[] Levels) { internal long Bytes => Levels.Sum(l => l.Pixels.LongLength * 12); }
    private const long CacheBytes = 192L * 1024 * 1024;
    private readonly ConcurrentDictionary<(string, bool), Chain> _images = new();
    // One worker decodes a texture while the others that want the same one wait for it.
    private readonly object[] _gates = [.. Enumerable.Range(0, 64).Select(_ => new object())];
    private readonly object _account = new();
    private long _bytes;
    private int _loads;
    private static readonly float[] Linear = Enumerable.Range(0,256).Select(i => TerrainSource.SrgbToLinear(new Vector3(i/255f)).X).ToArray();
    /// <summary>How many times a texture file was opened and decoded.</summary>
    internal int Loads => Volatile.Read(ref _loads);

    /// <summary>The level of a texture that one pixel of <paramref name="footprint"/> (in texture widths) reads.</summary>
    internal Image Level(string path, bool color, float footprint)
    {
        ct.ThrowIfCancellationRequested();
        var key = (path, color);
        if (_images.TryGetValue(key, out var chain) && Ready(chain, footprint) is { } ready) return ready;
        lock (_gates[(StringComparer.Ordinal.GetHashCode(path) & int.MaxValue) % _gates.Length])
        {
            _images.TryGetValue(key, out chain);
            bool loaded = false, changed = false;
            // A terrain tile reads a landscape texture far below its own resolution, so only the level asked for and the
            // coarser ones are kept. Holding the full image for every texture filled the cache within one large tile, and
            // each refill decoded the source files again.
            if (chain is null || Lod(footprint, chain.BaseWidth) < chain.First)
            {
                var image = Load(path, color);
                int baseWidth = image.Width, first = 0;
                for (int lod = Lod(footprint, baseWidth); first < lod && (image.Width > 1 || image.Height > 1); first++) image = Downsample(image);
                chain = new(baseWidth, first, [image]); loaded = changed = true;
            }
            int wanted = Lod(footprint, chain.BaseWidth);
            if (chain.First + chain.Levels.Length <= wanted && (chain.Levels[^1].Width > 1 || chain.Levels[^1].Height > 1))
            {
                var levels = chain.Levels.ToList();
                while (chain.First + levels.Count <= wanted && (levels[^1].Width > 1 || levels[^1].Height > 1)) levels.Add(Downsample(levels[^1]));
                chain = chain with { Levels = [.. levels] }; changed = true;
            }
            if (changed)
                lock (_account)
                {
                    if (_images.TryGetValue(key, out var previous)) _bytes -= previous.Bytes;
                    // Workers still sampling a dropped image keep it alive until they finish their tile.
                    if (loaded && _bytes >= CacheBytes) { _images.Clear(); _bytes = 0; }
                    _images[key] = chain; _bytes += chain.Bytes;
                }
            return chain.Levels[Math.Min(wanted - chain.First, chain.Levels.Length - 1)];
        }
    }

    /// <summary>The level for this footprint when the chain already holds it, without taking a lock.</summary>
    private static Image? Ready(Chain chain, float footprint)
    {
        int index = Lod(footprint, chain.BaseWidth) - chain.First;
        if (index < 0) return null;
        if (index < chain.Levels.Length) return chain.Levels[index];
        var last = chain.Levels[^1];
        return last.Width > 1 || last.Height > 1 ? null : last;
    }

    private static int Lod(float footprint, int baseWidth) => Math.Max(0, (int)MathF.Floor(MathF.Log2(Math.Max(1, footprint * baseWidth))));

    /// <summary>The source texture reduced to at most 512 pixels a side: level 0 of its chain.</summary>
    private Image Load(string path, bool color)
    {
        if (!assets.TryOpen(path, out var stream)) throw new FileNotFoundException("Missing landscape texture: " + path, path);
        Interlocked.Increment(ref _loads);
        TextureImage raw;
        using (stream) { using var bytes = new MemoryStream(); stream.CopyTo(bytes); raw = TextureImages.Decode(bytes.ToArray()); }
        int factor=1;
        while (raw.Width/factor>512 || raw.Height/factor>512) factor*=2;
        int width=Math.Max(1,raw.Width/factor), height=Math.Max(1,raw.Height/factor);
        var pixels = new Vector3[width*height];
        for (int y=0;y<height;y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x=0;x<width;x++)
            {
                Vector3 sum=default; int count=0;
                for (int sy=y*factor;sy<Math.Min((y+1)*factor,raw.Height);sy++)
                for (int sx=x*factor;sx<Math.Min((x+1)*factor,raw.Width);sx++)
                {
                    int offset=(sy*raw.Width+sx)*4;
                    byte r=raw.Pixels[offset], g=raw.Pixels[offset+1], b=raw.Pixels[offset+2];
                    sum+=color ? new Vector3(Linear[r],Linear[g],Linear[b]) : new Vector3(r,g,b)/255;
                    count++;
                }
                pixels[y*width+x]=sum/count;
            }
        }
        return new Image(pixels,width,height);
    }

    private Image Downsample(Image source)
    {
        int w = Math.Max(1, source.Width / 2), h = Math.Max(1, source.Height / 2);
        var pixels = new Vector3[w * h];
        for (int y = 0; y < h; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = 0; x < w; x++)
            {
                Vector3 sum = default;
                for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                    sum += source.Pixels[Math.Min(y * 2 + dy, source.Height - 1) * source.Width + Math.Min(x * 2 + dx, source.Width - 1)];
                pixels[y * w + x] = sum * .25f;
            }
        }
        return new(pixels, w, h);
    }
}

/// <summary>
/// One worker's view of the landscape materials: which files make up a material, and the texture levels the current tile
/// reads. Not shared between threads; <see cref="Fork"/> gives another worker its own over the same decoded textures.
/// </summary>
internal sealed class TerrainMaterials(IAssetSource assets, TerrainMaterialSettings settings, CancellationToken ct, TerrainTextures? textures = null)
{
    private sealed record Material(string Diffuse, string? Normal, string? Ao, bool Pbr);
    private readonly TerrainTextures _textures = textures ?? new(assets, ct);
    private readonly Dictionary<(string, string?, string?), Material> _materials = [];
    private readonly Dictionary<(string, bool, float), TerrainTextures.Image> _samples = [];
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private readonly HashSet<string> _available = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);
    /// <summary>How many times a texture file was opened and decoded, by this view and the others over the same textures.</summary>
    internal int Loads => _textures.Loads;
    internal TerrainMaterials Fork() => new(assets, settings, ct, _textures);
    /// <summary>Forgets what the last tile used, so <see cref="Reports"/> and <see cref="Warnings"/> describe one tile and no
    /// texture level is held longer than the tile that read it.</summary>
    internal void Reset() { _materials.Clear(); _samples.Clear(); _missing.Clear(); _available.Clear(); _warnings.Clear(); }
    internal IReadOnlyCollection<string> Warnings => _warnings;
    internal IEnumerable<TerrainMaterialReport> Reports => _materials.Select(p => new TerrainMaterialReport(p.Key.Item1,
        p.Value.Diffuse, p.Value.Normal, p.Value.Ao, p.Value.Pbr));

    internal TerrainSample Sample(string diffuse, string? normal, string? ao, float u, float v, float footprint)
    {
        var key = (diffuse, normal, ao);
        if (!_materials.TryGetValue(key, out var material))
        {
            bool pbr = diffuse.StartsWith(@"textures\pbr\", StringComparison.OrdinalIgnoreCase);
            if (!pbr && settings.PbrTwins && assets.Exists(Twin(diffuse)))
            {
                diffuse = Twin(diffuse); pbr = true;
                // A twin is a different material: never combine its albedo with the ordinary normal/AO.
                normal = normal is not null ? Twin(normal) : diffuse[..^4] + "_n.dds";
                ao = diffuse[..^4] + "_rmaos.dds";
            }
            if (pbr && ao is null) ao = diffuse[..^4] + "_rmaos.dds";
            material = new(diffuse, settings.NormalStrength > 0 ? normal : null,
                pbr && settings.PbrAlbedo && settings.OcclusionStrength > 0 ? ao : null, pbr);
            _materials[key] = material;
        }
        Vector3 color = Read(material.Diffuse, u, v, footprint, color: true);
        Vector3 detail = Vector3.UnitZ;
        if (Optional(material.Normal, "normal", out var normalPath))
        {
            var rgb = Read(normalPath, u, v, footprint, color: false);
            detail = (rgb * 255 - new Vector3(128)) / 127;
            detail.Z = Math.Max(0, detail.Z);
        }
        if (material.Pbr && settings.PbrAlbedo)
        {
            float occlusion = Optional(material.Ao, "ambient occlusion", out var aoPath)
                ? Read(aoPath, u, v, footprint, color: false).Z : 1;
            color = new Vector3(MathF.Pow(color.X, settings.PbrGamma), MathF.Pow(color.Y, settings.PbrGamma), MathF.Pow(color.Z, settings.PbrGamma))
                * (settings.PbrScale * (1 - settings.OcclusionStrength * (1 - occlusion)));
        }
        return new(color, detail);
    }

    internal Vector3 Read(string path, float u, float v, float footprint, bool color, bool wrap = true)
    {
        if (!_samples.TryGetValue((path, color, footprint), out var selected))
            _samples[(path, color, footprint)] = selected = _textures.Level(path, color, footprint);
        return Sample(selected, u, v, wrap);
    }

    internal bool Optional(string? path, string kind, out string found)
    {
        found = path!;
        if (path is null) return false;
        if (_available.Contains(path)) return true;
        if (_missing.Contains(path)) return false;
        if (assets.Exists(path)) { _available.Add(path); return true; }
        _missing.Add(path); _warnings.Add($"Missing optional terrain {kind}: {path}; using {(kind == "normal" ? "surface slope" : "unoccluded colour")}.");
        return false;
    }
    /// <summary>True when the diffuse map, or the PBR twin read in its place, is installed.</summary>
    internal static bool DiffuseAvailable(IAssetSource assets, TerrainMaterialSettings settings, string diffuse) =>
        assets.Exists(diffuse) || settings.PbrTwins && assets.Exists(Twin(diffuse));
    private static string Twin(string path) => path.StartsWith(@"textures\pbr\", StringComparison.OrdinalIgnoreCase)
        ? path : @"textures\pbr\" + path[@"textures\".Length..];
    private static Vector3 Sample(TerrainTextures.Image image, float u, float v, bool wrap)
    {
        float x = u * image.Width - .5f, y = v * image.Height - .5f;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y); float fx = x - ix, fy = y - iy;
        Vector3 Pixel(int px, int py)
        {
            px = wrap ? (px % image.Width + image.Width) % image.Width : Math.Clamp(px, 0, image.Width - 1);
            py = wrap ? (py % image.Height + image.Height) % image.Height : Math.Clamp(py, 0, image.Height - 1);
            return image.Pixels[py * image.Width + px];
        }
        return Vector3.Lerp(Vector3.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), fx), Vector3.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), fx), fy);
    }

    internal static Vector3 Normal(Vector3 slope, Vector3 detail, float strength)
    {
        // Work in east/north/up. DirectX terrain UVs increase east/south, so green points south.
        var n = Vector3.Normalize(new Vector3(slope.X, slope.Z, slope.Y));
        var tangent = Vector3.Normalize(new Vector3(n.Z, 0, -n.X));
        var south = -Vector3.Normalize(Vector3.Cross(n, tangent));
        var combined = tangent * detail.X * strength + south * detail.Y * strength + n * Math.Max(.001f, detail.Z);
        if (combined.LengthSquared() < 1e-8f) combined = n;
        combined = Vector3.Normalize(combined);
        return new(combined.X, combined.Z, combined.Y); // Skyrim model-space texture: east/up/north.
    }
}
