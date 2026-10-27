using System.Buffers.Binary;
using System.Numerics;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda;

namespace AnvilLOD.Core.Tests;

public sealed class TrunkTextureTests
{
    private const string Stem = @"dyndolod\lod\trees\test mod\testtree_12345678_trunk";
    private const string Hybrid = @"meshes\dyndolod\lod\trees\testtree_12345678passthru_lod.nif";
    private const string RenderPath = @"dyndolod\render\billboards\" + Stem + ".nif";
    private const string Target = @"textures\" + Stem;
    private static TextureGenerationRequest Request(TextureFixture f) => f.Request with { Stitched = false, TreeTrunks = true, UnitsPerPixel = 4 };

    [Fact]
    public void Native_trunks_keep_independent_crops_orientation_normals_and_unlock_the_hybrid()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        Setup(f);
        var assets = AssetIndex.Build(GameRelease.SkyrimSE, f.Data);
        Assert.Contains("missing texture", new Tree3DSource(assets).Validate(Hybrid));
        var result = TextureGeneration.Run(Request(f));
        Assert.True(result.Published); Assert.Equal(0, result.Errors); Assert.Equal(5, result.Files);
        Assert.Equal(2, result.Items.Count(i => i.Kind == "Tree trunk" && i.Status == "Written"));
        var front = Image(f, "_1.dds"); var side = Image(f, "_2.dds");
        AssertRed(Pixel(front, .1f, .5f)); AssertGreen(Pixel(front, .55f, .5f));
        Assert.True(Pixel(front, .95f, .5f).A < 10); // bounds come from the card, not a stretched source
        Assert.True(Pixel(front, .4f, .97f).A < 10); // negative Z margin is kept
        var blue = Pixel(side, .3f, .5f); var yellow = Pixel(side, .7f, .5f);
        Assert.True(blue.B > 160 && blue.R < 30); // view 2 sees -X, with +Y across the image
        Assert.True(yellow.R > 160 && yellow.G > 160 && yellow.B < 30);
        var normal = Pixel(Image(f, "_2_n.dds"), .3f, .5f);
        Assert.InRange(normal.R, 150, 185); Assert.InRange(normal.G, 55, 95); Assert.True(normal.B > 225);
        string metadata = File.ReadAllText(GamePath.ToFileSystemPath(f.Output, Target + ".txt"));
        Assert.Contains("Width_1=200.000000", metadata); Assert.Contains("Height_1=400.000000", metadata);
        Assert.Contains("Width_2=300.000000", metadata); Assert.Contains("Height_2=500.000000", metadata);
        Assert.Contains("Bottom_1=-40.000000", metadata); Assert.Contains("Left_2=-150.000000", metadata);
        foreach (string suffix in new[] { "_1.dds", "_2.dds", "_1_n.dds", "_2_n.dds" })
        {
            var bytes = File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, Target + suffix));
            Assert.True(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28)) > 1); // mip chain
        }
        var manifest = TextureGenerationManifest.Read(f.Output, true)!;
        manifest.Verify(f.Output); manifest.Attach(assets, f.Output);
        var source = new Tree3DSource(assets);
        Assert.Null(source.Validate(Hybrid));
        var tree = new TreeReference("000123:Skyrim.esm", "Tamriel", new(100, 100, 0), .4f, 1, 0x123,
            new("unused.dds", 200, 400, 0), Model3D: Hybrid);
        var references = Tree3DSource.ToReferences([tree], new Tree3DSettings(Enabled: true)).ToArray();
        var quad = new QuadKey("Tamriel", LodLevel.Lod4, 0, 0);
        string lod = Path.Combine(f.Root, "LOD");
        var emitted = new LodGenerator(assets, synthetic: source).Generate(
            new Dictionary<QuadKey, IReadOnlyList<LodReference>> { [quad] = references }, [quad], lod);
        Assert.Equal(1, emitted.BlocksWritten); Assert.Equal(0, emitted.BlocksFailed); Assert.Empty(emitted.MeshErrors);
        var output = AnvilLOD.Meshes.Nif.NifGeometryReader.Read(quad.RelativePath,
            File.ReadAllBytes(GamePath.ToFileSystemPath(lod, quad.RelativePath)), passthru: true);
        Assert.NotEmpty(output.Parts);
        foreach (var texture in output.Parts.SelectMany(p => p.Material.Textures).Where(p => p.Length > 0))
            Assert.True(assets.Exists(texture), "Generated block has an unresolved texture: " + texture);
    }

    [Theory]
    [InlineData(@"render\skyrim\billboards\", false)]
    [InlineData(@"render\billboards\", false)]
    [InlineData(@"render\skyrim\billboards\", true)]
    public void Resource_layouts_are_case_insensitive_and_enabled_mod_sources_override_the_bundle(string prefix, bool modOverride)
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        Setup(f);
        File.Delete(GamePath.ToFileSystemPath(f.Data, RenderPath));
        Directory.Delete(Path.Combine(f.Data, "dyndolod"), true); // avoid two distinct case-only roots on Linux
        var red = Box(@"textures\red.dds");
        TextureFixture.Put(f.Resources, prefix + Stem + ".nif", LodNifWriter.Write("trunk", red));
        if (modOverride)
            TextureFixture.Put(f.Data, (@"dyndolod\render\skyrim\billboards\" + Stem + ".nif").ToUpperInvariant(),
                LodNifWriter.Write("trunk", Box(@"textures\green.dds")));
        Assert.True(TextureGeneration.Run(Request(f)).Published);
        var pixel = Pixel(Image(f, "_1.dds"), .4f, .5f);
        if (modOverride) AssertGreen(pixel); else AssertRed(pixel);
    }

    [Fact]
    public void Missing_inputs_failed_geometry_and_cancellation_keep_the_previous_complete_output()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        Setup(f); var request = Request(f);
        Assert.True(TextureGeneration.Run(request).Published);
        string before = File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName));
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => TextureGeneration.Run(request,
            new Callback(p => { if (p.StartsWith("Rendering tree trunk")) cts.Cancel(); }), cts.Token));
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName)));
        File.Delete(GamePath.ToFileSystemPath(f.Data, @"textures\blue.dds"));
        var failed = TextureGeneration.Run(request);
        Assert.False(failed.Published); Assert.Contains(failed.Items, i => i.Status == "Failed" && i.Detail.Contains("blue.dds"));
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName)));
        TextureFixture.Put(f.Data, RenderPath, [0, 1, 2]);
        Assert.False(TextureGeneration.Run(request).Published);
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName)));
        TextureGenerationManifest.Read(f.Output, true)!.Verify(f.Output);
        Assert.Empty(Directory.GetDirectories(f.Output, ".texture-build-*"));
    }

    [Fact]
    public void Skinned_shapes_cannot_publish_an_incomplete_trunk()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        Setup(f);
        string path = GamePath.ToFileSystemPath(f.Data, RenderPath);
        var bytes = File.ReadAllBytes(path);
        var nif = AnvilLOD.Meshes.Nif.NifFile.Read(bytes);
        var shape = nif.Blocks.First(b => b.Type == "BSTriShape");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(shape.Offset + 88), 0);
        File.WriteAllBytes(path, bytes);
        var result = TextureGeneration.Run(Request(f));
        Assert.False(result.Published);
        Assert.Contains(result.Items, i => i.Status == "Failed" && i.Detail.Contains("Skinned"));
        Assert.Empty(Directory.GetFiles(f.Output, "*.dds", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_shared_texture_with_conflicting_crops_is_reported_instead_of_misaligning_the_trees()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        Setup(f);
        TextureFixture.Put(f.Data, @"meshes\dyndolod\lod\trees\conflictingpassthru_lod.nif",
            LodNifWriter.Write("FlatTrunk", [Quad(Target + "_1.dds", -100, 100, 0, 400, false)]));
        var result = TextureGeneration.Run(Request(f));
        Assert.False(result.Published);
        Assert.Contains(result.Items, i => i.Status == "Failed" && i.Detail.Contains("conflicting crops"));
    }

    [Fact]
    public void Mirrored_texture_coordinates_are_preserved_and_nonplanar_cards_are_rejected()
    {
        var card = Quad(@"textures\x.dds", -50, 150, -40, 360, false);
        card.UVs[0] = new(1, 1); card.UVs[1] = new(0, 1); card.UVs[2] = new(1, 0); card.UVs[3] = new(0, 0);
        var frame = TrunkView.FromCard(card, 1);
        Assert.Equal(-Vector3.UnitX, frame.Right); Assert.Equal(-150, frame.Left);
        var source = new LodMesh { Path = "test", Parts = [Quad("red", -100, 0, 0, 400, false), Quad("green", 0, 100, 0, 400, false)] };
        var image = frame.Render(source, p => (p == "red" ? new byte[] { 255, 0, 0, 255 } : new byte[] { 0, 255, 0, 255 }, 1, 1), 64, 4, 1);
        AssertGreen(Pixel(new(image.Rgba, image.Width, image.Height), .4f, .5f));
        AssertRed(Pixel(new(image.Rgba, image.Width, image.Height), .9f, .5f));
        card.Positions[3] += Vector3.UnitY * 10;
        Assert.Throws<NotSupportedException>(() => TrunkView.FromCard(card, 1));
    }

    [Fact]
    public void Filtering_and_unrelated_resources_do_not_trigger_missing_texture_failures()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        Setup(f);
        TextureFixture.Put(f.Resources, @"render\billboards\unrelated_trunk.nif", [0, 1, 2]);
        var result = TextureGeneration.Run(Request(f) with { ModelFilter = "testtree" });
        Assert.True(result.Published); Assert.Equal(5, result.Files);
        Assert.Throws<InvalidOperationException>(() => TextureGenerationManifest.Read(f.Output, true));
        var empty = TextureGeneration.Run(Request(f) with { OutputFolder = Path.Combine(f.Root, "Empty"), ModelFilter = "unrelated" });
        Assert.False(empty.Published); Assert.Equal(0, empty.Errors);
        Assert.Contains(empty.Items, i => i.Status == "Skipped" && i.Detail.Contains("No matching hybrid"));
    }

    [Fact]
    public void Missing_trunk_resource_keeps_installed_textures_available_and_reports_the_limitation()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        Setup(f); File.Delete(GamePath.ToFileSystemPath(f.Data, RenderPath));
        var missing = TextureGeneration.Run(Request(f) with { Stitched = true });
        Assert.True(missing.Published); Assert.Contains(missing.Warnings, w => w.Contains("No matching _trunk.nif"));
        foreach (string suffix in new[] { "_1.dds", "_2.dds", "_1_n.dds", "_2_n.dds" })
            TextureFixture.Put(f.Data, Target + suffix, TextureFixture.Solid(240, 12, 6));
        var existing = TextureGeneration.Run(Request(f) with { Stitched = true });
        Assert.True(existing.Published); Assert.DoesNotContain(existing.Warnings, w => w.Contains("_trunk.nif"));
        Assert.Contains(existing.Items, i => i.Kind == "Tree trunk" && i.Detail.Contains("using installed"));
    }

    private static void Setup(TextureFixture f)
    {
        TextureFixture.Put(f.Data, @"textures\blue.dds", TextureFixture.Solid(8, 10, 240));
        TextureFixture.Put(f.Data, @"textures\yellow.dds", TextureFixture.Solid(240, 240, 8));
        var parts = new[] {
            Quad(@"textures\red.dds", -100, 0, 0, 400, false, -100),
            Quad(@"textures\green.dds", 0, 100, 0, 400, false, -100),
            Quad(@"textures\green.dds", -100, 100, 0, 400, false, 100),
            Quad(@"textures\blue.dds", -100, 0, 0, 400, true, -100),
            Quad(@"textures\yellow.dds", 0, 100, 0, 400, true, -100),
            Quad(@"textures\green.dds", -100, 100, 0, 400, true, 100) };
        TextureFixture.Put(f.Data, RenderPath, LodNifWriter.Write("trunk", parts));
        TextureFixture.Put(f.Data, Hybrid, LodNifWriter.Write("FlatTrunk", [
            Quad((Target + "_1.dds").ToUpperInvariant(), -50, 150, -40, 360, false),
            Quad(Target + "_2.dds", -150, 150, -50, 450, true),
            Quad(Target + "_1.dds", -50, 150, -40, 360, false) ])); // duplicated back face is harmless
    }

    private static LodMeshPart[] Box(string texture) => [
        Quad(texture, -100, 100, 0, 400, false, -100), Quad(texture, -100, 100, 0, 400, true, -100) ];

    private static LodMeshPart Quad(string texture, float left, float right, float bottom, float top, bool side, float depth = 0)
    {
        Vector3 P(float x, float z) => side ? new(depth, x, z) : new(x, depth, z);
        var normal = side ? Vector3.Normalize(new(-.9f, .3f, .4f)) : -Vector3.UnitY;
        return new() {
            Material = TextureFixture.Card("", texture).Material with { Textures = [texture,
                texture.Contains("_trunk_", StringComparison.OrdinalIgnoreCase) ? texture[..^4] + "_n.dds" : ""] },
            Positions = [P(left, bottom), P(right, bottom), P(left, top), P(right, top)],
            UVs = [new(0, 1), new(1, 1), new(0, 0), new(1, 0)],
            Normals = Enumerable.Repeat(normal, 4).ToArray(), Tangents = Enumerable.Repeat(side ? Vector3.UnitY : Vector3.UnitX, 4).ToArray(),
            Bitangents = Enumerable.Repeat(Vector3.UnitZ, 4).ToArray(), Triangles = [0, 1, 2, 1, 3, 2]
        };
    }

    private static TextureImage Image(TextureFixture f, string suffix) => TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, Target + suffix)));
    private static (byte R, byte G, byte B, byte A) Pixel(TextureImage image, float x, float y)
    {
        int i = ((int)(image.Height * y) * image.Width + (int)(image.Width * x)) * 4;
        return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]);
    }
    private static void AssertRed((byte R, byte G, byte B, byte A) p) => Assert.True(p.R > 160 && p.G < 30 && p.A > 240, p.ToString());
    private static void AssertGreen((byte R, byte G, byte B, byte A) p) => Assert.True(p.G > 160 && p.R < 30 && p.A > 240, p.ToString());
    private sealed class Callback(Action<string> callback) : IProgress<string> { public void Report(string value) => callback(value); }
}
