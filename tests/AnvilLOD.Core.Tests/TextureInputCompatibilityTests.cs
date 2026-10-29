using System.Buffers.Binary;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Testing;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Core.Tests;

public sealed class TextureInputCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BethesdaNormalMarkerUsesGeometryNormalsAndWritesSafeSlots(bool legacy)
    {
        var geometry = legacy ? ModelNifFixture.Geometry.Triangles : ModelNifFixture.Geometry.Packed;
        var mesh = NifGeometryReader.Read("grass", ModelNifFixture.Create(geometry, le: legacy, normalTexture: "\bNOR"), passthru: true);
        Assert.Empty(mesh.Warnings);
        Assert.Equal("", Assert.Single(mesh.Parts).Material.Textures[1]);
        var withoutNormal = NifGeometryReader.Read("grass", ModelNifFixture.Create(geometry, le: legacy, normalTexture: ""), passthru: true);
        (byte[], int, int)? Load(string path)
        {
            Assert.Equal(@"textures\green.dds", path);
            return (new byte[] { 20, 190, 40, 255 }, 1, 1);
        }
        var image = BillboardRenderer.Render(mesh, Load, 32)!;
        var expected = BillboardRenderer.Render(withoutNormal, Load, 32)!;
        Assert.NotNull(image);
        Assert.Equal(expected.Rgba, image.Rgba); Assert.Equal(expected.NormalRgba, image.NormalRgba);
        var written = NifFile.Read(LodNifWriter.Write("grass", mesh.Parts));
        var slots = written.ReaderFor(Assert.Single(written.Blocks, b => b.Type == "BSShaderTextureSet"));
        slots.U32(); slots.SizedString(); Assert.Equal("", slots.SizedString());
        // The marker is handled only inside the NIF normal slot, never as a filesystem path.
        Assert.Throws<ArgumentException>(() => GamePath.NormalizeTexture("\bNOR"));
    }

    [Theory]
    [InlineData("\bOTHER", false)]
    [InlineData("textures\\\bNOR", false)]
    [InlineData("textures\\..\\outside.dds", false)]
    [InlineData("\bNOR", true)]
    public void InvalidTextureReferencesStillRejectTheModel(string texture, bool diffuse)
    {
        var bytes = ModelNifFixture.Create(ModelNifFixture.Geometry.Packed,
            diffuseTexture: diffuse ? texture : @"textures\green.dds", normalTexture: diffuse ? "" : texture);
        var mesh = NifGeometryReader.Read("unsafe", bytes);
        Assert.True(mesh.HasIncompleteGeometry);
        Assert.Empty(mesh.Parts);
        Assert.Contains(mesh.Warnings, w => w.Contains("safe Data-relative"));
    }

    [Theory]
    [InlineData("automatic", true)]
    [InlineData("preview", false)]
    [InlineData("rule", false)]
    [InlineData("grass", false)]
    [InlineData("mixed-error", false)]
    public void EffectTreeExclusionNeverHidesExplicitRequestsGrassOrMalformedGeometry(string mode, bool published)
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var bytes = ModelNifFixture.Create(ModelNifFixture.Geometry.Packed, effect: true, unsupportedEffect: true);
        if (mode == "mixed-error")
        {
            var root = NifFile.Read(bytes).Blocks[0];
            // Root has three child references. Corrupt one alongside the unsupported effect.
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(root.Offset + 76), 10000);
        }
        string path = @"meshes\trees\testtree.nif";
        if (mode == "grass")
        {
            var mod = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
            var grass = mod.Grasses.AddNew(); grass.Model = new Model { File = @"trees\testtree.nif" };
            mod.LandscapeTextures.AddNew().Grasses.Add(grass.ToLink());
            mod.WriteToBinary(Path.Combine(f.Data, "Skyrim.esm"));
        }
        if (mode == "rule")
        {
            using var game = GameContext.Open(f.Game);
            var tree = Assert.Single(game.LoadOrder.PriorityOrder.Tree().WinningOverrides());
            File.WriteAllText(Path.Combine(f.Resources, "Configs", "DynDOLOD_SSE_TexGen_Billboards_skyrimesm.txt"), $"Skyrim.esm;{tree.FormKey.ID:X8};True");
        }
        TextureFixture.Put(f.Data, path, bytes);
        var result = TextureGeneration.Run(f.Request with { Trees = mode != "grass", Grass = mode == "grass", ModelFilter = mode == "preview" ? "testtree" : "" });
        Assert.Equal(published, result.Published);
        var item = Assert.Single(result.Items, i => i.Kind == (mode == "grass" ? "Grass" : "Tree"));
        Assert.Equal(published ? "Skipped" : "Failed", item.Status);
        Assert.False(File.Exists(GamePath.ToFileSystemPath(f.Output, item.Output)));
        if (published)
        {
            Assert.Contains("Excluded whole tree", item.Detail);
            Assert.Contains(result.Warnings, w => w.Contains("Excluded whole tree"));
            Assert.Contains(",False", File.ReadAllText(GamePath.ToFileSystemPath(f.Output, @"textures\terrain\lodgen\texgen_sse_tree_billboards.txt")));
            TextureGenerationManifest.Read(f.Output, true)!.Verify(f.Output);
        }
    }

    [Fact]
    public void GrassWithBethesdaNormalMarkerPublishesCompleteBillboardFiles()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
        var grass = mod.Grasses.AddNew(); grass.Model = new Model { File = @"grass\grass.nif" };
        mod.LandscapeTextures.AddNew().Grasses.Add(grass.ToLink()); mod.WriteToBinary(Path.Combine(f.Data, "Skyrim.esm"));
        TextureFixture.Put(f.Data, @"meshes\grass\grass.nif", ModelNifFixture.Create(ModelNifFixture.Geometry.Packed, normalTexture: "\bNOR"));
        var result = TextureGeneration.Run(f.Request with { Grass = true });
        Assert.True(result.Published, string.Join("; ", result.Items.Select(i => i.Detail)));
        var item = Assert.Single(result.Items, i => i.Kind == "Grass"); Assert.Equal("Written", item.Status);
        foreach (string suffix in new[] { ".dds", "_1.dds", "_1_n.dds", ".txt" })
            Assert.True(File.Exists(GamePath.ToFileSystemPath(f.Output, item.Output[..^4] + suffix)));
        TextureGenerationManifest.Read(f.Output, true)!.Verify(f.Output);
    }
}
