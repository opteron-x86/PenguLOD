using System.Buffers.Binary;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Textures;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Core.Tests;

public sealed class TextureGenerationTests
{
    [Theory]
    [InlineData(@"textures\..\escape.dds")]
    [InlineData(@"d:\textures\bad.dds")]
    [InlineData(@"textures\d:\bad.dds")]
    [InlineData(@"/textures/absolute.dds")]
    public void RecipePathsCannotEscapeOutput(string path) => Assert.ThrowsAny<Exception>(() => TextureRecipe.AssetPath(path));

    [Fact]
    public void CroppedFractionalTilesAndGammaMultipliersMatchRecipeSemantics()
    {
        var tile = TextureRecipe.ParseTile("0\t0\t1\t1\ttextures\\x.dds\t0.74\t1\t-0.25\t0\ttextures\\lod\\x.dds\t1\t1", false);
        var target = TextureImages.Blank(64, 64);
        TextureImages.Blit(target, TextureImages.Decode(TextureFixture.Solid(230, 190, 10, 64)), tile, 64, false);
        Assert.Equal(0, target.Pixels[0]); Assert.InRange(target.Pixels[1], 180, 200); Assert.Equal(255, target.Pixels[3]);
        Assert.Equal(0, target.Pixels[(63 * 4) + 3]); // outside the clipped tile
    }

    [Fact]
    public void Legacy24BitDdsHonorsChannelMasksAndPaddedRows()
    {
        var bytes = new byte[144];
        void U(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        U(0, 0x20534444); U(4, 124); U(8, 8); U(12, 2); U(16, 2); U(20, 8); U(76, 32); U(80, 0x40); U(88, 24);
        U(92, 0xff0000); U(96, 0xff00); U(100, 0xff);
        new byte[] { 10, 20, 30, 40, 50, 60, 0xaa, 0xaa, 70, 80, 90, 100, 110, 120, 0xbb, 0xbb }.CopyTo(bytes, 128);
        var image = TextureImages.Decode(bytes);
        Assert.Equal(new byte[] { 30, 20, 10, 255, 60, 50, 40, 255, 90, 80, 70, 255, 120, 110, 100, 255 }, image.Pixels);
        Assert.Throws<InvalidDataException>(() => TextureImages.Decode(bytes[..^1]));
    }

    [Fact]
    public void SourceAlternativesUseFirstAvailableAndFinalAtlasDimensionsWin()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        File.AppendAllText(f.RuleFile, "\n0\t1\t1\t1\ttextures\\missing.dds|textures\\green.dds\t1\t1\t0\t0\ttextures\\lod\\atlas.dds\t1\t1");
        var result = TextureGeneration.Run(f.Request);
        Assert.True(result.Published); Assert.Contains(result.Warnings, w => w.Contains("final recipe dimensions"));
        var image = TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, @"textures\lod\atlas.dds")));
        Assert.Equal((64, 64), (image.Width, image.Height)); Assert.True(image.Pixels[1] > 220);
    }

    [Fact]
    public void FilteredPreviewsCannotReplaceOrBeUsedAsFullTextureOutput()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        Assert.True(TextureGeneration.Run(f.Request).Published);
        Assert.Throws<ArgumentException>(() => TextureGeneration.Run(f.Request with { ModelFilter = "tree" }));
        string preview = Path.Combine(f.Root, "Preview");
        Assert.True(TextureGeneration.Run(f.Request with { OutputFolder = preview, Stitched = false, Trees = true, ModelFilter = "tree" }).Published);
        Assert.Throws<InvalidOperationException>(() => TextureGenerationManifest.Read(preview, true));
    }

    [Fact]
    public void StitchesColorAndNormalAtlasesWithRectangularMipChainAndOverlayPriority()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var result = TextureGeneration.Run(f.Request);
        Assert.True(result.Published); Assert.Equal(2, result.Files); Assert.Equal(0, result.Errors);
        var bytes = File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, @"textures\lod\atlas.dds"));
        var image = TextureImages.Decode(bytes);
        Assert.Equal((128, 64), (image.Width, image.Height));
        Assert.True(image.Pixels[0] > 220); Assert.True(image.Pixels[(80 * 4) + 1] > 220);
        Assert.Equal(6, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28))); // 128×64 to 4×2
        var normal = TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, @"textures\lod\atlas_n.dds")));
        Assert.InRange(normal.Pixels[0], 125, 131); Assert.True(normal.Pixels[2] > 248);
        var manifest = TextureGenerationManifest.Read(f.Output, true)!; manifest.Verify(f.Output);
        TextureFixture.Put(f.Data, @"textures\lod\atlas.dds", TextureFixture.Solid(0, 0, 250));
        var assets = AssetIndex.Build(GameRelease.SkyrimSE, f.Data); manifest.Attach(assets, f.Output);
        Assert.True(assets.TryOpen(@"textures\lod\atlas.dds", out var stream));
        using (stream) { using var buffer = new MemoryStream(); stream.CopyTo(buffer); Assert.Equal(bytes, buffer.ToArray()); }
        Assert.Equal("AnvilLOD texture generation", assets.SourceOf(@"textures\lod\atlas.dds"));
        Assert.Equal(1, assets.EnumeratePaths(@"textures\lod\").Count(p => p == @"textures\lod\atlas.dds"));
    }

    [Fact]
    public void DataRecipesOverrideInstalledRecipesAndTemporaryAtlasesStayOutOfPublishedOutput()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        string temp = @"DynDOLOD-temp\textures\temp.dds";
        var text = TextureFixture.Tile(temp, @"textures\lod\result.dds", 0) + "\n" + TextureFixture.Tile(@"textures\green.dds", temp, 0);
        TextureFixture.Put(f.Data, @"DynDOLOD\DynDOLOD_SSE_TexGen_noalpha_skyrimesm.txt", System.Text.Encoding.UTF8.GetBytes(text));
        var result = TextureGeneration.Run(f.Request);
        Assert.True(result.Published); Assert.Equal(2, result.Files);
        Assert.False(Directory.Exists(Path.Combine(f.Output, "dyndolod-temp")));
        var image = TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, @"textures\lod\result.dds")));
        Assert.True(image.Pixels[1] > 220);
    }

    [Fact]
    public void ConversionRulesGenerateNativeMipsAndPersistTheirProgram()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        File.AppendAllText(f.RuleFile, "\n0\t1\t1\t1\ttextures\\green.dds\tx\t7\t12\t0\ttextures\\special.dds\t2\tx");
        var result = TextureGeneration.Run(f.Request);
        Assert.True(result.Published); Assert.Contains(result.Items, i => i.Kind == "Conversion" && i.Status == "Written");
        Assert.True(File.Exists(GamePath.ToFileSystemPath(f.Output, @"textures\special.dds")));
        Assert.Contains(TextureGenerationManifest.Read(f.Output, true)!.Recipes!.Entries, e => e.Conversion is not null);
    }

    [Fact]
    public void FailureAndCancellationPreservePreviousOutput()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        Assert.True(TextureGeneration.Run(f.Request).Published);
        string before = File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName));
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => TextureGeneration.Run(f.Request, new Callback(p => { if (p.StartsWith("Preparing texture")) cts.Cancel(); }), cts.Token));
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName)));
        File.WriteAllText(f.RuleFile, TextureFixture.Tile(@"textures\missing.dds", @"textures\lod\atlas.dds", 0));
        var failed = TextureGeneration.Run(f.Request);
        Assert.False(failed.Published); Assert.True(failed.Errors > 0);
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName)));
        TextureGenerationManifest.Read(f.Output, true)!.Verify(f.Output);
        Assert.Empty(Directory.GetDirectories(f.Output, ".texture-build-*"));
    }

    [Fact]
    public void RegenerationRemovesOnlyPreviouslyOwnedStaleTextures()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var first = TextureGeneration.Run(f.Request);
        File.WriteAllText(Path.Combine(f.Output, "notes.txt"), "keep");
        File.WriteAllText(f.RuleFile, TextureFixture.Tile(@"textures\green.dds", @"textures\lod\new.dds", 0));
        var second = TextureGeneration.Run(f.Request);
        Assert.True(second.Published); Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.False(File.Exists(GamePath.ToFileSystemPath(f.Output, @"textures\lod\atlas.dds")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(f.Output, "notes.txt")));
        File.WriteAllText(GamePath.ToFileSystemPath(f.Output, @"textures\lod\new.dds"), "edited");
        Assert.Throws<IOException>(() => TextureGeneration.Run(f.Request));
        Assert.Equal("edited", File.ReadAllText(GamePath.ToFileSystemPath(f.Output, @"textures\lod\new.dds")));
        Assert.Throws<IOException>(() => TextureGenerationManifest.Read(f.Output, true)!.Verify(f.Output));
    }

    [Fact]
    public void Texture_verification_reports_progress_and_honors_explicit_cancellation()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        TextureGeneration.Run(f.Request);
        var manifest = TextureGenerationManifest.Read(f.Output, true)!;
        var lines = new List<string>();
        manifest.Verify(f.Output, new Callback(lines.Add));
        Assert.StartsWith("Verifying generated textures: 0/", lines.First());
        Assert.StartsWith("Verified generated textures:", lines.Last());
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => manifest.Verify(f.Output,
            new Callback(_ => cancellation.Cancel()), cancellation.Token));
    }

    [Fact]
    public void NativeBillboardsAndRenderedObjectsProduceDdsAndMetadata()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var result = TextureGeneration.Run(f.Request with { Stitched = false, Trees = true, RenderedObjects = true });
        Assert.True(result.Published, string.Join("; ", result.Items.Select(i => i.Detail)));
        var tree = Assert.Single(result.Items, i => i.Kind == "Tree" && i.Status == "Written");
        Assert.Equal((64, 64), (tree.Width, tree.Height));
        Assert.Contains("Width=800", File.ReadAllText(GamePath.ToFileSystemPath(f.Output, tree.Output[..^4] + ".txt"))); // the off-center model is padded around its origin
        Assert.Contains(result.Items, i => i.Kind == "Rendered object" && i.Status == "Written");
        Assert.Contains(",True", File.ReadAllText(GamePath.ToFileSystemPath(f.Output, @"textures\terrain\lodgen\texgen_sse_tree_billboards.txt")));
    }

    [Fact]
    public void UnsupportedShapesDoNotProduceAnIncompleteBillboard()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var bytes = AnvilLOD.Meshes.Authoring.LodNifWriter.Write("tree",
            [TextureFixture.Card("trunk", @"textures\red.dds"), TextureFixture.Card("leaves", @"textures\green.dds")]);
        var nif = AnvilLOD.Meshes.Nif.NifFile.Read(bytes);
        var leaves = nif.Blocks.Where(b => b.Type == "BSTriShape").Skip(1).First();
        // ObjectNET + AVObject + bounding sphere: mark the second shape as skinned.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(leaves.Offset + 88), 0);
        Assert.Contains(AnvilLOD.Meshes.Nif.NifGeometryReader.Read("tree", bytes).Warnings, w => w.Contains("Skinned"));
        TextureFixture.Put(f.Data, @"meshes\trees\testtree.nif", bytes);
        var result = TextureGeneration.Run(f.Request with { Stitched = false, Trees = true });
        Assert.Contains(result.Items, i => i.Kind == "Tree" && i.Status == "Failed" && i.Detail.Contains("Unsupported model shapes"));
        Assert.Empty(Directory.GetFiles(f.Output, "*.dds", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void UnusedGrassDoesNotBlockTexturesButRequiredOrPreviewedGrassStillFails(bool used, bool preview, bool published)
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
        var grass = mod.Grasses.AddNew(); grass.EditorID = "UnusedLibraryGrass";
        grass.Model = new Model { File = @"grass\unused.nif" };
        if (used) mod.LandscapeTextures.AddNew().Grasses.Add(grass.ToLink());
        mod.WriteToBinary(Path.Combine(f.Data, "Skyrim.esm"));
        TextureFixture.Put(f.Data, @"meshes\grass\unused.nif", AnvilLOD.Meshes.Authoring.LodNifWriter.Write("grass",
            [TextureFixture.Card("grass", @"textures\missing-grass.dds")]));
        var result = TextureGeneration.Run(f.Request with { Grass = true, ModelFilter = preview ? "unused" : "" });
        Assert.Equal(published, result.Published);
        Assert.Contains(result.Items, i => i.Kind == "Grass" && i.Status == (published ? "Skipped" : "Failed"));
        if (published) Assert.Contains(result.Items, i => i.Kind == "Grass" && i.Detail.Contains("landscape"));
    }

    [Fact]
    public void WinningLandscapeOverrideControlsGrassSelectionAndExplicitBillboardRulesStillApply()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
        var grass = mod.Grasses.AddNew(); grass.Model = new Model { File = @"grass\unused.nif" };
        var land = mod.LandscapeTextures.AddNew(); land.Grasses.Add(grass.ToLink());
        mod.WriteToBinary(Path.Combine(f.Data, "Skyrim.esm"));
        var patch = new SkyrimMod(ModKey.FromNameAndExtension("Patch.esp"), SkyrimRelease.SkyrimSE);
        patch.LandscapeTextures.GetOrAddAsOverride(land).Grasses.Clear();
        patch.WriteToBinary(Path.Combine(f.Data, "Patch.esp"));
        File.WriteAllText(f.Plugins, "*Patch.esp");
        TextureFixture.Put(f.Data, @"meshes\grass\unused.nif", AnvilLOD.Meshes.Authoring.LodNifWriter.Write("grass",
            [TextureFixture.Card("grass", @"textures\missing-grass.dds")]));
        Assert.True(TextureGeneration.Run(f.Request with { Grass = true }).Published);
        File.WriteAllText(Path.Combine(f.Resources, "Configs", "DynDOLOD_SSE_TexGen_Billboards_skyrimesm.txt"), $"Skyrim.esm;{grass.FormKey.ID:X8};True");
        Assert.False(TextureGeneration.Run(f.Request with { Grass = true }).Published);
    }

    [Fact]
    public void OptionalObjectResourcesWithoutAnInstalledTargetAreSkipped()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        TextureFixture.Put(f.Resources, @"render\skyrim\objects\othergame\missing.nif", [0, 1, 2]);
        var result = TextureGeneration.Run(f.Request with { RenderedObjects = true });
        Assert.True(result.Published);
        Assert.Contains(result.Items, i => i.Source == @"othergame\missing.nif" && i.Status == "Skipped");
        Assert.Contains(result.Items, i => i.Source == @"lod\object.nif" && i.Status == "Written");
        Assert.False(File.Exists(GamePath.ToFileSystemPath(f.Output, @"textures\othergame\missing.dds")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalObjectFailuresRetainCompleteExistingTextures(bool unsupported)
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var bytes = AnvilLOD.Meshes.Authoring.LodNifWriter.Write("TexGen Plane",
            [TextureFixture.Card("TexGen Plane", @"textures\red.dds"), TextureFixture.Card("object", @"textures\missing.dds")]);
        if (unsupported)
        {
            var nif = AnvilLOD.Meshes.Nif.NifFile.Read(bytes);
            var shape = nif.Blocks.Where(b => b.Type == "BSTriShape").Skip(1).First();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(shape.Offset + 88), 0);
        }
        TextureFixture.Put(f.Resources, @"render\skyrim\objects\lod\object.nif", bytes);
        var result = TextureGeneration.Run(f.Request with { RenderedObjects = true });
        Assert.True(result.Published, string.Join("; ", result.Items.Select(i => i.Detail)));
        Assert.Contains(result.Items, i => i.Kind == "Rendered object" && i.Status == "Retained");
        Assert.Contains(result.Warnings, w => w.Contains("Retained existing"));
        foreach (var suffix in new[] { ".dds", "_n.dds" })
            Assert.Equal(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Data, @"textures\lod\object" + suffix)),
                File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, @"textures\lod\object" + suffix)));
        TextureGenerationManifest.Read(f.Output, true)!.Verify(f.Output);
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("mod-resource")]
    [InlineData("missing-normal")]
    [InlineData("corrupt-diffuse")]
    public void RequiredOrIncompleteObjectFallbacksStillFailAndPreservePreviousOutput(string mode)
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var request = f.Request with { RenderedObjects = true, ModelFilter = mode == "preview" ? "object" : "" };
        Assert.True(TextureGeneration.Run(request).Published);
        string before = File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName));
        var bytes = AnvilLOD.Meshes.Authoring.LodNifWriter.Write("TexGen Plane",
            [TextureFixture.Card("TexGen Plane", @"textures\red.dds"), TextureFixture.Card("object", @"textures\missing.dds")]);
        TextureFixture.Put(mode == "mod-resource" ? f.Data : f.Resources,
            (mode == "mod-resource" ? @"dyndolod\" : "") + @"render\skyrim\objects\lod\object.nif", bytes);
        if (mode == "missing-normal") File.Delete(GamePath.ToFileSystemPath(f.Data, @"textures\lod\object_n.dds"));
        if (mode == "corrupt-diffuse") TextureFixture.Put(f.Data, @"textures\lod\object.dds", [1, 2, 3]);
        var result = TextureGeneration.Run(request);
        Assert.False(result.Published); Assert.True(result.Errors > 0);
        Assert.Contains(result.Items, i => i.Status == "Failed" && i.Detail.Contains("Diffuse texture missing"));
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName)));
    }

    [Fact]
    public void UnsafeOrOverlappingDestinationsAreRejectedBeforeLodClearsAnything()
    {
        using var f = new TextureFixture();
        Assert.ThrowsAny<Exception>(() => TextureGeneration.Validate(f.Request with { OutputFolder = f.Data }));
        Assert.ThrowsAny<Exception>(() => TextureGeneration.Validate(f.Request with { OutputFolder = f.Resources }));
        Directory.CreateDirectory(f.Output); new TextureGenerationManifest().Save(f.Output);
        File.WriteAllText(Path.Combine(f.Output, "keep.txt"), "keep");
        var request = new ScanRequest(f.Game, new(), OutputFolder: f.Output, Generate: true, GeneratedTextureFolder: f.Output, CleanOutput: true);
        Assert.Throws<ArgumentException>(() => ScanPipeline.Run(request));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(f.Output, "keep.txt")));
        Assert.Throws<InvalidOperationException>(() => TextureGenerationManifest.Read(f.Output, true));
    }

    private sealed class Callback(Action<string> callback) : IProgress<string> { public void Report(string value) => callback(value); }
}
