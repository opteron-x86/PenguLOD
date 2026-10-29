using System.Numerics;
using System.Text;
using AnvilLOD.Core.Lod;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AnvilLOD.Core.Tests;

public class SeasonalPipelineTests
{
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
        TextureFixture.Put(f.Textures.Data,@"meshes\fixture\seasonobject_lod_0.nif",[1,2,3]);
        var failed=GenerationPipeline.Run(request with {Preflight=false},cache);
        Assert.False(failed.Success);Assert.Equal("lod",failed.FailedStage);Assert.Contains("Season",failed.Error);
        Assert.Equal(before,OutputSnapshot.HashFile(bto));
    }

    [Fact]
    public void DiagnosticsReportUnresolvedAndWrongRecordTypeBeforeGeneration()
    {
        using var f=Fixture();
        TextureFixture.Put(f.Textures.Data,@"seasons\Z_WIN.ini",Encoding.UTF8.GetBytes("[Statics]\nFixtureObject|MissingObject\n[Trees]\nFixtureTree|FixtureObject\n"));
        var request=Request(f,true);var report=PipelineDiagnostics.Run(request);
        Assert.False(report.CanGenerate);Assert.Equal(2,report.Seasons!.Sum(s=>s.Unresolved));
        Assert.Equal(2,report.Issues.Count(i=>i.Code=="unresolved-seasonal-swap"&&i.Severity=="Error"));
        Assert.False(Directory.Exists(f.Output));
    }

    private static TerrainFixture Fixture()
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
