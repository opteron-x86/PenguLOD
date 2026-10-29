using AnvilLOD.Core.Lod;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda;

namespace AnvilLOD.Core.Tests;

public sealed class TreeMaterialVariantTests
{
    private const string Model = @"meshes\trees\testtree.nif";
    private static TreeMaterialVariant Variant(string diffuse, string name = "tree:0") =>
        TreeMaterialVariant.Create(Model, [new(name, 0, [diffuse, @"textures\red_n.dds"])]);

    [Fact]
    public void SharedSourceHasSeparateMaterialsKeysAndCoverageWithoutMutation()
    {
        using var f = new TextureFixture();
        var assets = AssetIndex.Build(GameRelease.SkyrimSE, f.Data);
        var source = new Tree3DSource(assets, settings: new(true));
        var red = Variant(@"textures\red.dds"); var green = Variant(@"textures\green.dds");
        var a = source.Select(Model, 4, true, red); var b = source.Select(Model, 4, true, green);
        Assert.NotNull(a.MeshPath); Assert.NotNull(b.MeshPath); Assert.NotEqual(a.MeshPath, b.MeshPath);
        Assert.NotEqual(source.Fingerprint(a.MeshPath), source.Fingerprint(b.MeshPath));
        Assert.Equal(@"textures\red.dds", source.Build(a.MeshPath)!.Parts[0].Material.Textures[0]);
        Assert.Equal(@"textures\green.dds", source.Build(b.MeshPath)!.Parts[0].Material.Textures[0]);
        Assert.Equal(@"textures\green.dds", source.Build(Tree3DSource.MeshPath(Model))!.Parts[0].Material.Textures[0]);
        var tree = new TreeReference("tree", "World", default, 0, 1, 1, new("card.dds", 1, 1, 0),
            Model3D: Model, Model3DGenerated: true, MaterialVariant: red);
        var reports = source.Report([tree, tree with { MaterialVariant = green }], new(true));
        Assert.Equal(2, reports.Count); Assert.All(reports, r => Assert.Equal(1, r.Instances));
    }

    [Theory]
    [InlineData("missing-shape", @"textures\red.dds", "shape")]
    [InlineData("tree:0", @"textures\missing.dds", "missing alternate texture")]
    public void InvalidVariantsRetainBillboardsWithAReason(string shape, string texture, string reason)
    {
        using var f = new TextureFixture();
        var source = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: new(true));
        var selection = source.Select(Model, 4, true, Variant(texture, shape));
        Assert.Null(selection.MeshPath); Assert.Contains(reason, selection.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthoredUvsAreNotReusedForFullModelTextureOverridesWhenNativeGenerationIsDisabled()
    {
        using var f = new TextureFixture();
        var source = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: new(true, GenerateMissing: false));
        var result = source.Select(Model, 4, variant: Variant(@"textures\red.dds"));
        Assert.Null(result.MeshPath); Assert.Contains("verified mapping", result.Reason);
    }

    [Fact]
    public void AutomaticBillboardUsesTheSameOverrideAndInvalidatesWhenItChanges()
    {
        using var f = new TextureFixture();
        string cache = Path.Combine(f.Root, "cache");
        var synth = new TreeBillboardSynth(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), cache);
        foreach (var (texture, channel) in new[] { (@"textures\red.dds", 0), (@"textures\green.dds", 1) })
        {
            Assert.True(synth.TryRender(Model, "fixture.esm", "variant", out var reason, Variant(texture)), reason);
            var pixels = TextureImages.Decode(File.ReadAllBytes(Path.Combine(cache, "textures", "terrain", "lodgen", "fixture.esm", "variant.dds"))).Pixels;
            Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4 + 3] > 128
                && pixels[i * 4 + channel] > pixels[i * 4 + 1 - channel] + 50);
        }
    }

    [Fact]
    public void AChangedReplacementAssetChangesTheNativeMeshFingerprint()
    {
        using var f = new TextureFixture();
        var variant = Variant(@"textures\red.dds");
        string Fingerprint()
        {
            var source = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: new(true));
            return source.Fingerprint(source.Select(Model, 4, true, variant).MeshPath!)!;
        }
        var before = Fingerprint();
        TextureFixture.Put(f.Data, @"textures\red.dds", TextureFixture.Solid(12, 14, 210));
        var file = GamePath.ToFileSystemPath(f.Data, @"textures\red.dds");
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(10));
        Assert.NotEqual(before, Fingerprint());
    }
}
