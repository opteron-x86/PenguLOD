using System.Buffers.Binary;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Textures.Bc;
using AnvilLOD.Textures.Dds;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace AnvilLOD.Textures;

/// <summary>Portable BC6H repair. Preserve every source mip; clamp HDR to the game's LDR range.</summary>
public static class NativeTextureConverter
{
    public static byte[] Bc6ToBc7(byte[] source)
    {
        int Read(int offset) => BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset));
        if (source.Length < 148 || !source.AsSpan(0, 4).SequenceEqual("DDS "u8) || !source.AsSpan(84, 4).SequenceEqual("DX10"u8))
            throw new InvalidDataException("BC6H conversion requires a complete DX10 DDS header.");
        int format = Read(128), width = Read(16), height = Read(12), mipCount = Math.Max(1, Read(28));
        if (format is not (95 or 96)) throw new NotSupportedException("Only BC6H DDS textures can be repaired by this converter.");
        if (Read(132) != 3 || Read(140) != 1 || (Read(136) & 4) != 0 || (Read(112) & 0x200) != 0 || Read(24) > 1)
            throw new NotSupportedException("Texture arrays, cube maps and volume textures are not supported by native BC6H repair.");
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || mipCount > 1 + (int)Math.Log2(Math.Max(width, height)))
            throw new InvalidDataException("Invalid DDS dimensions or mip count.");
        long expected = 148;
        for (int i = 0; i < mipCount; i++) expected += ((Math.Max(1, width >> i) + 3L) / 4) * ((Math.Max(1, height >> i) + 3L) / 4) * 16;
        if (source.LongLength != expected) throw new InvalidDataException("BC6H data length does not match its dimensions and mip chain.");
        var decoder = new BCnEncoder.Decoder.BcDecoder();
        decoder.Options.IsParallel = false; // Encoding below observes the per-job worker limit.
        var mips = new List<byte[]>(mipCount);
        int offset = 148;
        for (int mip = 0; mip < mipCount; mip++)
        {
            RunLimits.Check();
            int w = Math.Max(1, width >> mip), h = Math.Max(1, height >> mip), bytes = ((w + 3) / 4) * ((h + 3) / 4) * 16;
            var pixels = decoder.DecodeRawHdr(source.AsSpan(offset, bytes).ToArray(), w, h, format == 95 ? CompressionFormat.Bc6U : CompressionFormat.Bc6S);
            offset += bytes;
            var rgba = new byte[checked(w * h * 4)];
            static byte Ldr(float value) => (byte)MathF.Round(Math.Clamp(float.IsNaN(value) ? 0 : value, 0, 1) * 255);
            for (int i = 0; i < pixels.Length; i++)
            { rgba[i * 4] = Ldr(pixels[i].r); rgba[i * 4 + 1] = Ldr(pixels[i].g); rgba[i * 4 + 2] = Ldr(pixels[i].b); rgba[i * 4 + 3] = 255; }
            mips.Add(Bc7Encoder.EncodeImage(rgba, w, h));
        }
        using var output = new MemoryStream();
        DdsFile.WriteBc7(output, width, height, mips);
        return output.ToArray();
    }
}
