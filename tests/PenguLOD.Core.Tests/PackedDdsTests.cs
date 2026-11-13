using System.Buffers.Binary;
using PenguLOD.Plugins;
using PenguLOD.Testing;
using PenguLOD.Textures;
using PenguLOD.Textures.Dds;
using Mutagen.Bethesda;

namespace PenguLOD.Core.Tests;

/// <summary>16-bit packed DDS files. The Creation Kit writes terrain LOD normal maps as R5G6B5.</summary>
public sealed class PackedDdsTests
{
    private const uint AlphaPixels = 0x1, Rgb = 0x40;

    private static byte[] Legacy(int width, int height, ushort[] pixels, uint flags, uint r, uint g, uint b, uint a, int rowPadding = 0)
    {
        int pitch = width * 2 + rowPadding;
        var bytes = new byte[128 + pitch * height];
        void Set(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        Set(0, 0x20534444); Set(4, 124); Set(8, 0x100F); Set(12, (uint)height); Set(16, (uint)width); Set(20, (uint)pitch);
        Set(76, 32); Set(80, flags); Set(88, 16); Set(92, r); Set(96, g); Set(100, b); Set(104, a); Set(108, 0x1000);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(128 + y * pitch + x * 2), pixels[y * width + x]);
        return bytes;
    }

    private static byte[] Dx10(int width, int height, ushort[] pixels, uint dxgi)
    {
        var bytes = new byte[148 + width * height * 2];
        void Set(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        Set(0, 0x20534444); Set(4, 124); Set(8, 0x1007); Set(12, (uint)height); Set(16, (uint)width);
        Set(76, 32); Set(80, 0x4); "DX10"u8.CopyTo(bytes.AsSpan(84)); Set(108, 0x1000); Set(128, dxgi); Set(132, 3); Set(140, 1);
        for (int i = 0; i < pixels.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(148 + i * 2), pixels[i]);
        return bytes;
    }

    [Fact]
    public void R5G6B5IsDecodedWithEachChannelScaledToEightBits()
    {
        // Red, green, blue, white, then a mid value in every channel, and a row of padding the pitch accounts for.
        ushort[] pixels = [0xF800, 0x07E0, 0x001F, 0xFFFF, (16 << 11) | (32 << 5) | 16, 0x0000];
        var file = Legacy(3, 2, pixels, Rgb, 0xF800, 0x07E0, 0x001F, 0, rowPadding: 2);
        var info = DdsFile.ReadInfo(file);
        Assert.True(info.Uncompressed); Assert.Equal(16, info.BitsPerPixel); Assert.False(DdsFile.HasAlpha(file, info));
        DdsFile.RequireTopMip(file, info);
        var (rgba, width, height, _) = DdsFile.DecodeTopMip(file);
        Assert.Equal((3, 2), (width, height));
        Assert.Equal(new byte[]
        {
            255, 0, 0, 255,   0, 255, 0, 255,   0, 0, 255, 255,
            255, 255, 255, 255,   132, 130, 132, 255,   0, 0, 0, 255
        }, rgba);
        Assert.False(DdsAlpha.IsTranslucent(file));
        Assert.Equal(rgba, TextureImages.Decode(file).Pixels);
    }

    [Fact]
    public void PackedAlphaFormatsKeepTheirAlpha()
    {
        // A1R5G5B5: opaque red, then transparent blue.
        var a1 = Legacy(2, 1, [0x8000 | 0x7C00, 0x001F], Rgb | AlphaPixels, 0x7C00, 0x03E0, 0x001F, 0x8000);
        Assert.True(DdsFile.HasAlpha(a1, DdsFile.ReadInfo(a1)));
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 0 }, DdsFile.DecodeTopMip(a1).Rgba);
        Assert.True(DdsAlpha.IsTranslucent(a1));
        // X1R5G5B5 has the same layout without the alpha flag: its top bit is padding.
        var x1 = Legacy(2, 1, [0x7C00, 0x001F], Rgb, 0x7C00, 0x03E0, 0x001F, 0);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, DdsFile.DecodeTopMip(x1).Rgba);
        Assert.False(DdsAlpha.IsTranslucent(x1));
        // A4R4G4B4: half-transparent green.
        var a4 = Legacy(1, 1, [0x80F0], Rgb | AlphaPixels, 0x0F00, 0x00F0, 0x000F, 0xF000);
        Assert.Equal(new byte[] { 0, 255, 0, 136 }, DdsFile.DecodeTopMip(a4).Rgba);
    }

    [Fact]
    public void Dx10PackedFormatsMatchTheirLegacyLayouts()
    {
        ushort[] pixels = [0xF800, 0x07E0, 0x001F, 0x8410];
        Assert.Equal(DdsFile.DecodeTopMip(Legacy(2, 2, pixels, Rgb, 0xF800, 0x07E0, 0x001F, 0)).Rgba, DdsFile.DecodeTopMip(Dx10(2, 2, pixels, 85)).Rgba);
        Assert.False(DdsFile.HasAlpha(Dx10(2, 2, pixels, 85), DdsFile.ReadInfo(Dx10(2, 2, pixels, 85))));
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 0 }, DdsFile.DecodeTopMip(Dx10(2, 1, [0xFC00, 0x001F], 86)).Rgba);
        Assert.Equal(new byte[] { 0, 255, 0, 136 }, DdsFile.DecodeTopMip(Dx10(1, 1, [0x80F0], 115)).Rgba);
    }

    [Fact]
    public void TruncatedOrMasklessPackedFilesAreRejected()
    {
        var file = Legacy(4, 4, new ushort[16], Rgb, 0xF800, 0x07E0, 0x001F, 0);
        Assert.Throws<InvalidDataException>(() => DdsFile.DecodeTopMip(file[..^2]));
        Assert.Throws<InvalidDataException>(() => DdsFile.RequireTopMip(file[..^2], DdsFile.ReadInfo(file)));
        Assert.Throws<InvalidDataException>(() => DdsFile.DecodeTopMip(Legacy(4, 4, new ushort[16], Rgb, 0xF800, 0, 0x001F, 0)));
        Assert.Throws<InvalidDataException>(() => DdsFile.DecodeTopMip(Legacy(4, 4, new ushort[16], Rgb, 0xFF0000, 0x07E0, 0x001F, 0)));
    }

    [Fact]
    public void TerrainReadsAnExistingR5G6B5NormalMap()
    {
        using var f = new TextureFixture();
        // (16, 32, 31) of (31, 63, 31): close to the flat normal (128, 128, 255).
        var pixels = Enumerable.Repeat((ushort)((16 << 11) | (32 << 5) | 31), 16).ToArray();
        TextureFixture.Put(f.Data, @"textures\terrain\World\World.4.0.0_n.dds", Legacy(4, 4, pixels, Rgb, 0xF800, 0x07E0, 0x001F, 0));
        var materials = new TerrainMaterials(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), new(), default);
        var value = materials.Read(@"textures\terrain\world\world.4.0.0_n.dds", .5f, .5f, 1 / 4f, color: false, wrap: false);
        Assert.InRange(value.X, .51f, .53f); Assert.InRange(value.Y, .50f, .52f); Assert.Equal(1f, value.Z, 3);
    }
}
