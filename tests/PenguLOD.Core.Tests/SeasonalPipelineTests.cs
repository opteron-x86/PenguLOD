using System.Numerics;
using System.Text;
using PenguLOD.Core.Lod;
using PenguLOD.Core.Pipeline;
using PenguLOD.Core.World;
using PenguLOD.Meshes;
using PenguLOD.Meshes.Authoring;
using PenguLOD.Meshes.Nif;
using PenguLOD.Plugins;
using PenguLOD.Testing;
using PenguLOD.Textures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace PenguLOD.Core.Tests;

public class SeasonalPipelineTests
{
    [Fact]
    public void AutomaticGrassWithoutSeasonRuntimeDoesNotRequireSeasonalInputs()
    {
        using var f=new GrassFixture();
        var result=PipelineDiagnostics.Run(new(Grass:f.Request));
        Assert.DoesNotContain(result.Issues,i=>i.Code=="no-seasonal-swaps");
        Assert.Empty(result.Seasons!);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PipelineBuildsAllFourSeasonsAcrossTerrainTreesObjectsAndGrass(bool native)
    {
        using var f=Fixture();string cache=Path.Combine(f.Root,"cache");var request=Request(f,native);
        var result=GenerationPipeline.Run(request,cache);
        Assert.True(result.Success,result.Error);
        Assert.Equal(12,result.Diagnostics!.Seasons!.Count);Assert.All(result.Diagnostics.Seasons,row=>Assert.Equal(0,row.Unresolved));
        Assert.Equal(5,result.Seasons!.Count);
        foreach(var row in result.Seasons)
        {
            Assert.Equal(1,row.TerrainTiles);Assert.Equal(1,row.ObjectBlocks);
            Assert.Equal(native?0:1,row.TreeBlocks);
        }
        var defaultMesh=Read("",request.Lod!.OutputFolder!);
        var normalGrass=Assert.Single(defaultMesh.Parts,p=>p.Material.Textures[0].Equals(GrassLodSource.AtlasDiffuse,StringComparison.OrdinalIgnoreCase));
        foreach(var (season,_) in SeasonSwaps.Seasons)
        {
            string path=GamePath.ToFileSystemPath(f.Output,$@"textures\terrain\FixtureWorld\FixtureWorld.4.-4.-4.{season}.dds");
            Assert.InRange(TextureImages.Decode(File.ReadAllBytes(path)).Pixels[1],225,245);
            var mesh=Read("."+season,request.Lod.OutputFolder!);
            Assert.Contains(mesh.Parts,p=>p.Material.Textures[0].EndsWith("seasonobject.dds",StringComparison.OrdinalIgnoreCase));
            var seasonalGrass=Assert.Single(mesh.Parts,p=>p.Material.Textures[0].Equals(GrassLodSource.AtlasDiffuse,StringComparison.OrdinalIgnoreCase));
            Assert.NotEqual(normalGrass.UVs[0],seasonalGrass.UVs[0]);
            if(native) Assert.Contains(mesh.Parts,p=>p.Material.Textures[0].Equals(@"textures\green.dds",StringComparison.OrdinalIgnoreCase));
        }
        var again=GenerationPipeline.Run(request,cache);Assert.True(again.Success,again.Error);Assert.All(again.Journal.Stages,s=>Assert.True(s.Reused));
        // Same-size INI edits must invalidate cached stages even if the timestamp is restored.
        string ini=GamePath.ToFileSystemPath(f.Textures.Data,@"seasons\Fixture_WIN.ini");var stamp=File.GetLastWriteTimeUtc(ini);
        File.WriteAllText(ini,File.ReadAllText(ini).Replace("revision A","revision B"));File.SetLastWriteTimeUtc(ini,stamp);
        var changed=GenerationPipeline.Run(request,cache);Assert.True(changed.Success,changed.Error);Assert.All(changed.Journal.Stages,s=>Assert.False(s.Reused));
        string bto=GamePath.ToFileSystemPath(request.Lod.OutputFolder!,@"meshes\terrain\FixtureWorld\objects\FixtureWorld.4.-4.-4.WIN.bto");
        string before=OutputSnapshot.HashFile(bto);
        // A swap to a LOD model that cannot be read stops a strict run, and the output already there stays as it was.
        TextureFixture.Put(f.Textures.Data,@"meshes\fixture\seasonobject_lod_0.nif",[1,2,3]);
        var failed=GenerationPipeline.Run(request with {Preflight=false,Lod=request.Lod with {SeasonsStrict=true}},cache);
        Assert.False(failed.Success);Assert.Equal("lod",failed.FailedStage);Assert.Contains("Season",failed.Error);
        Assert.Contains("seasonobject_lod_0.nif",failed.Error,StringComparison.OrdinalIgnoreCase);Assert.Contains("Strict seasonal swaps are on",failed.Error);
        Assert.Equal(before,OutputSnapshot.HashFile(bto));
        // Otherwise the object has no LOD in that season, as an object with such a model has none in the normal LOD, and the run goes on.
        var skipped=GenerationPipeline.Run(request with {Preflight=false},cache);
        Assert.True(skipped.Success,skipped.Error);
        var warnings=skipped.LodReceipt!.Warnings.Where(w=>w.Contains("seasonobject_lod_0.nif",StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(4,warnings.Length);Assert.All(warnings,w=>Assert.Contains("have no LOD in this season",w));
        foreach(var (season,_) in SeasonSwaps.Seasons)
        {
            var mesh=Read("."+season,request.Lod.OutputFolder!);
            Assert.DoesNotContain(mesh.Parts,p=>p.Material.Textures[0].EndsWith("object.dds",StringComparison.OrdinalIgnoreCase));
            Assert.Contains(mesh.Parts,p=>p.Material.Textures[0].Equals(GrassLodSource.AtlasDiffuse,StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AnUnreadableModelOfAnUnswappedObjectDoesNotStopASeason()
    {
        // user01-logs7: a rain barrel no season swaps had a LOD model that could not be read. The normal LOD left it out with a
        // warning; winter rebuilt its block for another object's swap and stopped the run with "required LOD models failed".
        using var f=Fixture(mod=>
        {
            var c=mod.Statics.AddNew();c.EditorID="Neighbour";c.Model=new Model {File=@"fixture\neighbour.nif"};c.MajorFlags|=Static.MajorFlag.HasDistantLOD;
            var placed=new PlacedObject(mod.GetNextFormKey(),SkyrimRelease.SkyrimSE) {Placement=new Placement {Position=new P3Float(-9000,-9000,0)}};
            placed.Base.SetTo(c);mod.Worldspaces.Single().SubCells[0].Items[0].Items[0].Temporary.Add(placed);
        });
        TextureFixture.Put(f.Textures.Data,@"meshes\fixture\neighbour.nif",LodNifWriter.Write("object",[TextureFixture.Card("object",@"textures\object.dds")]));
        TextureFixture.Put(f.Textures.Data,@"meshes\fixture\neighbour_lod_0.nif",[1,2,3]);
        var request=Request(f,native:false);
        // Strict seasonal swaps are about the swaps; this model is in none of them.
        var result=GenerationPipeline.Run(request with {Preflight=false,Lod=request.Lod! with {SeasonsStrict=true}},Path.Combine(f.Root,"cache"));
        Assert.True(result.Success,result.Error);
        Assert.Contains(result.LodReceipt!.Warnings,w=>w.Contains("LOD meshes could not be used"));
        Assert.DoesNotContain(result.LodReceipt.Warnings,w=>w.Contains("in this season"));
        foreach(var (season,_) in SeasonSwaps.Seasons)
            Assert.Contains(Read("."+season,request.Lod.OutputFolder!).Parts,p=>p.Material.Textures[0].EndsWith("seasonobject.dds",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ASeasonalObjectWithAnAlternateTextureThatNamesNoTextureSetKeepsItsModelTexture()
    {
        // A mod's seasonal object has an alternate texture entry whose texture set is empty, or comes from a plugin that is not
        // loaded. In game the shape keeps the model's texture. Generation stopped at the end of object LOD with "Unresolved
        // seasonal alternate texture on 000807:SnowOverSkyrim.esp".
        using var f=Fixture(mod=>
        {
            var target=mod.Statics.Single(s=>s.EditorID=="SeasonObject");
            var missing=new AlternateTexture {Name="object:0",Index=0};
            missing.NewTexture.SetTo(new FormKey(ModKey.FromNameAndExtension("NotLoaded.esp"),0x800));
            target.Model!.AlternateTextures=[new AlternateTexture {Name="object:0",Index=0},missing];
        });
        var request=Request(f,native:false);
        var result=GenerationPipeline.Run(request,Path.Combine(f.Root,"cache"));
        Assert.True(result.Success,result.Error);
        var warnings=result.LodReceipt!.Warnings.Where(w=>w.Contains("SeasonObject")).ToArray();
        Assert.Contains(warnings,w=>w.Contains("names no texture set")&&w.Contains("as in game"));
        Assert.Contains(warnings,w=>w.Contains("NotLoaded.esp")&&w.Contains("not in the load order"));
        Assert.Equal(2,warnings.Length);                                           // once each, not once per season
        var mesh=Read(".WIN",request.Lod!.OutputFolder!);
        Assert.Contains(mesh.Parts,p=>p.Material.Textures[0].EndsWith("seasonobject.dds",StringComparison.OrdinalIgnoreCase));

        var strict=GenerationPipeline.Run(request with {Preflight=false,Lod=request.Lod with {SeasonsStrict=true}},Path.Combine(f.Root,"strict-cache"));
        Assert.False(strict.Success);Assert.Equal("lod",strict.FailedStage);
        Assert.Contains("Strict seasonal swaps are on",strict.Error);
    }

    [Fact]
    public void OnlyTheSelectedSeasonsAreGeneratedAndTheOthersAreRemoved()
    {
        using var f=Fixture();string cache=Path.Combine(f.Root,"cache");var all=Request(f,false);
        // A broken summer INI must not matter to a winter-only run, even a strict one.
        var winterOnly=all with {Terrain=all.Terrain! with {OnlySeasons="WIN",SeasonsStrict=true},Lod=all.Lod! with {OnlySeasons="WIN",SeasonsStrict=true}};
        var first=GenerationPipeline.Run(all,cache);Assert.True(first.Success,first.Error);
        Assert.All(first.Seasons!,row=>Assert.Equal((1,1,1),(row.TerrainTiles,row.ObjectBlocks,row.TreeBlocks)));
        TextureFixture.Put(f.Textures.Data,@"seasons\Z_SUM.ini",Encoding.UTF8.GetBytes("[Statics]\nFixtureObject|MissingObject\n[Trees]\nFixtureTree|MissingTree\n[LandTextures]\nFixtureLand|MissingLand\n"));

        var result=GenerationPipeline.Run(winterOnly,cache);
        Assert.True(result.Success,result.Error);
        Assert.Equal(["WIN"],result.Diagnostics!.Seasons!.Select(s=>s.Season).Distinct());
        Assert.DoesNotContain(result.Diagnostics.Issues,i=>i.Code=="unresolved-seasonal-swap");
        foreach(var row in result.Seasons!)
            Assert.Equal(row.Season is "Default" or "WIN"?(1,1,1):(0,0,0),(row.TerrainTiles,row.ObjectBlocks,row.TreeBlocks));
        // The winter block still carries the winter swap and the winter grass cache.
        var normalGrass=Assert.Single(Read("",all.Lod!.OutputFolder!).Parts,p=>p.Material.Textures[0].Equals(GrassLodSource.AtlasDiffuse,StringComparison.OrdinalIgnoreCase));
        var winter=Read(".WIN",all.Lod.OutputFolder!);
        Assert.Contains(winter.Parts,p=>p.Material.Textures[0].EndsWith("seasonobject.dds",StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(normalGrass.UVs[0],Assert.Single(winter.Parts,p=>p.Material.Textures[0].Equals(GrassLodSource.AtlasDiffuse,StringComparison.OrdinalIgnoreCase)).UVs[0]);
        Assert.True(OutputManagement.Verify([all.Lod.OutputFolder!,all.Textures!.OutputFolder,all.Terrain!.OutputFolder]).Valid);
        Assert.Throws<ArgumentException>(()=>TerrainGeneration.Validate(all.Terrain with {OnlySeasons="monsoon"}));
    }

    [Fact]
    public void DifferentPlansCanTakeTurnsOnOneLodFolderWithSeasonalBlocks()
    {
        // The desktop's buttons are separate plans with their own checkpoints but share the LOD output folder.
        using var f=Fixture();string cache=Path.Combine(f.Root,"cache");var full=Request(f,false);
        var lodOnly=new GenerationPipelineRequest(Lod:full.Lod! with {GeneratedTextureFolder=full.Textures!.OutputFolder,
            GeneratedTerrainFolder=full.Terrain!.OutputFolder,ObjectBrightness=0.5f},Preflight:false);
        var first=GenerationPipeline.Run(full,cache);Assert.True(first.Success,first.Error);
        string winter=GamePath.ToFileSystemPath(full.Lod!.OutputFolder!,@"meshes\terrain\FixtureWorld\objects\FixtureWorld.4.-4.-4.WIN.bto");
        string before=OutputSnapshot.HashFile(winter);
        var other=GenerationPipeline.Run(lodOnly,cache);Assert.True(other.Success,other.Error);
        Assert.NotEqual(before,OutputSnapshot.HashFile(winter));
        var again=GenerationPipeline.Run(full,cache);Assert.True(again.Success,again.Error);
        Assert.Equal(before,OutputSnapshot.HashFile(winter));
        Assert.True(OutputManagement.Verify([full.Lod.OutputFolder!,full.Textures.OutputFolder,full.Terrain.OutputFolder]).Valid);
    }

    [Fact]
    public void DiagnosticsReportUnresolvedAndWrongRecordTypeBeforeGeneration()
    {
        using var f=Fixture();
        TextureFixture.Put(f.Textures.Data,@"seasons\Z_WIN.ini",Encoding.UTF8.GetBytes("[Statics]\nFixtureObject|MissingObject\n[Trees]\nFixtureTree|FixtureObject\n"));
        var request=Request(f,true);
        // Strict seasonal swaps block generation before anything is written.
        var strict=PipelineDiagnostics.Run(request with {Lod=request.Lod! with {SeasonsStrict=true}});
        Assert.False(strict.CanGenerate);Assert.Equal(2,strict.Seasons!.Sum(s=>s.Unresolved));
        Assert.Equal(2,strict.Issues.Count(i=>i.Code=="unresolved-seasonal-swap"&&i.Severity=="Error"));
        Assert.False(Directory.Exists(f.Output));
        // By default they are reported and skipped, as Seasons of Skyrim skips them in game.
        var report=PipelineDiagnostics.Run(request);
        Assert.True(report.CanGenerate);
        Assert.Equal(2,report.Issues.Count(i=>i.Code=="unresolved-seasonal-swap"&&i.Severity=="Warning"));
    }

    private static TerrainFixture Fixture(Action<SkyrimMod>? edit=null)
    {
        uint baseId=0,seasonId=0;
        var f=new TerrainFixture(seasons:true,treeAlternates:true,editMod:mod=>
        {
            var a=mod.Statics.AddNew();a.EditorID="FixtureObject";a.Model=new Model {File=@"fixture\object.nif"};a.MajorFlags|=Static.MajorFlag.HasDistantLOD;
            var b=mod.Statics.AddNew();b.EditorID="SeasonObject";b.Model=new Model {File=@"fixture\seasonobject.nif"};b.MajorFlags|=Static.MajorFlag.HasDistantLOD;
            var placed=new PlacedObject(mod.GetNextFormKey(),SkyrimRelease.SkyrimSE) {Placement=new Placement {Position=new P3Float(-9500,-9500,0)}};
            placed.Base.SetTo(a);mod.Worldspaces.Single().SubCells[0].Items[0].Items[0].Temporary.Add(placed);
            var grass=mod.Grasses.AddNew();grass.EditorID="FixtureGrass";grass.Model=new Model {File=@"grass\testplant.nif"};baseId=grass.FormKey.ID;
            var replacement=mod.Grasses.AddNew();replacement.EditorID="SeasonGrass";replacement.Model=new Model {File=@"grass\seasonplant.nif"};seasonId=replacement.FormKey.ID;
            mod.LandscapeTextures.First().Grasses.Add(grass.ToLink());mod.LandscapeTextures.Last().Grasses.Add(replacement.ToLink());
            edit?.Invoke(mod);
        });
        string data=f.Textures.Data;
        foreach(string name in new[]{"object","seasonobject"})
        {
            byte[] mesh=LodNifWriter.Write("object",[TextureFixture.Card("object",$@"textures\{name}.dds")]);
            TextureFixture.Put(data,$@"meshes\fixture\{name}.nif",mesh);TextureFixture.Put(data,$@"meshes\fixture\{name}_lod_0.nif",mesh);
            TextureFixture.Put(data,$@"textures\{name}.dds",TextureFixture.Solid(80,100,220));
        }
        TextureFixture.Put(data,@"meshes\grass\testplant.nif",LodNifWriter.Write("grass",[TextureFixture.Card("grass",@"textures\red.dds")]));
        TextureFixture.Put(data,@"meshes\grass\seasonplant.nif",LodNifWriter.Write("grass",[TextureFixture.Card("grass",@"textures\green.dds")]));
        TextureFixture.Put(data,@"grass\FixtureWorldx-003y-003.cgid",Cache(@"grass\testplant.nif",baseId));
        foreach(var (suffix,_) in SeasonSwaps.Seasons)
        {
            TextureFixture.Put(data,$@"seasons\Fixture_{suffix}.ini",Encoding.UTF8.GetBytes("; revision A\n[Trees]\nFixtureTree|WinterTree\n[LandTextures]\nFixtureLand|WinterLand\n[Statics]\nFixtureObject|SeasonObject\n"));
            TextureFixture.Put(data,$@"grass\FixtureWorldx-003y-003.{suffix}.cgid",Cache(@"grass\seasonplant.nif",seasonId));
        }
        return f;
    }
    private static GenerationPipelineRequest Request(TerrainFixture f,bool native) => new(
        Textures:f.Textures.Request with {Stitched=false,Trees=true,Grass=true},Terrain:f.Request with {Seasons=true},
        Lod:new(f.Game,new(Worldspaces:["FixtureWorld"],TreeLod:true,DynamicLod:false,GridObjects:false,Tree3D:new(Enabled:native)),
            OutputFolder:Path.Combine(f.Root,"lod"),Generate:true,Seasons:true,GrassLod:true,GrassDensity:1,ChildWorlds:false,SkseDll:SkseDllChoice.None,
            RemoveBuried:false,RemoveUnderwater:false,CleanOutput:false));
    private static LodMesh Read(string suffix,string output)
    {
        string path=GamePath.ToFileSystemPath(output,$@"meshes\terrain\FixtureWorld\objects\FixtureWorld.4.-4.-4{suffix}.bto");
        return NifGeometryReader.Read(path,File.ReadAllBytes(path),passthru:true);
    }
    private static byte[] Cache(string model,uint id)
    {
        using var bytes=new MemoryStream();using var w=new BinaryWriter(bytes);
        w.Write(1);byte[] name=Encoding.ASCII.GetBytes(model+"\0");w.Write(name.Length);w.Write(name);w.Write(1f);w.Write(id);w.Write(new byte[]{1,1,0});w.Write(1);
        w.Write(-10000f);w.Write(-10000f);w.Write(0f);for(int i=0;i<4;i++)w.Write(0f);w.Write(1);w.Write(16);
        for(int i=0;i<16;i++)w.Write(BitConverter.HalfToUInt16Bits((Half)(i<2?-10000:i is 3 or 4?1:0)));
        return bytes.ToArray();
    }
}
