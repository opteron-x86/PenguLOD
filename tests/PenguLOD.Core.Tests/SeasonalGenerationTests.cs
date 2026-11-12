using PenguLOD.Core.Incremental;
using PenguLOD.Core.World;
using PenguLOD.Meshes.Nif;
using PenguLOD.Plugins;
using PenguLOD.Testing;
using PenguLOD.Textures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace PenguLOD.Core.Tests;

public class SeasonalGenerationTests
{
    [Fact]
    public void NativeTreesAndBillboardsPreserveSeasonalRecordSpecificTextureVariants()
    {
        using var f = new TerrainFixture(seasons: true, treeAlternates: true);
        Assert.True(TextureGeneration.Run(f.Textures.Request with { Stitched = false, Trees = true }).Published);
        using var game = GameContext.Open(f.Game);
        var assets = game.BuildAssets(_ => true);
        TextureGenerationManifest.Read(f.Textures.Output, true)!.Attach(assets, f.Textures.Output);
        var settings = new Tree3DSettings(Enabled: true);
        var scan = new MutagenReferenceScanner(game, assets).Scan(new(Worldspaces: ["FixtureWorld"], TreeLod: true, Tree3D: settings));
        var original = Assert.Single(scan.Trees!);
        Assert.NotNull(original.Model3D);
        var winter = SeasonalAssets.SwapTree(game, assets, original,
            SeasonalAssets.Read(game, assets, "Trees")["WIN"], settings);
        Assert.NotNull(winter.Model3D);
        Assert.NotEqual(original.Billboard.TexturePath, winter.Billboard.TexturePath);
        var source = new Tree3DSource(assets, settings: settings);
        foreach (var (tree, dominant) in new[] { (original, 0), (winter, 1) })
        {
            var selected = source.Select(tree.Model3D!, 4, tree.Model3DGenerated, tree.MaterialVariant);
            Assert.NotNull(selected.MeshPath);
            Assert.Contains(source.Build(selected.MeshPath)!.Parts, p => p.Material.Textures[0]
                .Equals(dominant == 0 ? @"textures\red.dds" : @"textures\green.dds", StringComparison.OrdinalIgnoreCase));
            Assert.True(assets.TryOpen(tree.Billboard.TexturePath, out var stream));
            using (stream)
            {
                using var data = new MemoryStream(); stream.CopyTo(data);
                var pixels = TextureImages.Decode(data.ToArray()).Pixels;
                Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4 + 3] > 128
                    && pixels[i * 4 + dominant] > pixels[i * 4 + 1 - dominant] + 50);
            }
        }
    }

    [Fact]
    public void SeasonalLandscapeUsesSwappedLayersAndCorrectTexturePaths()
    {
        using var f=new TerrainFixture(seasons:true);
        var result=TerrainGeneration.Run(f.Request with {Seasons=true});
        string diffuse=@"textures\terrain\FixtureWorld\FixtureWorld.4.-4.-4.WIN.dds";
        var winter=TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output,diffuse)));
        Assert.InRange(winter.Pixels[1],225,245);Assert.InRange(winter.Pixels[0],0,20);
        var path=GamePath.ToFileSystemPath(f.Output,@"meshes\terrain\FixtureWorld\FixtureWorld.4.-4.-4.WIN.btr");
        var nif=NifGeometryReader.Read(path,File.ReadAllBytes(path),passthru:true);
        Assert.Equal(diffuse,nif.Parts[0].Material.Textures[0],ignoreCase:true);
        Assert.EndsWith(".WIN_n.dds",nif.Parts[0].Material.Textures[1],StringComparison.OrdinalIgnoreCase);
        TerrainGeneration.Run(f.Request with {Seasons=false});
        Assert.False(File.Exists(path)); // stale seasonal files disappear in transactional replacement
    }

    [Fact]
    public void SeasonalTreeAtlasBttAndListCoexistWithDefaultAndCanBeRemoved()
    {
        using var f=new TerrainFixture(seasons:true);
        var textures=TextureGeneration.Run(f.Textures.Request with {Stitched=false,Trees=true});
        Assert.True(textures.Published);
        using var game=GameContext.Open(f.Game);
        var assets=game.BuildAssets(_=>true);
        TextureGenerationManifest.Read(f.Textures.Output,true)!.Attach(assets,f.Textures.Output);
        var tree=game.LoadOrder.PriorityOrder.Tree().WinningOverrides().Single(t=>t.EditorID=="FixtureTree");
        var replacement=game.LoadOrder.PriorityOrder.Tree().WinningOverrides().Single(t=>t.EditorID=="WinterTree");
        string file=$@"textures\terrain\lodgen\skyrim.esm\testtree_{tree.FormKey.ID:x8}";
        using var info=new StreamReader(File.OpenRead(GamePath.ToFileSystemPath(f.Textures.Output,file+".txt")));
        var bb=TreeBillboard.Parse(file+".dds",info.ReadToEnd())!;
        var original=new TreeReference("test","FixtureWorld",new(-10000,-10000,0),0,1,100,bb,BaseFormKey:tree.FormKey.ToString());
        var swaps=SeasonalAssets.Read(game,assets,"Trees");
        var winter=SeasonalAssets.SwapTree(game,assets,original,swaps["WIN"],Tree3DSettings.Off);
        Assert.Equal(replacement.FormKey.ToString(),winter.BaseFormKey);
        Assert.NotEqual(original.Billboard.TexturePath,winter.Billboard.TexturePath);
        var grids=new Dictionary<string,LodGrid>{{"FixtureWorld",new("FixtureWorld",new(-4,-4,4,4,4))}};
        string output=Path.Combine(f.Root,"Tree output");var manifest=new BuildManifest();
        TreeLodGenerator.Generate(grids,[original],assets,output,manifest);
        TreeLodGenerator.Generate(grids,[winter],assets,output,manifest,season:"WIN");
        string normal=GamePath.ToFileSystemPath(output,TreeLodFiles.ListPath("FixtureWorld"));
        string seasonal=normal[..^4]+".WIN.lst";
        Assert.True(File.Exists(normal));Assert.True(File.Exists(seasonal));
        Assert.Single(Directory.GetFiles(output,"*.WIN.btt",SearchOption.AllDirectories));
        TreeLodGenerator.RemoveInactiveSeasons(output,grids.Keys,new HashSet<string>(),manifest);
        Assert.True(File.Exists(normal));Assert.False(File.Exists(seasonal));
    }

    [Fact]
    public void UnresolvedSeasonalSwapFailsBeforeReplacingTerrainWhenStrict()
    {
        using var f=new TerrainFixture(seasons:true);
        File.AppendAllText(GamePath.ToFileSystemPath(f.Textures.Data,@"seasons\Fixture_WIN.ini"),"\n[LandTextures]\nFixtureLand|MissingLand\n");
        Assert.Throws<InvalidDataException>(()=>TerrainGeneration.Run(f.Request with {Seasons=true,SeasonsStrict=true}));
        Assert.False(Directory.Exists(f.Output));
    }

    [Fact]
    public void SeasonalObjectLodSkipsUnresolvedSwapsAndWritesNothingWhenNoBlockChanges()
    {
        using var f=new TerrainFixture(seasons:true,editMod:mod=>
        {
            var a=mod.Statics.AddNew();a.EditorID="FixtureObject";a.Model=new Model {File=@"fixture\object.nif"};a.MajorFlags|=Static.MajorFlag.HasDistantLOD;
            var placed=new PlacedObject(mod.GetNextFormKey(),SkyrimRelease.SkyrimSE) {Placement=new Placement {Position=new Noggog.P3Float(-9500,-9500,0)}};
            placed.Base.SetTo(a);mod.Worldspaces.Single().SubCells[0].Items[0].Items[0].Temporary.Add(placed);
        });
        byte[] mesh=PenguLOD.Meshes.Authoring.LodNifWriter.Write("object",[TextureFixture.Card("object",@"textures\red.dds")]);
        TextureFixture.Put(f.Textures.Data,@"meshes\fixture\object.nif",mesh);TextureFixture.Put(f.Textures.Data,@"meshes\fixture\object_lod_0.nif",mesh);
        File.AppendAllText(GamePath.ToFileSystemPath(f.Textures.Data,@"seasons\Fixture_WIN.ini"),"\n[Statics]\n0x800~NotInstalled.esp|0x801~NotInstalled.esp\n");
        Assert.True(TextureGeneration.Run(f.Textures.Request with {Stitched=false,Trees=true}).Published);
        string output=Path.Combine(f.Root,"lod");
        var request=new ScanRequest(f.Game,new(Worldspaces:["FixtureWorld"],TreeLod:true,DynamicLod:false,GridObjects:false,Tree3D:new(Enabled:false)),
            OutputFolder:output,Generate:true,Seasons:true,GrassLod:false,ChildWorlds:false,SkseDll:SkseDllChoice.None,
            GeneratedTextureFolder:f.Textures.Output,RemoveBuried:false,RemoveUnderwater:false);
        var result=ScanPipeline.Run(request);
        Assert.Contains(result.Warnings,w=>w.Contains("[Statics]") && w.Contains("skipped"));
        // Billboard trees are tree LOD, not object LOD: no object block differs in winter, so no seasonal object set is written.
        Assert.Contains(Directory.GetFiles(output,"*.bto",SearchOption.AllDirectories),p=>!p.EndsWith(".WIN.bto"));
        Assert.Empty(Directory.GetFiles(output,"*.WIN.bto",SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(output,"*.WIN.btt",SearchOption.AllDirectories));
        Assert.Throws<InvalidDataException>(()=>ScanPipeline.Run(request with {SeasonsStrict=true,OutputFolder=Path.Combine(f.Root,"strict")}));
    }

    [Fact]
    public void UnresolvedSeasonalSwapIsSkippedAndReportedLikeTheGame()
    {
        // Seasons of Skyrim ignores a line for a form that is not loaded (an optional plugin, say); generation does too.
        using var f=new TerrainFixture(seasons:true);
        File.AppendAllText(GamePath.ToFileSystemPath(f.Textures.Data,@"seasons\Fixture_WIN.ini"),"\n[LandTextures]\nFixtureLand|MissingLand\n");
        var result=TerrainGeneration.Run(f.Request with {Seasons=true});
        Assert.Contains(result.Warnings,w=>w.Contains("MissingLand") && w.Contains("skipped"));
        Assert.Contains(result.Tiles,t=>t.Season=="WIN");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FullScanBuildsSeasonalTreesWithAndWithoutNative3D(bool native, bool alternates)
    {
        using var f=new TerrainFixture(seasons:true, treeAlternates:alternates);
        Assert.True(TextureGeneration.Run(f.Textures.Request with {Stitched=false,Trees=true}).Published);
        string output=Path.Combine(f.Root,"lod");
        var request=new ScanRequest(f.Game,new(Worldspaces:["FixtureWorld"],TreeLod:true,DynamicLod:false,GridObjects:false,Tree3D:new(Enabled:native)),
            OutputFolder:output,Generate:true,Seasons:true,GrassLod:false,ChildWorlds:false,SkseDll:SkseDllChoice.None,
            GeneratedTextureFolder:f.Textures.Output,RemoveBuried:false,RemoveUnderwater:false);
        var result=ScanPipeline.Run(request);
        Assert.Equal(0,result.Generation!.BlocksFailed);
        if(native)
        {
            var seasonal=Assert.Single(Directory.GetFiles(output,"*.WIN.bto",SearchOption.AllDirectories));
            var mesh=NifGeometryReader.Read(seasonal,File.ReadAllBytes(seasonal),passthru:true);
            Assert.NotEmpty(mesh.Parts);
            Assert.Contains(mesh.Parts,p=>p.Material.Textures.Any(t=>t.Contains(alternates ? "green.dds" : "red.dds")));
            if (alternates)
            {
                var defaultFile = Assert.Single(Directory.GetFiles(output, "*.bto", SearchOption.AllDirectories), p => !p.Contains(".WIN."));
                var defaultMesh = NifGeometryReader.Read(defaultFile, File.ReadAllBytes(defaultFile), passthru: true);
                Assert.Contains(defaultMesh.Parts, p => p.Material.Textures.Any(t => t.Contains("red.dds")));
            }
        }
        else Assert.Single(Directory.GetFiles(output,"*.WIN.btt",SearchOption.AllDirectories));
    }
}
