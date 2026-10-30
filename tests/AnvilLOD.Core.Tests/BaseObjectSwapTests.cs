using System.Text;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Lod;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Mo2;
using AnvilLOD.Testing;
using AnvilLOD.Meshes.Authoring;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AnvilLOD.Core.Tests;

public class BaseObjectSwapTests
{
    [Fact]
    public void ReferenceRemovalDoesNotRemoveOtherInstancesOfTheSameTree()
    {
        using var f = new Fixture();
        f.Swap("Driftwood_SWAP.ini", $"[References]\n{f.First}|0x3B~Skyrim.esm|posA(1,2,-30000)|flag(0x00000800)\n");
        var (scan, report) = f.Scan();
        Assert.Equal(2, scan.Trees!.Count);
        Assert.Equal(3, scan.Stats.PlacedObjectsVisited);
        var removed = Assert.Single(report.RemovedReferences);
        Assert.Equal(f.References[0].ToString(), removed.Reference);
        Assert.Equal("Driftwood_SWAP.ini", removed.File); Assert.Equal(2, removed.Line);
        Assert.Empty(report.Diagnostics);
    }

    [Fact]
    public void NoRuntimeMeansNoExclusionsAndAnActionableWarning()
    {
        using var f = new Fixture(runtime: false);
        f.Swap("Remove_SWAP.ini", $"[References]\n{f.First}|0x3B~Skyrim.esm\n");
        var result = ScanPipeline.Run(f.Request with { Generate = false });
        Assert.Equal(3, result.TreeReferences);
        Assert.False(result.BaseObjectSwaps!.RuntimeInstalled);
        Assert.Empty(result.BaseObjectSwaps.RemovedReferences);
        Assert.Contains(result.Warnings, w => w.Contains("po3_BaseObjectSwapper.dll is absent"));
    }

    [Fact]
    public void CaseSensitiveFileOrderAndLastRowWinEvenWhenRestoringATree()
    {
        using var f = new Fixture();
        f.Swap("ZRemove_SWAP.ini", $"[References]\n{f.First}|0x3B~Skyrim.esm\n");
        f.Swap("aRestore_SWAP.ini", $"[References]\n{f.First}|0x3B~Skyrim.esm\n{f.First}|FixtureTree\n");
        var (scan, report) = f.Scan();
        Assert.Equal(3, scan.Trees!.Count); Assert.Empty(report.RemovedReferences);
    }

    [Theory]
    [InlineData("0x3B~Skyrim.esm||chanceS(50)")]
    [InlineData("FixtureTree||chanceR(50)")]
    [InlineData("0x3B~Skyrim.esm,FixtureTree")]
    public void UnknownLaterOutcomeNeverBecomesAnUnconditionalRemoval(string later)
    {
        using var f = new Fixture();
        f.Swap("Remove_SWAP.ini", $"[References]\n{f.First}|0x3B~Skyrim.esm\n{f.First}|{later}\n");
        var (scan, report) = f.Scan();
        Assert.Equal(3, scan.Trees!.Count); Assert.Empty(report.RemovedReferences);
        Assert.NotEmpty(report.Diagnostics);
    }

    [Theory]
    [InlineData("0x3B~Skyrim.esm")]
    [InlineData("XMarker")]
    [InlineData("0000003B")]
    [InlineData("XMarker||chanceS(100)")]
    public void MarkerIdentifiersAndCertainChanceResolve(string target)
    {
        using var f = new Fixture();
        f.Swap("Remove_SWAP.ini", $"[References]\n{f.First}|{target}\n");
        Assert.Equal(2, f.Scan().Scan.Trees!.Count);
    }

    [Fact]
    public void ReferenceSwapTakesPrecedenceOverBaseRemovalAndConditionalBaseSwaps()
    {
        using var f = new Fixture();
        f.Swap("Base_SWAP.ini", $"[Forms]\nFixtureTree|XMarker\n[References]\n{f.First}|FixtureTree\n");
        Assert.Single(f.Scan().Scan.Trees!);
        f.Swap("Conditions_SWAP.ini", "[Forms|FixtureWorld]\nFixtureTree|WinterTree\n");
        Assert.Equal(3, f.Scan().Scan.Trees!.Count); // do not assume a conditional swap will fail
        f.Swap("Last_SWAP.ini", $"[References]\n{f.First}|XMarker\n");
        Assert.Equal(2, f.Scan().Scan.Trees!.Count); // explicit reference still wins
    }

    [Fact]
    public void InvalidReplacementDoesNotOverrideEarlierValidRemoval()
    {
        using var f = new Fixture();
        f.Swap("Remove_SWAP.ini", $"[References]\n{f.First}|XMarker\n{f.First}|0x800~Absent.esp\n");
        var (scan, report) = f.Scan();
        Assert.Equal(2, scan.Trees!.Count); Assert.Single(report.RemovedReferences);
        Assert.Contains(report.Diagnostics, d => d.Reason.Contains("Replacement is unresolved"));
    }

    [Fact]
    public void EnabledVirtualFilesWinAndHiddenOrDisabledModsAreIgnored()
    {
        using var f = new Fixture();
        string instance = Path.Combine(f.Root, "Manager"), mods = Path.Combine(instance, "mods"), profiles = Path.Combine(instance, "profiles");
        string profile = Path.Combine(profiles, "Default"); Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "plugins.txt"), "");
        // MO2 writes the highest-priority mod first.
        File.WriteAllText(Path.Combine(profile, "modlist.txt"), "-Disabled\n+High\n+Low\n");
        void Put(string mod, string file, string rule) => TextureFixture.Put(Path.Combine(mods, mod), file, Encoding.UTF8.GetBytes(rule));
        Put("Low", "Remove_SWAP.ini", $"[References]\n{f.First}|XMarker\n");
        Put("High", "remove_SWAP.ini", $"[References]\n{f.First}|FixtureTree\n");
        Put("High", "ZHidden_SWAP.ini.mohidden", "[Forms]\nFixtureTree|XMarker\n");
        Put("Disabled", "ZRemove_SWAP.ini", "[Forms]\nFixtureTree|XMarker\n");
        var options = new GameContextOptions(Mode: GameSourceMode.Mo2Instance, Mo2InstanceFolder: instance, Mo2Profile: "Default",
            Mo2Locations: new Mo2Locations(GamePath: Path.GetDirectoryName(f.Data), ModsFolder: mods, ProfilesFolder: profiles));
        var (scan, report) = f.Scan(options);
        Assert.Equal(3, scan.Trees!.Count); Assert.Equal(1, report.ConfigFiles);
        Assert.Empty(report.RemovedReferences);
    }

    [Fact]
    public void RemovedStaticsCannotEnterObjectLodOrNewLargeReferenceLists()
    {
        FormKey removed = default, kept = default;
        using var f = new TerrainFixture(editMod: mod =>
        {
            var obj = mod.Statics.AddNew(); obj.EditorID = "FixtureObject";
            obj.Model = new Model { File = @"fixture\object.nif" };
            obj.MajorFlags |= Static.MajorFlag.HasDistantLOD;
            for (int i = 0; i < 2; i++)
            {
                var placed = new PlacedObject(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE)
                { Placement = new Placement { Position = new P3Float(-10000 + i * 500, -10000, 0) } };
                placed.Base.SetTo(obj); mod.Worldspaces.Single().SubCells[0].Items[0].Items[0].Temporary.Add(placed);
                if (i == 0) removed = placed.FormKey; else kept = placed.FormKey;
            }
            mod.Statics.Add(new Static(new FormKey(mod.ModKey, 0x3B), SkyrimRelease.SkyrimSE) { EditorID = "XMarker" });
        });
        var data = f.Textures.Data;
        TextureFixture.Put(data, @"skse\plugins\po3_BaseObjectSwapper.dll", [0]);
        TextureFixture.Put(data, @"meshes\fixture\object.nif", LodNifWriter.Write("full", [TextureFixture.Card("object", @"textures\red.dds")]));
        TextureFixture.Put(data, @"meshes\fixture\object_lod_0.nif", LodNifWriter.Write("lod", [TextureFixture.Card("object", @"textures\red.dds")]));
        File.WriteAllText(Path.Combine(data, "Remove_SWAP.ini"), $"[References]\n0x{removed.ID:X}~Skyrim.esm|XMarker\n");
        using var game = GameContext.Open(f.Game);
        var assets = game.BuildAssets(_ => true);
        var resolver = new LodMeshResolver(LodMeshIndex.Build(assets.EnumeratePaths(@"meshes\")), new LodRules(), assets.Exists);
        var scanner = new MutagenReferenceScanner(game, assets, resolver);
        var scan = scanner.Scan(new(Worldspaces: ["FixtureWorld"], TreeLod: false, DynamicLod: false, GridObjects: false));
        Assert.Equal(kept.ToString(), Assert.Single(scan.References).FormKey);
        var large = LargeReferenceStage.Run(game, ["FixtureWorld"], Path.Combine(f.Root, "large"), null, _ => { }, default,
            excludedReferences: scanner.RemovedByBaseObjectSwapper);
        Assert.Equal(1, Assert.Single(large.Worldspaces).Skipped[LargeReferenceRules.Reason.BaseObjectSwap]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RegenerationInvalidatesStageAndClearsOldTreeLodWhenEveryTreeIsRemoved(bool native3d)
    {
        using var f = new Fixture(); using var limits = RunLimits.Enter(2);
        string cache = Path.Combine(f.Root, "cache");
        var lod = f.Request with { Scan = f.Request.Scan with { Tree3D = new Tree3DSettings(Enabled: native3d) } };
        string extension = native3d ? "*.bto" : "*.btt";
        var request = new GenerationPipelineRequest(Lod: lod, VerifySourceContents: true);
        var before = GenerationPipeline.Run(request, cache); Assert.True(before.Success, before.Error);
        Assert.NotEmpty(Directory.GetFiles(f.Output, extension, SearchOption.AllDirectories));
        f.Swap("Remove_SWAP.ini", "[Forms]\nFixtureTree|XMarker\n");
        var after = GenerationPipeline.Run(request, cache); Assert.True(after.Success, after.Error);
        Assert.All(after.Journal.Stages, s => Assert.False(s.Reused));
        Assert.Empty(Directory.GetFiles(f.Output, extension, SearchOption.AllDirectories));
        Assert.Equal(3, after.Lod!.BaseObjectSwaps!.RemovedReferences.Count);
        using (var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(after.JobFolder, "scan-report.json"))))
            Assert.Equal(3, report.RootElement.GetProperty("baseObjectSwaps").GetProperty("RemovedReferences").GetArrayLength());
        var restored = Path.Combine(f.Data, "Remove_SWAP.ini"); File.Delete(restored);
        var again = GenerationPipeline.Run(request, cache); Assert.True(again.Success, again.Error);
        Assert.NotEmpty(Directory.GetFiles(f.Output, extension, SearchOption.AllDirectories));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TerrainFixture _terrain;
        public List<FormKey> References { get; } = [];
        public string Root => _terrain.Root;
        public string Data => _terrain.Textures.Data;
        public string Output => Path.Combine(Root, "lod");
        public string First => $"0x{References[0].ID:X}~Skyrim.esm";
        public ScanRequest Request => new(_terrain.Game, new(Worldspaces: ["FixtureWorld"], DynamicLod: false, GridObjects: false),
            OutputFolder: Output, Generate: true, GrassLod: false, ChildWorlds: false, SkseDll: SkseDllChoice.None,
            RemoveBuried: false, RemoveUnderwater: false, CleanOutput: false);
        public Fixture(bool runtime = true)
        {
            FormKey treeKey = default;
            _terrain = new TerrainFixture(seasons: true, editMod: mod =>
            {
                var tree = mod.Trees.Single(t => t.EditorID == "FixtureTree"); treeKey = tree.FormKey;
                var cell = mod.Worldspaces.Single().SubCells[0].Items[0].Items.Single(c => c.Temporary.OfType<PlacedObject>().Any());
                var first = cell.Temporary.OfType<PlacedObject>().Single(); References.Add(first.FormKey);
                for (int i = 1; i < 3; i++)
                {
                    var placed = new PlacedObject(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE)
                    { Placement = new Placement { Position = new P3Float(-10000 + i * 500, -10000, 0) } };
                    placed.Base.SetTo(tree); cell.Temporary.Add(placed); References.Add(placed.FormKey);
                }
                mod.Statics.Add(new Static(new FormKey(mod.ModKey, 0x3B), SkyrimRelease.SkyrimSE) { EditorID = "XMarker" });
            });
            if (runtime) TextureFixture.Put(Data, @"skse\plugins\po3_BaseObjectSwapper.dll", [0]);
            string billboard = $@"textures\terrain\lodgen\skyrim.esm\testtree_{treeKey.ID:x8}";
            TextureFixture.Put(Data, billboard + ".dds", TextureFixture.Solid(10, 200, 20));
            TextureFixture.Put(Data, billboard + ".txt", Encoding.UTF8.GetBytes("Width=400\nHeight=400\nShiftZ=0\n"));
        }
        public void Swap(string file, string contents) => File.WriteAllText(Path.Combine(Data, file), contents);
        public (ScanResult Scan, BaseObjectSwapReport Report) Scan(GameContextOptions? options = null)
        {
            using var game = GameContext.Open(options ?? _terrain.Game);
            var scanner = new MutagenReferenceScanner(game, game.BuildAssets(_ => true));
            var result = scanner.Scan(Request.Scan);
            return (result, scanner.BaseObjectSwaps!);
        }
        public void Dispose() => _terrain.Dispose();
    }
}
