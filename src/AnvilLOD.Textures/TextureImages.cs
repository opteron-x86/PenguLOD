using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Textures;
using AnvilLOD.Textures.Bc;
using AnvilLOD.Textures.Dds;

namespace AnvilLOD.Textures;

public sealed record TextureImage(byte[] Pixels, int Width, int Height);

/// <summary>Native resizing, stitching and DDS output; normal vectors and alpha survive the mip chain.</summary>
public static class TextureImages
{
    public static TextureImage Decode(byte[] bytes)
    {
        if (bytes.Length >= 148 && bytes.AsSpan(84, 4).SequenceEqual("DX10"u8)
            && BitConverter.ToInt32(bytes, 128) is 95 or 96)
            bytes = NativeTextureConverter.Bc6ToBc7(bytes);
        var d = DdsFile.DecodeTopMip(bytes);
        return new(d.Rgba, d.Width, d.Height);
    }

    public static TextureImage Blank(int width, int height, bool normal = false)
    {
        if (width < 4 || height < 4 || width > 4096 || height > 4096)
            throw new ArgumentOutOfRangeException(nameof(width), "Generated textures must be 4–4096 pixels per side.");
        var pixels = new byte[checked(width * height * 4)];
        if (normal)
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 128; pixels[i + 1] = 128; pixels[i + 2] = 255; pixels[i + 3] = 255; }
        return new(pixels, width, height);
    }

    public static int Size(float factor, int basis)
    {
        double size = Math.Round((double)factor * basis);
        if (size < 4 || size > 4096 || (int)size % 4 != 0)
            throw new InvalidDataException($"Texture dimension {size} is outside 4–4096 or is not a multiple of four; adjust base size.");
        return (int)size;
    }

    public static void Blit(TextureImage target, TextureImage source, TextureTile tile, int basis, bool normal, float brightness = 1)
    {
        int w = checked((int)MathF.Round(tile.Width * basis)), h = checked((int)MathF.Round(tile.Height * basis));
        if (w is < 1 or > 16384 || h is < 1 or > 16384) throw new InvalidDataException("Invalid tile dimensions.");
        int ox = checked((int)MathF.Round(tile.X * basis)), oy = checked((int)MathF.Round(tile.Y * basis));
        // TexGen recipes deliberately place tiles partly outside the atlas (for example woven fences).
        int left = (int)Math.Clamp(-(long)ox, 0, w), right = (int)Math.Clamp((long)target.Width - ox, 0, w);
        int topRow = (int)Math.Clamp(-(long)oy, 0, h), bottomRow = (int)Math.Clamp((long)target.Height - oy, 0, h);
        // Reduce large inputs first so a small tile averages the source rather than sampling isolated full-resolution pixels.
        while (source.Width >= w * 2 || source.Height >= h * 2)
        {
            if (source.Width == 1 && source.Height == 1) break;
            var d = TreeAtlasBuilder.Downsample(source.Pixels, source.Width, source.Height);
            source = new(d.Rgba, d.W, d.H);
        }
        for (int y = topRow; y < bottomRow; y++)
        {
            RunLimits.Check();
            float sy = (y + .5f) * source.Height / h - .5f;
            int y0 = (int)MathF.Floor(sy), y1 = Math.Clamp(y0 + 1, 0, source.Height - 1);
            float fy = sy - y0; y0 = Math.Clamp(y0, 0, source.Height - 1);
            for (int x = left; x < right; x++)
            {
                float sx = (x + .5f) * source.Width / w - .5f;
                int x0 = (int)MathF.Floor(sx), x1 = Math.Clamp(x0 + 1, 0, source.Width - 1);
                float fx = sx - x0; x0 = Math.Clamp(x0, 0, source.Width - 1);
                int dest = ((oy + y) * target.Width + ox + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    float top = source.Pixels[(y0 * source.Width + x0) * 4 + c] * (1 - fx) + source.Pixels[(y0 * source.Width + x1) * 4 + c] * fx;
                    float bottom = source.Pixels[(y1 * source.Width + x0) * 4 + c] * (1 - fx) + source.Pixels[(y1 * source.Width + x1) * 4 + c] * fx;
                    float value = top * (1 - fy) + bottom * fy;
                    if (!normal && c < 3)
                        value = (value * MathF.Pow(c == 0 ? tile.Red : c == 1 ? tile.Green : tile.Blue, 1 / 2.2f) + tile.Brightness) * brightness;
                    if (!normal && c == 3 && !tile.Alpha) value = 255;
                    target.Pixels[dest + c] = (byte)Math.Clamp(MathF.Round(value), 0, 255);
                }
                if (normal) Normalize(target.Pixels, dest);
            }
        }
    }

    public static void Normalize(byte[] px, int offset)
    {
        float x = px[offset] / 127.5f - 1, y = px[offset + 1] / 127.5f - 1, z = px[offset + 2] / 127.5f - 1;
        float length = MathF.Sqrt(x * x + y * y + z * z);
        if (length < .001f) { x = y = 0; z = 1; length = 1; }
        px[offset] = (byte)Math.Clamp(MathF.Round((x / length + 1) * 127.5f), 0, 255);
        px[offset + 1] = (byte)Math.Clamp(MathF.Round((y / length + 1) * 127.5f), 0, 255);
        px[offset + 2] = (byte)Math.Clamp(MathF.Round((z / length + 1) * 127.5f), 0, 255);
    }

    public static byte[] Encode(TextureImage image, bool normal = false, bool alpha = false)
    {
        var mips = new List<byte[]>();
        var levels = alpha && !normal ? PbrLodConverter.MipChain(image.Pixels, image.Width, image.Height)
            : Levels(image).ToList();
        foreach (var level in levels)
        {
            RunLimits.Check();
            if (normal)
                for (int i = 0; i < level.Rgba.Length; i += 4) Normalize(level.Rgba, i);
            mips.Add(Bc7Encoder.EncodeImage(level.Rgba, level.W, level.H));
        }
        using var output = new MemoryStream();
        DdsFile.WriteBc7(output, image.Width, image.Height, mips);
        return output.ToArray();
    }

    private static IEnumerable<(byte[] Rgba, int W, int H)> Levels(TextureImage image)
    {
        var level = (Rgba: image.Pixels, W: image.Width, H: image.Height);
        yield return level;
        while (level.W > 4 || level.H > 4) { level = TreeAtlasBuilder.Downsample(level.Rgba, level.W, level.H); yield return level; }
    }
}
