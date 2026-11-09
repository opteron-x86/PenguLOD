using System.Buffers.Binary;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Textures.Bc;
using AnvilLOD.Textures.Dds;

namespace AnvilLOD.Textures;

/// <summary>LOD-only texture sizing. Authored compressed mip levels are copied without recompression.</summary>
public static class LodTextureEncoding
{
    public enum Channels { Color, OpaqueColor, Normal, Data }

    public static bool IsSrgb(DdsFile.Info info) => info.DxgiFormat is 29 or 72 or 75 or 78 or 91 or 99;

    public static DdsFile.Info Read2D(byte[] bytes)
    {
        var info = DdsFile.ReadInfo(bytes);
        uint caps2 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(112));
        if ((caps2 & 0x20FE00) != 0 || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)) > 1
            || (info.DataOffset == 148 && (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(132)) != 3
                || (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(136)) & 4) != 0
                || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(140)) != 1)))
            throw new NotSupportedException("Cubemaps, volumes and texture arrays retain their original textures.");
        if (info.Width > 16384 || info.Height > 16384 || info.MipCount > 15)
            throw new NotSupportedException("Texture dimensions or mip count exceed the supported limits.");
        if (info.Uncompressed && info.DataOffset == 128 && info.BitsPerPixel == 32)
        {
            uint r = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(92)), g = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(96));
            uint b = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(100)), a = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(104));
            if (g != 0xFF00 || !((r == 0xFF && b == 0xFF0000) || (r == 0xFF0000 && b == 0xFF)) || a is not (0 or 0xFF000000))
                throw new NotSupportedException("Nonstandard uncompressed channel masks retain their original texture.");
        }
        return info;
    }

    public static TextureImage Decode(byte[] bytes)
    {
        var info = Read2D(bytes);
        var image = TextureImages.Decode(bytes);
        if (info.Uncompressed && info.DataOffset == 128 && info.BitsPerPixel == 32
            && (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(104)) == 0 || (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80)) & 1) == 0))
            for (int i = 3; i < image.Pixels.Length; i += 4) image.Pixels[i] = 255;
        return image;
    }

    /// <returns>Null if the existing file should be used unchanged.</returns>
    public static byte[]? Optimize(byte[] bytes, int maxSize, Channels channels, int? alphaCutoff = null)
    {
        if (maxSize is < 128 or > 4096 || (maxSize & (maxSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(maxSize));
        var info = Read2D(bytes);
        int w = info.Width, h = info.Height;
        if (!info.Uncompressed)
        {
            int offset = info.DataOffset, first = -1, start = 0, width = 0, height = 0;
            for (int mip = 0; mip < info.MipCount; mip++)
            {
                int length = checked(((w + 3) / 4) * ((h + 3) / 4) * info.Format.BlockBytes());
                if (offset + (long)length > bytes.Length) throw new InvalidDataException("Truncated compressed mip chain.");
                if (first < 0 && w <= maxSize && h <= maxSize) { first = mip; start = offset; width = w; height = h; }
                offset += length;
                if (w == 1 && h == 1 && mip + 1 < info.MipCount) throw new InvalidDataException("Excess mip levels.");
                if (mip + 1 < info.MipCount) { w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); }
            }
            // Keep even partial authored mip chains. Adding levels can change alpha coverage or atlas seams.
            if (first == 0) return null;
            if (first > 0)
            {
                var result = new byte[info.DataOffset + offset - start];
                bytes.AsSpan(0, info.DataOffset).CopyTo(result);
                bytes.AsSpan(start, offset - start).CopyTo(result.AsSpan(info.DataOffset));
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), height);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), width);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(20), ((width + 3) / 4) * ((height + 3) / 4) * info.Format.BlockBytes());
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), info.MipCount - first);
                return result;
            }
            // Signed/typeless data and two-channel non-normal maps cannot be faithfully decoded by the RGBA decoder.
            if (info.DxgiFormat is 70 or 73 or 76 or 79 or 81 or 82 or 84 or 97
                || (info.Format == BcFormat.Bc5 && channels != Channels.Normal))
                throw new NotSupportedException("This compressed format needs authored mip levels for lossless sizing.");
        }
        else if (info.DxgiFormat is 27 or 90)
            throw new NotSupportedException("Typeless textures retain their original interpretation.");

        var image = Decode(bytes);
        float? coverage = alphaCutoff is > 0 and < 256 ? PbrLodConverter.Coverage(image.Pixels, 1, alphaCutoff.Value) : null;
        while (image.Width > maxSize || image.Height > maxSize) image = Down(image, channels, IsSrgb(info));
        var encoded = Encode(image, channels, IsSrgb(info), alphaCutoff, coverage);
        return encoded.Length < bytes.Length ? encoded : null;
    }

    public static TextureImage Down(TextureImage image, Channels channels, bool srgb = false)
    {
        // Average normal/gloss and packed data channels independently. Color transparency weights RGB only.
        var pixels = image.Pixels;
        if (srgb && channels != Channels.Normal)
        {
            // Linear-light filtering at byte precision is deliberately avoided: dark texels would quantize badly.
            return Resample(image, Math.Max(1, image.Width / 2), Math.Max(1, image.Height / 2), channels, true, prefilter: false);
        }
        var next = TreeAtlasBuilder.Downsample(pixels, image.Width, image.Height, alphaWeighted: channels == Channels.Color);
        if (channels == Channels.Normal)
            for (int i = 0; i < next.Rgba.Length; i += 4) TextureImages.Normalize(next.Rgba, i);
        return new(next.Rgba, next.W, next.H);
    }

    public static TextureImage Limit(TextureImage image, int maxSize, Channels channels, bool srgb = false, int? alphaCutoff = null)
    {
        float? target = alphaCutoff is > 0 and < 256 ? PbrLodConverter.Coverage(image.Pixels, 1, alphaCutoff.Value) : null;
        while (image.Width > maxSize || image.Height > maxSize) image = Down(image, channels, srgb);
        if (target is > 0 and < .999f) PbrLodConverter.KeepCoverage(image.Pixels, target.Value, alphaCutoff!.Value, preserveMatching: true);
        return image;
    }

    public static TextureImage Resample(TextureImage image, int width, int height, Channels channels, bool srgb = false, bool prefilter = true)
    {
        // Box prefilter before bilinear resampling prevents small cells from aliasing large source textures.
        while (prefilter && image.Width >= width * 2 && image.Height >= height * 2 && (image.Width > 1 || image.Height > 1))
        {
            // For exact half-size sRGB resampling the loop below is the box filter itself.
            if (srgb && channels != Channels.Normal && image.Width == width * 2 && image.Height == height * 2) break;
            image = Down(image, channels, srgb);
        }
        var result = new byte[checked(width * height * 4)];
        Span<float> sum = stackalloc float[4];
        for (int y = 0; y < height; y++)
        {
            RunLimits.Check();
            float sy = (y + .5f) * image.Height / height - .5f;
            int y0 = (int)MathF.Floor(sy); float fy = sy - y0;
            for (int x = 0; x < width; x++)
            {
                float sx = (x + .5f) * image.Width / width - .5f;
                int x0 = (int)MathF.Floor(sx); float fx = sx - x0;
                sum.Clear();
                float weight = 0;
                for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                {
                    float f = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
                    int p = (Math.Clamp(y0 + dy, 0, image.Height - 1) * image.Width + Math.Clamp(x0 + dx, 0, image.Width - 1)) * 4;
                    float alpha = image.Pixels[p + 3] / 255f;
                    float colorWeight = channels == Channels.Color ? f * alpha : f;
                    weight += colorWeight; sum[3] += f * alpha;
                    for (int c = 0; c < 3; c++)
                    {
                        float value = image.Pixels[p + c] / 255f;
                        if (srgb && channels != Channels.Normal) value = value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
                        sum[c] += colorWeight * value;
                    }
                }
                int dest = (y * width + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    float value = c == 3 ? sum[c] : weight > 0 ? sum[c] / weight : 0;
                    if (c < 3 && srgb && channels != Channels.Normal) value = value <= .0031308f ? 12.92f * value : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
                    result[dest + c] = (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);
                }
                if (channels == Channels.Normal) TextureImages.Normalize(result, dest);
            }
        }
        return new(result, width, height);
    }

    public static byte[] Encode(TextureImage image, Channels channels, bool srgb = false, int? alphaCutoff = null, float? coverage = null)
    {
        var mips = new List<byte[]>();
        coverage ??= alphaCutoff is > 0 and < 256 ? PbrLodConverter.Coverage(image.Pixels, 1, alphaCutoff.Value) : null;
        var level = image;
        while (true)
        {
            RunLimits.Check();
            var shown = (byte[])level.Pixels.Clone();
            if (channels == Channels.Normal) for (int i = 0; i < shown.Length; i += 4) TextureImages.Normalize(shown, i);
            if (coverage is > 0 and < .999f && alphaCutoff is > 0 and < 256)
                PbrLodConverter.KeepCoverage(shown, coverage.Value, alphaCutoff.Value, preserveMatching: true);
            mips.Add(Bc7Encoder.EncodeImage(shown, level.Width, level.Height));
            if (level.Width == 1 && level.Height == 1) break;
            level = Down(level, channels, srgb);
        }
        using var output = new MemoryStream();
        DdsFile.WriteBc7(output, image.Width, image.Height, mips, srgb);
        return output.ToArray();
    }
}
