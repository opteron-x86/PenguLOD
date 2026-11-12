using System.Buffers.Binary;
using PenguLOD.Core.Textures;
using PenguLOD.Core.World;
using PenguLOD.Meshes.Authoring;
using PenguLOD.Meshes.Nif;
using PenguLOD.Testing;
using PenguLOD.Textures;
using PenguLOD.Textures.Bc;
using PenguLOD.Textures.Dds;

namespace PenguLOD.Core.Tests;

/// <summary>Malformed mod files must surface as InvalidDataException or NotSupportedException so callers report them.</summary>
public class MalformedInputTests
{
    private static byte[] Nif() => LodNifWriter.Write("PL:objects:4", [TextureFixture.Card("card", @"textures\red.dds")]);

    private static byte[] Dds(int size = 8)
    {
        var rgba = new byte[size * size * 4];
        for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 200; rgba[i + 3] = 255; }
        using var ms = new MemoryStream(); DdsFile.WriteBc7(ms, size, size, [Bc7Encoder.EncodeImage(rgba, size, size)]);
        return ms.ToArray();
    }

    private static void ExpectMalformed(Action read)
    {
        try { read(); }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException) { }
    }

    [Fact]
    public void TruncatedNifsFailAsMalformedAtEveryLength()
    {
        var nif = Nif();
        for (int n = 0; n < nif.Length; n++)
        {
            var cut = nif[..n];
            ExpectMalformed(() => NifFile.Read(cut));
            ExpectMalformed(() => NifGeometryReader.Read("cut.nif", cut));
        }
    }

    [Fact]
    public void AbsurdNifCountsFailAsMalformed()
    {
        var nif = Nif();
        int header = Array.IndexOf(nif, (byte)'\n') + 1;
        // num blocks follows version (4), endian (1) and user version (4).
        var blocks = (byte[])nif.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(blocks.AsSpan(header + 9), 0xF0000000);
        Assert.Throws<InvalidDataException>(() => NifFile.Read(blocks));
        Assert.Throws<InvalidDataException>(() => NifGeometryReader.Read("blocks.nif", blocks));
        // Overwrite every 4-byte window with a huge count: no overflow or range exception may escape.
        for (int at = header; at + 4 <= Math.Min(nif.Length, header + 400); at++)
        {
            var bad = (byte[])nif.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(at), 0x7FFFFFFF);
            ExpectMalformed(() => NifGeometryReader.Read("bad.nif", bad));
        }
    }

    [Fact]
    public void TruncatedDdsFilesFailAsMalformed()
    {
        var dds = Dds();
        for (int n = 0; n < dds.Length; n++)
        {
            var cut = dds[..n];
            ExpectMalformed(() => DdsFile.DecodeTopMip(cut));
            ExpectMalformed(() => DdsAlpha.IsTranslucent(cut));
        }
        var shortPayload = dds[..(dds.Length - 4)];
        Assert.Throws<InvalidDataException>(() => DdsFile.DecodeTopMip(shortPayload));
        Assert.Throws<InvalidDataException>(() => DdsFile.RequireTopMip(shortPayload, DdsFile.ReadInfo(shortPayload)));
    }

    [Fact]
    public void ATruncatedBillboardIsReportedWithoutStoppingTheAtlas()
    {
        var good = Dds(); var bad = good[..(good.Length - 4)];
        var atlas = new TreeAtlasBuilder().Build([new("good", good), new("bad", bad)]);
        Assert.Contains("bad", atlas.Errors.Keys);
        Assert.Contains("good", atlas.Rects.Keys);
        Assert.DoesNotContain("bad", atlas.Rects.Keys);
    }

    [Fact]
    public void TruncatedGrassCachesFailAsMalformed()
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write(1u); var model = "meshes\\grass.nif\0"u8.ToArray(); w.Write((uint)model.Length); w.Write(model);
        w.Write(0f); w.Write(0x123u); w.Write(new byte[3]); w.Write(1u);
        w.Write(0f); w.Write(0f); w.Write(0f); w.Write(new byte[16]); w.Write(1u); w.Write(6u); w.Write(new byte[12]);
        var cache = ms.ToArray();
        Assert.Single(GrassCache.Read(cache));
        for (int n = 4; n < cache.Length; n++) Assert.Throws<InvalidDataException>(() => GrassCache.Read(cache.AsSpan(0, n)));
        var negative = (byte[])cache.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(negative.AsSpan(4 + 4 + model.Length + 4 + 4 + 3 + 4 + 28), 0xFFFFFFFF);
        Assert.Throws<InvalidDataException>(() => GrassCache.Read(negative));
    }

    [Fact]
    public void LegacyX8AndRgbTexturesAreOpaque()
    {
        // 32-bit X8R8G8B8 without the alpha-pixels flag, padding byte zero.
        var x8 = new byte[128 + 4 * 4 * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(x8, 0x20534444); BinaryPrimitives.WriteInt32LittleEndian(x8.AsSpan(4), 124);
        BinaryPrimitives.WriteInt32LittleEndian(x8.AsSpan(12), 4); BinaryPrimitives.WriteInt32LittleEndian(x8.AsSpan(16), 4);
        BinaryPrimitives.WriteInt32LittleEndian(x8.AsSpan(76), 32); BinaryPrimitives.WriteUInt32LittleEndian(x8.AsSpan(80), 0x40);
        BinaryPrimitives.WriteInt32LittleEndian(x8.AsSpan(88), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(x8.AsSpan(92), 0xFF0000); BinaryPrimitives.WriteUInt32LittleEndian(x8.AsSpan(96), 0xFF00);
        BinaryPrimitives.WriteUInt32LittleEndian(x8.AsSpan(100), 0xFF);
        Assert.False(DdsAlpha.IsTranslucent(x8));
        Assert.All(DdsFile.DecodeTopMip(x8).Rgba.Where((_, i) => i % 4 == 3), a => Assert.Equal(255, a));
        // With the flag and an alpha mask, the same zero bytes are real transparency.
        BinaryPrimitives.WriteUInt32LittleEndian(x8.AsSpan(80), 0x41); BinaryPrimitives.WriteUInt32LittleEndian(x8.AsSpan(104), 0xFF000000);
        Assert.True(DdsAlpha.IsTranslucent(x8));

        var rgb = new byte[128 + 4 * 4 * 3];
        x8.AsSpan(0, 128).CopyTo(rgb);
        BinaryPrimitives.WriteUInt32LittleEndian(rgb.AsSpan(80), 0x40); BinaryPrimitives.WriteInt32LittleEndian(rgb.AsSpan(88), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(rgb.AsSpan(104), 0);
        Assert.False(DdsAlpha.IsTranslucent(rgb));
    }

    [Fact]
    public void ATextureTheConverterCannotReadDoesNotStopTheBatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "pengulod-malformed-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "textures"));
            File.WriteAllBytes(Path.Combine(root, "textures", "a.dds"), [1]); File.WriteAllBytes(Path.Combine(root, "textures", "b.dds"), [2]);
            var items = new[] { new BadTexture(root, Path.Combine("textures", "a.dds"), "mod", 95, "BC6H", 4, 4, 1, true),
                new BadTexture(root, Path.Combine("textures", "b.dds"), "mod", 95, "BC6H", 4, 4, 1, true) };
            var results = TextureFixer.Fix(items, "", Path.Combine(root, "out"), null, dryRun: false,
                nativeConverter: _ => throw new InvalidDataException("BC6H chain length does not match"));
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Contains("chain length", r.Status));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
