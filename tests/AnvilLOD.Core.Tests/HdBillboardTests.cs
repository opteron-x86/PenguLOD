using System.Numerics;
using System.Text;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using AnvilLOD.Textures.Dds;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Core.Tests;

public class HdBillboardTests
{
    private static LodMesh Asymmetric()
    {
        var front = TextureFixture.Card("front", @"textures\red.dds");
        var side = TextureFixture.Card("side", @"textures\green.dds");
        return new() { Path = @"meshes\trees\testtree.nif", Parts = [front, new()
        {
            Name = "side", Material = side.Material,
            Positions = side.Positions.Select(p => new Vector3(-200, p.X * .5f, p.Z)).ToArray(),
            UVs = side.UVs, Normals = Enumerable.Repeat(Vector3.UnitX, 4).ToArray(),
            Tangents = Enumerable.Repeat(Vector3.UnitY, 4).ToArray(), Bitangents = side.Bitangents, Triangles = side.Triangles
        }] };
    }
    private static (byte[], int, int) Color(string path) =>
        (path.Contains("red") ? new byte[] { 240, 10, 10, 255 } : new byte[] { 10, 240, 10, 255 }, 1, 1);
    private static IEnumerable<int> Visible(byte[] image) => Enumerable.Range(0, image.Length / 4).Where(i => image[i * 4 + 3] > 128);

    [Fact]
    public void ViewsHaveIndependentSilhouettesAndRotateTheNormalBasis()
    {
        var front = BillboardRenderer.Render(Asymmetric(), p => Color(p), 128, unitsPerPixel: 12, originCentered: true, shaded: false)!;
        var side = BillboardRenderer.Render(Asymmetric(), p => Color(p), 128, unitsPerPixel: 12, originCentered: true, shaded: false, view: BillboardView.Side)!;
        Assert.Equal(800, front.WorldWidth); Assert.Equal(400, side.WorldWidth);
        Assert.All(Visible(front.Rgba), i => { Assert.True(front.Rgba[i * 4] > 200); Assert.True(i % front.Width >= front.Width / 2); });
        Assert.All(Visible(side.Rgba), i => { Assert.True(side.Rgba[i * 4 + 1] > 200); Assert.True(i % side.Width >= side.Width / 2); });
        foreach (var image in new[] { front, side })
            Assert.All(Visible(image.Rgba), i => Assert.InRange(image.NormalRgba![i * 4 + 2], (byte)248, (byte)255));
    }

    [Fact]
    public void TextureStagePublishesBothViewsAndCardsConsumeTheirOwnDimensionsAndTextures()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        TextureFixture.Put(f.Data, Asymmetric().Path, LodNifWriter.Write("tree", Asymmetric().Parts));
        var request = f.Request with { Stitched = false, Trees = true, BillboardMaxSize = 128 };
        var result = TextureGeneration.Run(request); Assert.True(result.Published);
        var item = Assert.Single(result.Items, i => i.Status == "Written");
        string stem = item.Output[..^4];
        Assert.True(File.Exists(GamePath.ToFileSystemPath(f.Output, stem + "_2_n.dds")));
        using var game = GameContext.Open(f.Game); var assets = game.BuildAssets(_ => true);
        TextureGenerationManifest.Read(f.Output, true)!.Attach(assets, f.Output);
        var source = new TreeCardSource(assets); var cards = source.Build(TreeCardSource.Prefix + item.Output)!;
        Assert.Equal(2, cards.Parts.Count);
        Assert.EndsWith("_1.dds", cards.Parts[0].Material.Textures[0]); Assert.EndsWith("_2.dds", cards.Parts[1].Material.Textures[0]);
        Assert.Equal(800, cards.Parts[0].Positions.Max(p => p.X) - cards.Parts[0].Positions.Min(p => p.X));
        Assert.Equal(400, cards.Parts[1].Positions.Max(p => p.Y) - cards.Parts[1].Positions.Min(p => p.Y));
        string before = source.Fingerprint(TreeCardSource.Prefix + item.Output)!;
        var originalSide = File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, stem + "_2.dds"));
        TextureFixture.Put(f.Output, stem + "_2.dds", TextureFixture.Solid(20, 20, 220));
        var nextAssets = game.BuildAssets(_ => true); TextureGenerationManifest.Read(f.Output, true)!.Attach(nextAssets, f.Output);
        Assert.NotEqual(before, new TreeCardSource(nextAssets).Fingerprint(TreeCardSource.Prefix + item.Output));
        TextureFixture.Put(f.Output, stem + "_2.dds", originalSide);
        // Disabling HD views transactionally removes the side resources from an earlier complete run.
        var basic = TextureGeneration.Run(request with { Billboards = new(HdViews: false) }); Assert.True(basic.Published);
        Assert.False(File.Exists(GamePath.ToFileSystemPath(f.Output, stem + "_2.dds")));
    }

    [Fact]
    public void NativeFallbackCacheRepairsMissingSideResources()
    {
        using var f = new TextureFixture();
        TextureFixture.Put(f.Data, Asymmetric().Path, LodNifWriter.Write("tree", Asymmetric().Parts));
        using var game = GameContext.Open(f.Game); var assets = game.BuildAssets(_ => true);
        var synth = new TreeBillboardSynth(assets, Path.Combine(f.Root, "cache"));
        Assert.True(synth.TryRender(Asymmetric().Path, "skyrim.esm", "fixture", out var error), error);
        string side = Path.Combine(f.Root, "cache", "textures", "terrain", "lodgen", "skyrim.esm", "fixture_2_n.dds");
        Assert.True(File.Exists(side)); File.Delete(side);
        Assert.True(synth.TryRender(Asymmetric().Path, "skyrim.esm", "fixture", out error), error);
        Assert.True(File.Exists(side));
    }

    [Fact]
    public void PlacedScaleChangesResolutionAndExplicitRulesOverrideItWithoutChangingWorldSize()
    {
        FormKey key = default;
        using var f = new TerrainFixture(seasons: true, editMod: mod =>
        {
            var tree = mod.Trees.Single(t => t.EditorID == "FixtureTree"); key = tree.FormKey;
            var placed = mod.Worldspaces.Single().SubCells[0].Items[0].Items.SelectMany(c => c.Temporary).OfType<PlacedObject>().Single();
            placed.Scale = 4;
        });
        using var limits = RunLimits.Enter(2);
        var request = f.Textures.Request with { Stitched = false, Trees = true, BillboardMaxSize = 1024, Billboards = new(HdViews: false) };
        var scaled = TextureGeneration.Run(request); Assert.True(scaled.Published);
        var item = Assert.Single(scaled.Items, i => i.Source.EndsWith(@"\testtree.nif"));
        Assert.Equal((512, 256), (item.Width, item.Height));
        File.WriteAllText(Path.Combine(f.Textures.Resources, "Configs", "DynDOLOD_SSE_TexGen_Billboards_skyrimesm.txt"), $"Skyrim.esm;{key.ID:X8};True,Scale=1\n");
        var overridden = TextureGeneration.Run(request); Assert.True(overridden.Published);
        var smaller = Assert.Single(overridden.Items, i => i.Source.EndsWith(@"\testtree.nif"));
        Assert.Equal((128, 64), (smaller.Width, smaller.Height));
        string text = File.ReadAllText(GamePath.ToFileSystemPath(f.Textures.Output, smaller.Output[..^4] + ".txt"));
        var billboard = TreeBillboard.Parse(smaller.Output, text)!;
        Assert.Equal(800, billboard.Width); Assert.Equal(400, billboard.Height); Assert.Contains("ResolutionScale=1", text);
    }

    [Theory]
    [InlineData(GrassTextureMode.Auto)] [InlineData(GrassTextureMode.Packed)]
    public void PackedGrassUsesTopHalfColorAndBottomHalfNormalsIncludingZeroSpecularAlpha(GrassTextureMode mode)
    {
        var pixels = new byte[16 * 32 * 4];
        for (int i = 0; i < pixels.Length / 2; i += 4) { pixels[i] = 240; pixels[i + 1] = 10; pixels[i + 2] = 10; pixels[i + 3] = 255; }
        for (int i = pixels.Length / 2; i < pixels.Length; i += 4) { pixels[i] = 204; pixels[i + 1] = 128; pixels[i + 2] = 230; }
        var mesh = new LodMesh { Path = "grass.nif", Parts = [TextureFixture.Card("grass", "packed.dds")] };
        var image = BillboardRenderer.Render(mesh, _ => (pixels, 16, 32), 64, vertexColors: false, shaded: false, grassTextures: mode)!;
        Assert.All(Visible(image.Rgba), i => { Assert.True(image.Rgba[i * 4] > 220); Assert.True(image.Rgba[i * 4 + 2] < 20); });
        Assert.All(Visible(image.Rgba), i => Assert.True(image.NormalRgba![i * 4] > 190));
        var basic = BillboardRenderer.Render(mesh, _ => (pixels, 16, 32), 64, shaded: false, grassTextures: GrassTextureMode.Basic)!;
        Assert.Contains(Visible(basic.Rgba), i => basic.Rgba[i * 4 + 2] > 100);
    }

    [Fact]
    public void SplitPrecedesReductionAndDetectsEachMaterialSeparately()
    {
        var packed = new byte[8 * 4096 * 4];
        for (int i = 0; i < packed.Length / 2; i += 4) { packed[i] = 240; packed[i + 3] = 255; }
        for (int i = packed.Length / 2; i < packed.Length; i += 4) { packed[i] = packed[i + 1] = 128; packed[i + 2] = 255; }
        var decoded = GrassTexture.Decode((packed, 8, 4096), GrassTextureMode.Auto, .1f);
        Assert.Equal(1024, decoded.Diffuse.Height); Assert.NotNull(decoded.Normal);
        Assert.All(Enumerable.Range(0, decoded.Diffuse.Rgba.Length / 4), i => Assert.Equal(0, decoded.Diffuse.Rgba[i * 4 + 2]));
        Assert.Null(GrassTexture.Decode((new byte[] { 10, 240, 10, 255 }, 1, 1), GrassTextureMode.Auto, .1f).Normal);
        Assert.Throws<InvalidDataException>(() => GrassTexture.Decode((new byte[12], 1, 3), GrassTextureMode.Packed, .1f));
    }

    [Fact]
    public void HdGrassPlanesUseSeparateAtlasTilesAndKeepTheSameDensityTier()
    {
        var side = new GrassBillboard("atlas.dds", "atlas_n.dds", 50, 75, -2, new(.5f, 0, 1, 1));
        var front = new GrassBillboard("atlas.dds", "atlas_n.dds", 100, 75, -2, new(0, 0, .5f, 1), side);
        var mesh = GrassPatchBuilder.Build("grass", Vector3.Zero, [(front, new[] { new GrassInstance(Vector3.Zero, 1, 0) })], 1, 1, tiers: 4);
        var part = Assert.Single(mesh.Parts); Assert.Equal(8, part.Positions.Length);
        Assert.All(part.UVs.Take(4), uv => Assert.InRange(uv.X, 0, .5f));
        Assert.All(part.UVs.Skip(4), uv => Assert.InRange(uv.X, .5f, 1));
        Assert.Equal(100, Vector3.Distance(part.Positions[0], part.Positions[1]));
        Assert.Equal(50, Vector3.Distance(part.Positions[4], part.Positions[5]));
    }

    [Fact]
    public void NormalAtlasMipsRemainUnitVectorsAndIgnoreSpecularAlphaForFiltering()
    {
        var pixels = new byte[16 * 16 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        { pixels[i] = (byte)(i % 8 == 0 ? 240 : 128); pixels[i + 1] = (byte)(i % 8 == 0 ? 128 : 240); pixels[i + 2] = 160; pixels[i + 3] = (byte)(i % 8 == 0 ? 0 : 255); }
        var atlas = new TreeAtlasBuilder { MaxSize = 256 }.Build([new("normal", TextureImages.Encode(new(pixels, 16, 16)))], normalMap: true);
        var info = DdsFile.ReadInfo(atlas.Dds); int offset = info.DataOffset, size = info.Width;
        for (int mip = 0; mip < info.MipCount; mip++)
        {
            int bytes = ((size + 3) / 4) * ((size + 3) / 4) * 16;
            using var file = new MemoryStream(); DdsFile.WriteBc7(file, size, size, [atlas.Dds.AsSpan(offset, bytes).ToArray()]);
            var image = TextureImages.Decode(file.ToArray());
            for (int i = 0; i < image.Pixels.Length; i += 4)
            {
                var n = new Vector3(image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]) / 127.5f - Vector3.One;
                Assert.InRange(n.Length(), .93f, 1.07f); Assert.InRange(image.Pixels[i + 3], (byte)250, (byte)255); // BC7 is lossy
            }
            offset += bytes; size = Math.Max(4, size / 2);
        }
    }

    [Fact]
    public void GrassAtlasIncludesBothViewsAndNormalOnlyEditsInvalidateBlocks()
    {
        using var f = new TextureFixture();
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
        var grass = mod.Grasses.AddNew(); grass.Model = new Model { File = @"grass\used.nif" };
        mod.WriteToBinary(Path.Combine(f.Data, "Skyrim.esm"));
        string stem = $@"textures\terrain\lodgen\skyrim.esm\used_{grass.FormKey.ID:x8}";
        TextureFixture.Put(f.Data, stem + "_1.dds", TextureFixture.Solid(240, 10, 10));
        TextureFixture.Put(f.Data, stem + "_2.dds", TextureFixture.Solid(10, 240, 10));
        TextureFixture.Put(f.Data, stem + "_1_n.dds", TextureFixture.Solid(128, 128, 255));
        TextureFixture.Put(f.Data, stem + "_2_n.dds", TextureFixture.Solid(128, 128, 255));
        TextureFixture.Put(f.Data, stem + ".txt", Encoding.UTF8.GetBytes("Width=100\nHeight=80\nWidth_1=100\nHeight_1=80\nWidth_2=50\nHeight_2=80\n"));
        using (var stream = new MemoryStream())
        {
            using var w = new BinaryWriter(stream, Encoding.ASCII, true);
            w.Write(1); var name = Encoding.ASCII.GetBytes("grass\\used.nif\0"); w.Write(name.Length); w.Write(name);
            w.Write(1f); w.Write(grass.FormKey.ID); w.Write(new byte[] { 1, 1, 0 }); w.Write(1);
            for (int i = 0; i < 7; i++) w.Write(0f); w.Write(1); w.Write(16);
            for (int i = 0; i < 16; i++) w.Write((Half)(i is 3 or 4 ? 1 : 0));
            TextureFixture.Put(f.Data, @"grass\TestWorldx0000y0000.cgid", stream.ToArray());
        }
        using var game = GameContext.Open(f.Game);
        var grids = new Dictionary<string, LodGrid>(StringComparer.OrdinalIgnoreCase) { ["TestWorld"] = new("TestWorld", new(-4, -4, 8, 4, 8)) };
        var source = new GrassLodSource(game, game.BuildAssets(), 1, 1, f.Output);
        var cell = Assert.Single(source.Discover(grids)); source.PrepareAtlas(); Assert.Equal(2, source.Atlas!.Tiles);
        var part = Assert.Single(source.Build(cell.Meshes.Lod4!)!.Parts);
        var atlas = TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, GrassLodSource.AtlasDiffuse)));
        for (int plane = 0; plane < 2; plane++)
        {
            var uv = part.UVs.Skip(plane * 4).Take(4).Aggregate(Vector2.Zero, (a, b) => a + b) / 4;
            int i = ((int)(uv.Y * atlas.Height) * atlas.Width + (int)(uv.X * atlas.Width)) * 4;
            Assert.True(atlas.Pixels[i + plane] > 180); Assert.True(atlas.Pixels[i + 1 - plane] < 40);
        }
        string? before = source.Fingerprint(cell.Meshes.Lod4!);
        TextureFixture.Put(f.Data, stem + "_2_n.dds", TextureFixture.Solid(204, 128, 230));
        var updated = new GrassLodSource(game, game.BuildAssets(), 1, 1, f.Output);
        var updatedCell = Assert.Single(updated.Discover(grids)); updated.PrepareAtlas();
        Assert.NotEqual(before, updated.Fingerprint(updatedCell.Meshes.Lod4!));
    }
}
