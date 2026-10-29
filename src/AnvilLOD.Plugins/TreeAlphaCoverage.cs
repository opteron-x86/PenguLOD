using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Textures;

namespace AnvilLOD.Plugins;

internal sealed class TreeAlphaCoverage(AssetIndex assets)
{
    private sealed record AlphaImage(byte[] Pixels, int Width, int Height);
    private readonly Dictionary<string, AlphaImage> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();
    private long _bytes;
    private const int Budget = 64 * 1024 * 1024;

    public Func<Vector2, float>? Sampler(LodMaterial material)
    {
        if (material.Textures.Count == 0) return null;
        var path = GamePath.NormalizeTexture(material.Textures[0]);
        if (!_cache.TryGetValue(path, out var image))
        {
            if (!assets.TryOpen(path, out var input)) return null;
            try
            {
                using (input)
                {
                    using var data = new MemoryStream(); input.CopyTo(data);
                    var decoded = TextureImages.Decode(data.ToArray());
                    if ((long)decoded.Width * decoded.Height > Budget / 4) return null;
                    var alpha = new byte[decoded.Width * decoded.Height];
                    for (int i = 0; i < alpha.Length; i++) alpha[i] = decoded.Pixels[i * 4 + 3];
                    image = new(alpha, decoded.Width, decoded.Height);
                }
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or ArgumentException or EndOfStreamException)
            { return null; }
            while (_bytes + image.Pixels.Length > Budget && _order.TryDequeue(out var old))
                if (_cache.Remove(old, out var previous)) _bytes -= previous.Pixels.Length;
            _cache[path] = image; _order.Enqueue(path); _bytes += image.Pixels.Length;
        }
        bool wrapU = (material.ClampMode & 2) != 0, wrapV = (material.ClampMode & 1) != 0;
        return uv =>
        {
            if (material.Passthru is { } shader)
                uv = uv * new Vector2(shader.UvScaleX, shader.UvScaleY) + new Vector2(shader.UvOffsetX, shader.UvOffsetY);
            float u = wrapU ? uv.X - MathF.Floor(uv.X) : Math.Clamp(uv.X, 0, 1);
            float v = wrapV ? uv.Y - MathF.Floor(uv.Y) : Math.Clamp(uv.Y, 0, 1);
            float x = u * image.Width - .5f, y = v * image.Height - .5f;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            float a = Pixel(x0, y0), b = Pixel(x0 + 1, y0), c = Pixel(x0, y0 + 1), d = Pixel(x0 + 1, y0 + 1);
            return ((a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy) / 255f
                * Math.Clamp(material.Passthru?.Alpha ?? 1, 0, 1);
        };
        byte Pixel(int x, int y)
        {
            x = wrapU ? (x % image.Width + image.Width) % image.Width : Math.Clamp(x, 0, image.Width - 1);
            y = wrapV ? (y % image.Height + image.Height) % image.Height : Math.Clamp(y, 0, image.Height - 1);
            return image.Pixels[y * image.Width + x];
        }
    }
}
