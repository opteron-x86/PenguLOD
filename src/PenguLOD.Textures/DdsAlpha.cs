using PenguLOD.Textures.Bc;
using PenguLOD.Textures.Dds;

namespace PenguLOD.Textures;

/// <summary>
/// Object LOD shapes that have no NiAlphaProperty still get alpha-tested by the engine's LOD shader, so a diffuse texture whose
/// alpha channel carries something else (gloss, a height or mask, leftover paint) punches holes in the mesh. Vanilla and
/// DynDOLOD never show it because their LOD textures are atlases with opaque alpha; PenguLOD points at the full textures.
/// </summary>
public static class DdsAlpha
{
    /// <summary>Alpha below this counts as translucent. A few texels at 250+ are rounding noise from a lossy encoder.</summary>
    public const byte OpaqueAbove = 250;

    /// <summary>True when the top mip has any texel with alpha below <see cref="OpaqueAbove"/>. Formats without alpha say no.</summary>
    public static bool IsTranslucent(byte[] dds)
    {
        var info = DdsFile.ReadInfo(dds);
        if (info.Uncompressed)
        {
            // 24-bit, BGRX and legacy X8 files carry no alpha; their fourth byte (if any) is padding.
            if (!DdsFile.HasAlpha(dds, info)) return false;
            if (info.BitsPerPixel != 32)
            {
                // Packed 16-bit formats with alpha (A1R5G5B5, A4R4G4B4): expand, then test the same way.
                var rgba = DdsFile.DecodeTopMip(dds).Rgba;
                for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] < OpaqueAbove) return true;
                return false;
            }
            var data = dds.AsSpan(info.DataOffset);
            long n = (long)info.Width * info.Height;
            if (n * 4 > data.Length) throw new InvalidDataException("Truncated texture.");
            for (int i = 0; i < n; i++) if (data[i * 4 + 3] < OpaqueAbove) return true;
            return false;
        }
        switch (info.Format)
        {
            case BcFormat.Bc3:
            {
                // Alpha endpoints sit in the first two bytes of each block; the palette lies between them (or is 0/255 at the
                // extremes for the six-value mode), so the lower endpoint is the darkest texel unless the six-value mode uses 0.
                var blocks = DdsFile.TopMipBlocks(dds, info);
                for (int o = 0; o + 16 <= blocks.Length; o += 16)
                {
                    byte a0 = blocks[o], a1 = blocks[o + 1];
                    if (Math.Min(a0, a1) < OpaqueAbove) return true;
                }
                return false;
            }
            case BcFormat.Bc2:
            {
                var blocks = DdsFile.TopMipBlocks(dds, info);
                for (int o = 0; o + 16 <= blocks.Length; o += 16)
                    for (int k = 0; k < 8; k++)
                        if (((blocks[o + k] & 0xF) * 17) < OpaqueAbove || ((blocks[o + k] >> 4) * 17) < OpaqueAbove) return true;
                return false;
            }
            case BcFormat.Bc7:
            {
                var (rgba, _, _, _) = DdsFile.DecodeTopMip(dds);
                for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] < OpaqueAbove) return true;
                return false;
            }
            default:
                return false; // BC1 has at most a 1-bit alpha the shader ignores; BC4/BC5 aren't diffuse textures
        }
    }

    /// <summary>
    /// A copy of the texture with alpha forced to 255. BC2/BC3 are patched block by block, so colours and mips stay byte-identical;
    /// BC7 and uncompressed files are decoded and written again as BC7 with a mip chain, shrunk to <paramref name="maxSize"/> at most.
    /// </summary>
    public static byte[] MakeOpaque(byte[] dds, int maxSize)
    {
        var info = DdsFile.ReadInfo(dds);
        if (!info.Uncompressed && info.Format is BcFormat.Bc2 or BcFormat.Bc3)
        {
            var copy = (byte[])dds.Clone();
            int step = 16;
            for (int o = info.DataOffset; o + step <= copy.Length; o += step)
            {
                if (info.Format == BcFormat.Bc3)
                {
                    copy[o] = 255; copy[o + 1] = 255; // both endpoints opaque
                    for (int k = 2; k < 8; k++) copy[o + k] = 0; // every index 0 = the first endpoint
                }
                else
                    for (int k = 0; k < 8; k++) copy[o + k] = 0xFF;
            }
            return copy;
        }

        var (rgba, w, h, _) = DdsFile.DecodeTopMip(dds);
        while (Math.Max(w, h) > maxSize && Math.Min(w, h) > 4)
            (rgba, w, h) = TreeAtlasBuilder.Downsample(rgba, w, h);
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        bool srgb = LodTextureEncoding.IsSrgb(info);
        var aligned = LodTextureEncoding.BlockAligned(new(rgba, w, h), LodTextureEncoding.Channels.Color, srgb);
        (rgba, w, h) = (aligned.Pixels, aligned.Width, aligned.Height);

        var mips = new List<byte[]> { Bc7Encoder.EncodeImage(rgba, w, h) };
        int mw = w, mh = h;
        var level = rgba;
        while (mw > 4 || mh > 4)
        {
            (level, mw, mh) = TreeAtlasBuilder.Downsample(level, mw, mh);
            mips.Add(Bc7Encoder.EncodeImage(level, mw, mh));
        }
        using var ms = new MemoryStream();
        DdsFile.WriteBc7(ms, w, h, mips, srgb: srgb);
        return ms.ToArray();
    }
}
