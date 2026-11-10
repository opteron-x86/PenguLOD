using System.Buffers.Binary;
using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using AnvilLOD.Textures.Dds;

namespace AnvilLOD.Core.Tests;

public class ObjectTextureOptimizationTests
{
    internal static byte[] Raw(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var bytes = new byte[128 + width * height * 4];
        void U(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        U(0, 0x20534444); U(4, 124); U(8, 0x100F); U(12, (uint)height); U(16, (uint)width); U(20, (uint)width * 4);
        U(28, 1); U(76, 32); U(80, 0x41); U(88, 32); U(92, 0xFF); U(96, 0xFF00); U(100, 0xFF0000); U(104, 0xFF000000); U(108, 0x1000);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            var c = pixel(x, y); int i = 128 + (y * width + x) * 4;
            bytes[i] = c.R; bytes[i + 1] = c.G; bytes[i + 2] = c.B; bytes[i + 3] = c.A;
        }
        return bytes;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrimmingCompressedMipsIsByteExactAndPreservesColorSpace(bool srgb)
    {
        var image = TextureImages.Decode(Raw(256, 128, (x, y) => ((byte)x, (byte)y, (byte)50, (byte)255)));
        var bytes = LodTextureEncoding.Encode(image, LodTextureEncoding.Channels.Color, srgb);
        var optimized = LodTextureEncoding.Optimize(bytes, 128, LodTextureEncoding.Channels.Color)!;
        var before = DdsFile.ReadInfo(bytes); var after = DdsFile.ReadInfo(optimized);
        Assert.Equal(128, after.Width); Assert.Equal(64, after.Height); Assert.Equal(before.MipCount - 1, after.MipCount);
        Assert.Equal(before.DxgiFormat, after.DxgiFormat);
        Assert.Equal(bytes[(148 + 256 * 128)..], optimized[148..]);
        Assert.Null(LodTextureEncoding.Optimize(optimized, 128, LodTextureEncoding.Channels.Color));
    }

    [Fact]
    public void RawNormalsKeepGlossAlphaAndGetNormalizedMips()
    {
        var bytes = Raw(256, 128, (x, _) => (180, 180, 230, (byte)(x % 2 == 0 ? 40 : 120)));
        var optimized = LodTextureEncoding.Optimize(bytes, 128, LodTextureEncoding.Channels.Normal)!;
        var image = TextureImages.Decode(optimized); var info = DdsFile.ReadInfo(optimized);
        Assert.Equal(8, info.MipCount); Assert.Equal(128, image.Width); Assert.Equal(64, image.Height);
        Assert.InRange(image.Pixels[3], 76, 84);
        var n = new Vector3(image.Pixels[0], image.Pixels[1], image.Pixels[2]) / 127.5f - Vector3.One;
        Assert.InRange(n.Length(), .96f, 1.04f);
        Assert.Equal((byte)40, bytes[131]); // source was not modified
    }

    [Fact]
    public void PackedDataChannelsAreFilteredIndependentlyOfAlpha()
    {
        var image = TextureImages.Decode(Raw(256, 256, (x, _) => x % 2 == 0 ? ((byte)0, (byte)40, (byte)200, (byte)0) : ((byte)200, (byte)80, (byte)0, (byte)255)));
        var down = LodTextureEncoding.Limit(image, 128, LodTextureEncoding.Channels.Data);
        Assert.InRange(down.Pixels[0], 99, 101); Assert.InRange(down.Pixels[1], 59, 61);
        Assert.InRange(down.Pixels[2], 99, 101); Assert.InRange(down.Pixels[3], 127, 128);
    }

    [Fact]
    public void LinearLightFilteringHandlesRectangularTexturesWithoutRecursion()
    {
        var image = TextureImages.Decode(Raw(4, 3, (x, _) => x % 2 == 0 ? ((byte)0, (byte)0, (byte)0, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255)));
        var down = LodTextureEncoding.Down(image, LodTextureEncoding.Channels.Color, true);
        Assert.Equal(2, down.Width); Assert.Equal(1, down.Height); Assert.InRange(down.Pixels[0], 186, 189);
    }

    [Fact]
    public void UnsupportedCubemapsAndTruncatedMipChainsAreRejected()
    {
        var bytes = Raw(256, 256, (_, _) => (255, 0, 0, 255));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(112), 0x200);
        Assert.Throws<NotSupportedException>(() => LodTextureEncoding.Optimize(bytes, 128, LodTextureEncoding.Channels.Color));
        var compressed = LodTextureEncoding.Encode(TextureImages.Blank(256, 256), LodTextureEncoding.Channels.Color);
        Assert.Throws<InvalidDataException>(() => LodTextureEncoding.Optimize(compressed[..^1], 128, LodTextureEncoding.Channels.Color));
    }

    [Fact]
    public void PrivateCopiesPreserveUvsAndSourceFilesAndRepairDamagedOutput()
    {
        using var f = new TextureFixture();
        var bytes = Raw(256, 256, (_, _) => (220, 30, 10, 255));
        TextureFixture.Put(f.Data, @"textures\lod\Large.dds", bytes);
        using var game = GameContext.Open(f.Game); var assets = game.BuildAssets(_ => true);
        var part = TextureFixture.Card("solid", @"textures\lod\large.dds");
        var mesh = new LodMesh { Path = "fixture", Parts = [part] };
        var result = ObjectTextureOptimizationStage.Build([mesh], assets, f.Output, 128);
        var mapped = Assert.Single(result.Transform(mesh).Parts);
        Assert.Same(part.UVs, mapped.UVs); Assert.Same(part.Positions, mapped.Positions); Assert.Same(part.Triangles, mapped.Triangles);
        Assert.StartsWith(@"textures\lod\pengulod\object-textures\", mapped.Material.Textures[0]);
        Assert.Equal(BtoBuilder.ShapeNameFor(LodShaderKind.Snow, part.Material, LodLevel.Lod4), BtoBuilder.ShapeNameFor(LodShaderKind.Snow, mapped.Material, LodLevel.Lod4));
        Assert.Equal(bytes, File.ReadAllBytes(GamePath.ToFileSystemPath(f.Data, @"textures\lod\Large.dds")));
        string copy = GamePath.ToFileSystemPath(f.Output, mapped.Material.Textures[0]); var expected = File.ReadAllBytes(copy);
        File.WriteAllBytes(copy, [1, 2, 3]);
        var rebuilt = ObjectTextureOptimizationStage.Build([mesh], assets, f.Output, 128);
        Assert.Equal(mapped.Material, rebuilt.Transform(mesh).Parts[0].Material); Assert.Equal(expected, File.ReadAllBytes(copy));
        Assert.True(result.Report.OutputBytes < result.Report.SourceBytes / 4);
        Assert.Empty(result.Report.Warnings);
    }

    [Fact]
    public void UnsupportedInputOnlyFallsBackForAffectedTexture()
    {
        using var f = new TextureFixture();
        TextureFixture.Put(f.Data, @"textures\large.dds", Raw(256, 256, (_, _) => (90, 120, 60, 255)));
        TextureFixture.Put(f.Data, @"textures\bad.dds", [1, 2, 3, 4]);
        using var game = GameContext.Open(f.Game); var assets = game.BuildAssets(_ => true);
        var bad = TextureFixture.Card("bad", @"textures\bad.dds");
        var mesh = new LodMesh { Path = "fixture", Parts = [bad, TextureFixture.Card("good", @"textures\large.dds")] };
        var result = ObjectTextureOptimizationStage.Build([mesh], assets, f.Output, 128);
        Assert.Same(bad, result.Transform(mesh).Parts[0]); Assert.NotEqual(mesh.Parts[1].Material, result.Transform(mesh).Parts[1].Material);
        Assert.Single(result.Report.Warnings); Assert.Contains("bad.dds", result.Report.Warnings[0]);
    }

    [Fact]
    public void CopyWriteFailureNeverPublishesAnUnwrittenTextureReference()
    {
        using var f = new TextureFixture();
        TextureFixture.Put(f.Data, @"textures\large.dds", Raw(256, 256, (_, _) => (90, 120, 60, 255)));
        TextureFixture.Put(f.Output, @"textures\pengulod\object-textures", [1]); // a file blocks the generated directory
        using var game = GameContext.Open(f.Game); var assets = game.BuildAssets(_ => true);
        var part = TextureFixture.Card("good", @"textures\large.dds"); var mesh = new LodMesh { Path = "fixture", Parts = [part] };
        var result = ObjectTextureOptimizationStage.Build([mesh], assets, f.Output, 128);
        Assert.Same(part, result.Transform(mesh).Parts[0]); Assert.Equal(0, result.Report.RewrittenTextures);
        Assert.Single(result.Report.Warnings);
    }

    [Fact]
    public void RgbxInputGetsOpaqueAlphaAndUnchangedCoverageDoesNotLowerLeafAlpha()
    {
        var rgbx = Raw(8, 8, (_, _) => (80, 100, 90, 0));
        BinaryPrimitives.WriteUInt32LittleEndian(rgbx.AsSpan(104), 0);
        Assert.Equal((byte)255, LodTextureEncoding.Decode(rgbx).Pixels[3]);
        var leaves = TextureImages.Decode(Raw(16, 16, (x, _) => (80, 100, 90, (byte)(x < 8 ? 255 : 0))));
        var limited = LodTextureEncoding.Limit(leaves, 128, LodTextureEncoding.Channels.Color, alphaCutoff: 86);
        Assert.Equal((byte)255, limited.Pixels[3]);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(86)]
    [InlineData(128)]
    [InlineData(201)]
    public void AtlasCutoutCoverageUsesTheActualThresholdAcrossMips(int cutoff)
    {
        var source = TextureImages.Decode(Raw(128, 128, (x, _) => (120, 150, 60, (byte)(x < 64 ? Math.Min(255, cutoff + 30) : 0))));
        var atlas = ObjectTextureAtlas.Build([new("cutout", source, null, true, 3, cutoff)], 64, 64);
        var bytes = atlas.Pages[0].Diffuse; var info = DdsFile.ReadInfo(bytes); int offset = info.DataOffset, size = info.Width;
        for (int mip = 0; mip < info.MipCount; mip++)
        {
            int length = ((size + 3) / 4) * ((size + 3) / 4) * 16;
            var pixels = AnvilLOD.Textures.Bc.BcDecoder.DecodeImage(bytes.AsSpan(offset, length), size, size, info.Format);
            int visible = Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] >= cutoff);
            double coverage = visible / (double)(pixels.Length / 4);
            // The wrapped left/right boundaries have equal alpha. At 8px both columns cross together;
            // 3/8 or 5/8 is the closest representable coverage to 1/2, with no dithering or geometry change.
            Assert.InRange(coverage, .5 - 1d / size - .005, .5 + 1d / size + .005);
            offset += length; size /= 2;
        }
    }
}
