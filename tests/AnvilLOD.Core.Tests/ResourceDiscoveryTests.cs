using System.Text;
using AnvilLOD.Core.Lod;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AnvilLOD.Core.Tests;

public class ResourceDiscoveryTests
{
    private const string Recipe = "DynDOLOD_SSE_TexGen_noalpha_skyrimesm.txt";
    private static void Put(string root, string path, string text) => TextureFixture.Put(root, path, Encoding.UTF8.GetBytes(text));

    [Theory]
    [InlineData("")]
    [InlineData(@"dYnDoLoD")]
    [InlineData(@"dAtA\dYnDoLoD")]
    [InlineData(@"eDiT sCrIpTs\dYnDoLoD")]
    public void ResourceOnlyFoldersSupportAllBundleLayoutsAndCase(string layout)
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        string pack = Path.Combine(f.Root, "resources only"), prefix = layout.Length == 0 ? "" : layout + "\\";
        Put(pack, prefix + @"cOnFiGs\" + Recipe, TextureFixture.Tile(@"textures\green.dds", @"textures\lod\green.dds", 0));
        var result = TextureGeneration.Run(f.Request with { DynDolodFolder = null, ResourcePacks = [pack] });
        Assert.True(result.Published); Assert.Equal(1, result.Resources!.Configs);
        Assert.Empty(Directory.GetFiles(pack, "*.exe", SearchOption.AllDirectories));
        Assert.Contains(result.Items, i => i.Output == @"textures\lod\green.dds" && i.Status == "Written");
    }

    private sealed class Messages(List<string> lines) : IProgress<string> { public void Report(string value) => lines.Add(value); }

    [Fact]
    public void ScanOnlyNeverStartsOcclusionPublicationOrUndersideTerrainPreparation()
    {
        using var f = new TerrainFixture(); var messages = new List<string>();
        string output = Path.Combine(f.Root, "scan-only");
        ScanPipeline.Run(new(f.Game, new(TreeLod: false, DynamicLod: false, GridObjects: false, MatchTextures: false),
            output, GrassLod: false, RemoveBuried: false, RemoveUnderwater: false, Underside: true, Occlusion: new()), new Messages(messages));
        Assert.False(Directory.Exists(output));
        Assert.DoesNotContain(messages, m => m.StartsWith("Reading terrain heights") || m.StartsWith("Occlusion"));
    }

    [Fact]
    public void WindowsUnicodeRecipesAndMixedCaseChildworldMatchesRemainReadable()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        File.WriteAllText(f.RuleFile, TextureFixture.Tile(@"textures\green.dds", @"textures\lod\unicode.dds", 0), Encoding.Unicode);
        var result = TextureGeneration.Run(f.Request); Assert.True(result.Published);
        Assert.Contains(result.Items, i => i.Output.EndsWith("unicode.dds") && i.Status == "Written");
        var copies = ChildWorldCopies.FromFiles(new Dictionary<string, Func<string>> {
            ["DYNDolod_SSE_ChildworldMatches.TXT"] = () => "Skyrim.esm;00000001;Skyrim.esm;00000002;test"
        }, ["Skyrim.esm"], LodPreset.High);
        Assert.True(copies.IsMatched(LodRules.FormIdKey("Skyrim.esm", 1)));
    }

    [Fact]
    public void LaterPacksAndStructuredEnabledModResourcesWinWithProvenance()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        string pack = Path.Combine(f.Root, "second pack");
        Put(pack, @"Configs\" + Recipe, "invalid external recipe");
        Put(f.Data, @"DynDOLOD\" + Recipe, "invalid flat recipe");
        Put(f.Data, @"DynDOLOD\Configs\" + Recipe, TextureFixture.Tile(@"textures\green.dds", @"textures\lod\winner.dds", 0));
        var result = TextureGeneration.Run(f.Request with { ResourcePacks = [pack] });
        Assert.True(result.Published);
        var row = Assert.Single(result.Resources!.Files, r => r.Path == @"configs\" + Recipe.ToLowerInvariant());
        Assert.True(row.FromEnabledMods); Assert.Equal(3, row.Overridden.Count);
        Assert.Contains("second pack", row.Overridden[1]);
        Assert.True(TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, @"textures\lod\winner.dds"))).Pixels[1] > 220);
    }

    [Fact]
    public void RenderAliasesHonorPackOrderThenEnabledModsAndModernLayout()
    {
        using var f = new TextureFixture(); string second = Path.Combine(f.Root, "second");
        TextureFixture.Put(f.Resources, @"render\objects\lod\fixture.nif", [1]);
        TextureFixture.Put(second, @"render\skyrim\objects\lod\fixture.nif", [2]);
        using var game = GameContext.Open(f.Game with { ExtraLooseRoots = ["dyndolod"] });
        var external = ResourceCatalog.Load(game, game.BuildAssets(), f.Resources, [second]);
        Assert.Equal(new byte[] { 2 }, external.RenderModels("objects").Last(p => p.Relative == @"lod\fixture.nif").File.Read());
        var repeated = ResourceCatalog.Load(game, game.BuildAssets(), f.Resources, [second, f.Resources]);
        Assert.Equal(new byte[] { 1 }, repeated.RenderModels("objects").Last(p => p.Relative == @"lod\fixture.nif").File.Read());
        TextureFixture.Put(f.Data, @"dyndolod\render\skyrim\objects\lod\fixture.nif", [3]);
        TextureFixture.Put(f.Data, @"dyndolod\render\objects\lod\fixture.nif", [4]);
        using var updated = GameContext.Open(f.Game with { ExtraLooseRoots = ["dyndolod"] });
        var modded = ResourceCatalog.Load(updated, updated.BuildAssets(), f.Resources, [second]);
        Assert.Equal(new byte[] { 4 }, modded.RenderModels("objects").Last(p => p.Relative == @"lod\fixture.nif").File.Read());
    }

    [Fact]
    public void ModernModObjectRenderLayoutWorksWithoutExternalResources()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        TextureFixture.Put(f.Data, @"dyndolod\render\objects\lod\object.nif", File.ReadAllBytes(GamePath.ToFileSystemPath(f.Resources, @"render\skyrim\objects\lod\object.nif")));
        var result = TextureGeneration.Run(f.Request with { DynDolodFolder = null, Stitched = false, RenderedObjects = true });
        Assert.True(result.Published); Assert.Equal(0, result.Errors);
        Assert.Contains(result.Items, i => i.Kind == "Rendered object" && i.Status == "Written");
    }

    [Fact]
    public void AdditionalPackContentInvalidatesResumeButUnrelatedExecutablesDoNot()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        string pack = Path.Combine(f.Root, "pack"); string recipe = GamePath.ToFileSystemPath(pack, @"Configs\" + Recipe);
        Put(pack, @"Configs\" + Recipe, TextureFixture.Tile(@"textures\green.dds", @"textures\lod\winner.dds", 0));
        var request = new GenerationPipelineRequest(Textures: f.Request with { DynDolodFolder = null, ResourcePacks = [pack] });
        string cache = Path.Combine(f.Root, "cache");
        var first = GenerationPipeline.Run(request, cache); Assert.True(first.Success, first.Error);
        var reused = GenerationPipeline.Run(request, cache); Assert.True(reused.Success, reused.Error); Assert.True(reused.Journal.Stages.Single().Reused);
        File.WriteAllText(Path.Combine(pack, "unused.exe"), "not a program");
        var unrelated = GenerationPipeline.Run(request, cache); Assert.True(unrelated.Success, unrelated.Error); Assert.True(unrelated.Journal.Stages.Single().Reused);
        var stamp = File.GetLastWriteTimeUtc(recipe);
        File.WriteAllText(recipe, TextureFixture.Tile(@"textures\red.dds", @"textures\lod\winner.dds", 0)); File.SetLastWriteTimeUtc(recipe, stamp);
        var changed = GenerationPipeline.Run(request, cache); Assert.True(changed.Success, changed.Error); Assert.False(changed.Journal.Stages.Single().Reused);
        Assert.True(TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, @"textures\lod\winner.dds"))).Pixels[0] > 220);
    }

    [Fact]
    public void EnabledModRulesLookupAndChildWorldConfigReachTheScanner()
    {
        using var f = new TerrainFixture(editMod: mod =>
        {
            var parent = mod.Worldspaces.Single();
            var child = mod.Worldspaces.AddNew(); child.EditorID = "FixtureCity"; child.Parent = new WorldspaceParent(); child.Parent.Worldspace.SetTo(parent);
            var stat = mod.Statics.AddNew(); stat.EditorID = "FixtureWall"; stat.Model = new Model { File = @"fixture\original.nif" }; stat.MajorFlags |= Static.MajorFlag.HasDistantLOD;
            var placed = new PlacedObject(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE) { Placement = new Placement { Position = new P3Float(-10000, -10000, 0) } }; placed.Base.SetTo(stat);
            var cell = new Cell(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE) { Grid = new CellGrid { Point = new P2Int(-3, -3) } }; cell.Temporary.Add(placed);
            var sub = new WorldspaceSubBlock { BlockNumberX = -1, BlockNumberY = -1, GroupType = (GroupTypeEnum)5 }; sub.Items.Add(cell);
            var block = new WorldspaceBlock { BlockNumberX = -1, BlockNumberY = -1, GroupType = (GroupTypeEnum)4 }; block.Items.Add(sub); child.SubCells.Add(block);
        });
        string data = f.Textures.Data;
        var model = LodNifWriter.Write("wall", [TextureFixture.Card("wall", @"textures\red.dds")]);
        TextureFixture.Put(data, @"meshes\fixture\original.nif", model);
        TextureFixture.Put(data, @"meshes\fixture\replacement_lod_0.nif", model);
        Put(data, @"DynDOLOD\Rules\DynDOLOD_SSE_skyrimesm.ini", "[Skyrim LODGen]\nLODGen1=fixture\\,Level0,None,None,,Unchanged,0\n");
        Put(data, @"DynDOLOD\Configs\DynDOLOD_SSE_mesh_lookup.txt", @"meshes\fixture\original.nif=meshes\fixture\replacement.nif");
        const string config = @"DynDOLOD\Configs\DynDOLOD_SSE_childworld_FixtureCity.ini";
        Put(data, config, "[FixtureWorld]\nScanChild=1\nNoCellsWithNAVM=0\n");
        var request = new ScanRequest(f.Game, new(Worldspaces: ["FixtureWorld"], TreeLod: false, DynamicLod: false, GridObjects: false, MatchTextures: false),
            GrassLod: false, DetailedShips: false, RemoveBuried: false, RemoveUnderwater: false);
        using var limits = RunLimits.Enter(2, cacheDirectory: Path.Combine(f.Root, "cache"));
        var scan = ScanPipeline.Run(request);
        var reference = Assert.Single(scan.Quads.SelectMany(p => p.Value));
        Assert.Equal("FixtureWorld", reference.Worldspace); Assert.Equal(@"meshes\fixture\replacement_lod_0.nif", reference.Meshes.Lod4);
        Assert.Equal(1, scan.Resources!.Rules); Assert.Equal(2, scan.Resources.Configs);
        Put(data, config, "[FixtureWorld]\nScanChild=0\n");
        Assert.Empty(ScanPipeline.Run(request).Quads);
    }

    [Fact]
    public void InvalidAndNestedResourceFoldersAreRejectedBeforePublicationAndRedactedInDiagnostics()
    {
        using var f = new TextureFixture();
        string missing = Path.Combine(f.Root, "private resources");
        var request = f.Request with { DynDolodFolder = null, ResourcePacks = [missing] };
        Assert.Throws<DirectoryNotFoundException>(() => TextureGeneration.Run(request)); Assert.False(Directory.Exists(f.Output));
        var diagnostic = PipelineDiagnostics.Run(new(Textures: request));
        var issue = Assert.Single(diagnostic.Issues, i => i.Code == "invalid-resource-pack");
        Assert.DoesNotContain(f.Root, issue.Message); Assert.Contains("<resource-1>", issue.Message);
        Assert.Throws<ArgumentException>(() => TextureGeneration.Validate(f.Request with { OutputFolder = Path.Combine(f.Resources, "output") }));
        var lod = new ScanRequest(f.Game, new(), Path.Combine(f.Resources, "output"), ResourcePacks: [f.Resources]);
        Assert.Throws<ArgumentException>(() => GenerationPipeline.Run(new(Lod: lod), Path.Combine(f.Root, "cache")));
        // A texture resource pack must also be protected from the LOD stage's destination.
        Assert.Throws<ArgumentException>(() => GenerationPipeline.Run(new(Textures: f.Request, Lod: lod with { ResourcePacks = null }), Path.Combine(f.Root, "cache")));
    }
}
