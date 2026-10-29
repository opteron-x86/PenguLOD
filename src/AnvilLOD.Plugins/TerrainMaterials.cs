using System.Numerics;
using AnvilLOD.Core;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Textures;

namespace AnvilLOD.Plugins;

internal readonly record struct TerrainSample(Vector3 Color, Vector3 Detail, Vector3? ExistingNormal = null);

/// <summary>Linear-light colour mips and unweighted data mips. Alpha is not transparency in landscape textures.</summary>
internal sealed class TerrainMaterials(IAssetSource assets, TerrainMaterialSettings settings, CancellationToken ct)
{
    private sealed record Image(Vector3[] Pixels, int Width, int Height);
    private sealed record Material(string Diffuse, string? Normal, string? Ao, bool Pbr);
    private readonly Dictionary<(string, string?, string?), Material> _materials = [];
    private readonly Dictionary<(string, bool), List<Image>> _images = [];
    private readonly Dictionary<(string, bool, float), Image> _samples = [];
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private readonly HashSet<string> _available = new(StringComparer.Ordinal);
    private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);
    private long _bytes;
    private static readonly float[] Linear = Enumerable.Range(0,256).Select(i => TerrainSource.SrgbToLinear(new Vector3(i/255f)).X).ToArray();
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
        ct.ThrowIfCancellationRequested();
        if (_samples.TryGetValue((path,color,footprint),out var selected)) return Sample(selected,u,v,wrap);
        var key = (path, color);
        if (!_images.TryGetValue(key, out var mips))
        {
            if (!assets.TryOpen(path, out var stream)) throw new FileNotFoundException("Missing landscape texture: " + path, path);
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
            var image = new Image(pixels,width,height);
            // Cache complete mip chains under 192 MiB. Material records do not retain evicted images.
            if (_bytes >= 192L * 1024 * 1024) { _images.Clear(); _samples.Clear(); _bytes = 0; }
            mips = [image]; _images[key] = mips; _bytes += image.Pixels.LongLength * 12;
        }
        int lod = Math.Max(0, (int)MathF.Floor(MathF.Log2(Math.Max(1, footprint * mips[0].Width))));
        while (mips.Count <= lod && (mips[^1].Width > 1 || mips[^1].Height > 1))
        { var mip = Downsample(mips[^1]); mips.Add(mip); _bytes += mip.Pixels.LongLength * 12; }
        selected=mips[Math.Min(lod, mips.Count - 1)];_samples[(path,color,footprint)]=selected;
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
    private static string Twin(string path) => path.StartsWith(@"textures\pbr\", StringComparison.OrdinalIgnoreCase)
        ? path : @"textures\pbr\" + path[@"textures\".Length..];
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
    private static Vector3 Sample(Image image, float u, float v, bool wrap)
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
