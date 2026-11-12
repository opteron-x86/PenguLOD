using System.Buffers.Binary;
using PenguLOD.Core.Textures;
using PenguLOD.Textures;
using PenguLOD.Textures.Dds;

namespace PenguLOD.Core.Tests;

/// <summary>BC output must have a top level that is a multiple of four: Direct3D 11 will not create the texture otherwise.</summary>
public class TextureSizingTests
{
    private static byte[] Header(int width, int height, int mips, uint pfFlags, uint fourCc = 0, int bits = 0,
        uint r = 0, uint g = 0, uint b = 0, uint a = 0, int? dxgi = null)
    {
        var h = new byte[dxgi is null ? 128 : 148];
        BinaryPrimitives.WriteUInt32LittleEndian(h, 0x20534444); BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(8), 0x1007 | (mips > 1 ? 0x20000u : 0));
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(12), height); BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(16), width);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(28), mips); BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(76), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(80), pfFlags); BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(84), fourCc);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(88), bits);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(92), r); BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(96), g);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(100), b); BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(104), a);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(108), 0x1000);
        if (dxgi is { } format)
        {
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(128), format); BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(132), 3);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(140), 1);
        }
        return h;
    }

    /// <summary>BC3 with a full authored mip chain.</summary>
    private static byte[] Bc3(int size)
    {
        var bytes = new List<byte>();
        int mips = 0;
        for (int s = size; ; s = Math.Max(1, s / 2)) { mips++; bytes.AddRange(new byte[((s + 3) / 4) * ((s + 3) / 4) * 16]); if (s == 1) break; }
        return [.. Header(size, size, mips, 0x4, 0x35545844), .. bytes];
    }

    /// <summary>Legacy 32-bit ARGB with real alpha, every texel half transparent.</summary>
    private static byte[] Argb(int width, int height, byte alpha = 128)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 40; pixels[i + 1] = 160; pixels[i + 2] = 220; pixels[i + 3] = alpha; }
        return [.. Header(width, height, 1, 0x41, 0, 32, 0xFF0000, 0xFF00, 0xFF, 0xFF000000), .. pixels];
    }

    private static (int Width, int Height) Size(byte[] dds) { var info = DdsFile.ReadInfo(dds); return (info.Width, info.Height); }

    [Fact]
    public void AuthoredMipsThatAreNotBlockAlignedAreNotUsedAsTheTopLevel()
    {
        // 1500 → 750: the first level within 1024 pixels is 750×750, which the game could not load.
        Assert.Null(LodTextureEncoding.Optimize(Bc3(1500), 1024, LodTextureEncoding.Channels.Color));
        // 256 → 128 stays byte-exact slicing.
        var sliced = LodTextureEncoding.Optimize(Bc3(256), 128, LodTextureEncoding.Channels.Color)!;
        Assert.Equal((128, 128), Size(sliced));
    }

    [Theory]
    [InlineData(10, 10, 12, 12)]
    [InlineData(250, 250, 252, 252)]
    [InlineData(6, 13, 8, 12)]
    public void EncodedTexturesAreResizedToBlockMultiples(int width, int height, int expectedWidth, int expectedHeight)
    {
        var encoded = LodTextureEncoding.Optimize(Argb(width, height), 4096, LodTextureEncoding.Channels.Color);
        if (encoded is not null) Assert.Equal((expectedWidth, expectedHeight), Size(encoded));
        Assert.Equal((expectedWidth, expectedHeight), Size(LodTextureEncoding.Encode(new(new byte[width * height * 4], width, height), LodTextureEncoding.Channels.Color)));
        Assert.Equal((expectedWidth, expectedHeight), Size(DdsAlpha.MakeOpaque(Argb(width, height), 4096)));
        Assert.Equal((expectedWidth, expectedHeight), Size(PbrLodConverter.Convert(Argb(width, height, 255), 4096, 1)));
    }

    [Fact]
    public void OpaqueCopiesOfSrgbTexturesStaySrgb()
    {
        // DXGI 29 (R8G8B8A8_UNORM_SRGB) was missing from the list; 93 is never returned by the reader.
        var pixels = new byte[8 * 8 * 4]; for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 100;
        var srgb = DdsAlpha.MakeOpaque([.. Header(8, 8, 1, 0x4, 0x30315844, dxgi: 29), .. pixels], 4096);
        Assert.Equal(99, DdsFile.ReadInfo(srgb).DxgiFormat);
    }

    [Fact]
    public void TilesWithAnotherAspectKeepTheResolutionOfTheirLongAxis()
    {
        // Two-texel-wide vertical stripes on a 512×512 source, drawn into a 256×128 tile: horizontally that is a 2:1 reduction,
        // so the stripes must survive as one-texel stripes. Halving both axes together blurred them to grey.
        var pixels = new byte[512 * 512 * 4];
        for (int y = 0; y < 512; y++)
            for (int x = 0; x < 512; x++) { int o = (y * 512 + x) * 4; byte v = (byte)((x / 2) % 2 == 0 ? 0 : 255); pixels[o] = pixels[o + 1] = pixels[o + 2] = v; pixels[o + 3] = 255; }
        var target = new TextureImage(new byte[256 * 128 * 4], 256, 128);
        var tile = new TextureTile("stripes", "atlas", 1, 1, 0, 0, 1, 1, 0, 1, 1, 1, false);
        TextureImages.Blit(target, new(pixels, 512, 512), tile with { Width = 2, Height = 1 }, 128, normal: false);
        int contrast = 0;
        for (int x = 0; x + 1 < 256; x++) contrast = Math.Max(contrast, Math.Abs(target.Pixels[(64 * 256 + x) * 4] - target.Pixels[(64 * 256 + x + 1) * 4]));
        Assert.True(contrast > 200, $"strongest neighbouring difference {contrast}");
    }
}
