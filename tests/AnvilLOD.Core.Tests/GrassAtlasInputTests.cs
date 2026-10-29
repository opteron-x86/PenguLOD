using System.Text;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Core.Tests;

public sealed class GrassAtlasInputTests
{
    private static Dictionary<string, LodGrid> Grids => new(StringComparer.OrdinalIgnoreCase)
    { ["TestWorld"] = new("TestWorld", new LodSettings(-4, -4, 8, 4, 8)) };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtlasRendersOnlyGrassWithInstancesInSelectedCaches(bool missingUsedTexture)
    {
        using var f = new TextureFixture();
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
        var used = mod.Grasses.AddNew(); used.Model = new Model { File = @"grass\used.nif" };
        var unused = mod.Grasses.AddNew(); unused.Model = new Model { File = @"grass\unused.nif" };
        mod.WriteToBinary(Path.Combine(f.Data, "Skyrim.esm"));
        TextureFixture.Put(f.Data, @"meshes\grass\used.nif", LodNifWriter.Write("used",
            [TextureFixture.Card("grass", missingUsedTexture ? @"textures\missing.dds" : @"textures\green.dds")]));
        TextureFixture.Put(f.Data, @"meshes\grass\unused.nif", LodNifWriter.Write("unused", [TextureFixture.Card("grass", @"textures\missing.dds")]));
        TextureFixture.Put(f.Data, @"grass\TestWorldx0000y0000.cgid", Cache((@"grass\used.nif", used.FormKey.ID, 1), (@"grass\unused.nif", unused.FormKey.ID, 0)));
        // Out-of-scope files must never be opened, even when their contents are corrupt.
        TextureFixture.Put(f.Data, @"grass\OtherWorldx0000y0000.cgid", [1,2,3]);
        TextureFixture.Put(f.Data, @"grass\TestWorldx0099y0099.cgid", [1,2,3]);
        using var game = GameContext.Open(f.Game);
        var source = new GrassLodSource(game, game.BuildAssets(), 1, 1, f.Output);
        var cell = Assert.Single(source.Discover(Grids));
        if (missingUsedTexture)
        {
            Assert.Throws<FileNotFoundException>(() => source.PrepareAtlas());
            Assert.False(File.Exists(GamePath.ToFileSystemPath(f.Output, GrassLodSource.AtlasDiffuse)));
        }
        else
        {
            source.PrepareAtlas();
            Assert.Equal(1, source.Atlas!.Tiles); Assert.Equal(1, source.BillboardsRendered);
            Assert.All(source.Build(cell.Meshes.Lod4!)!.Parts, p => Assert.Equal(GrassLodSource.AtlasDiffuse, p.Material.Textures[0]));
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void SeasonalAtlasTypesRequireEnabledSeasonsAndAMatchingBaseCell(bool seasons, bool matchingCell)
    {
        using var f = new TextureFixture();
        // No GRAS records are needed: cache model fallback must still be atlased.
        TextureFixture.Put(f.Data, @"meshes\grass\used.nif", LodNifWriter.Write("used", [TextureFixture.Card("grass", @"textures\green.dds")]));
        TextureFixture.Put(f.Data, @"meshes\grass\winter.nif", LodNifWriter.Write("winter", [TextureFixture.Card("grass", @"textures\red.dds")]));
        TextureFixture.Put(f.Data, @"grass\TestWorldx0000y0000.cgid", Cache((@"grass\used.nif", 0x12345678, 1)));
        TextureFixture.Put(f.Data, matchingCell ? @"grass\TestWorldx0000y0000.WIN.cgid" : @"grass\TestWorldx0001y0000.WIN.cgid",
            Cache((@"grass\winter.nif", 0x12345679, 1)));
        using var game = GameContext.Open(f.Game);
        var source = new GrassLodSource(game, game.BuildAssets(), 1, 1, f.Output);
        var cell = Assert.Single(source.Discover(Grids, seasons)); source.PrepareAtlas();
        Assert.Equal(seasons && matchingCell ? 2 : 1, source.Atlas!.Tiles);
        if (seasons && matchingCell)
        {
            var winter = source.SeasonalVariant(cell, "WIN")!;
            Assert.All(source.Build(winter.Meshes.Lod4!)!.Parts, p => Assert.Equal(GrassLodSource.AtlasDiffuse, p.Material.Textures[0]));
        }
        else Assert.Null(source.SeasonalVariant(cell, "WIN"));
    }

    [Fact]
    public void TypeMetadataChecksPayloadBoundsWithoutReadingEveryBlade()
    {
        var bytes = Cache((@"grass\a.nif", 17, 3), (@"grass\b.nif", 19, 0), (@"grass\c.nif", 23, 7));
        using var seekable = new CountingStream(bytes, true);
        var types = GrassCacheValidation.ReadTypes(seekable);
        Assert.Equal(new[] { 3L, 0L, 7L }, types.Select(t => t.Instances));
        Assert.Equal(new uint[] { 17, 19, 23 }, types.Select(t => t.RuntimeFormId));
        Assert.Equal(new[] { @"grass\a.nif", @"grass\b.nif", @"grass\c.nif" }, types.Select(t => t.Model));
        Assert.Equal(bytes.Length - 10 * 32, seekable.BytesRead);
        using var streaming = new CountingStream(bytes, false);
        Assert.Equal(types, GrassCacheValidation.ReadTypes(streaming));
        Assert.Throws<EndOfStreamException>(() => GrassCacheValidation.ReadTypes(new MemoryStream(bytes[..^1])));
        Assert.Throws<InvalidDataException>(() => GrassCacheValidation.ReadTypes(new MemoryStream([..bytes, 1])));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => GrassCacheValidation.ReadTypes(new MemoryStream(bytes), cancelled.Token));
    }

    private static byte[] Cache(params (string Model, uint Id, int Count)[] types)
    {
        using var bytes = new MemoryStream(); using var w = new BinaryWriter(bytes);
        w.Write(types.Length);
        foreach (var t in types)
        {
            byte[] model = Encoding.ASCII.GetBytes(t.Model + "\0"); w.Write(model.Length); w.Write(model);
            w.Write(1f); w.Write(t.Id); w.Write(new byte[] {1,1,0}); w.Write(1);
            for (int i = 0; i < 7; i++) w.Write(0f); w.Write(t.Count); w.Write(16);
            for (int i = 0; i < t.Count; i++)
                for (int j = 0; j < 16; j++) w.Write((Half)(j is 3 or 4 ? 1 : 0));
        }
        return bytes.ToArray();
    }

    private sealed class CountingStream(byte[] bytes, bool seekable) : Stream
    {
        private readonly MemoryStream _stream = new(bytes);
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => seekable;
        public override bool CanWrite => false;
        public override long Length => seekable ? _stream.Length : throw new NotSupportedException();
        public override long Position { get => _stream.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) { int n = _stream.Read(buffer); BytesRead += n; return n; }
        public override long Seek(long offset, SeekOrigin origin) => seekable ? _stream.Seek(offset, origin) : throw new NotSupportedException();
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _stream.Dispose(); base.Dispose(disposing); }
    }
}
