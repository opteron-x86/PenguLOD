using System.Numerics;
using System.Text.Json;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Core.Tests;

public class TerrainResumeTests
{
    [Fact]
    public void CancellationKeepsCompletedTileAndPublishedOutputThenResumesTheSeason()
    {
        using var f=new TerrainFixture(seasons:true);var request=new GenerationPipelineRequest(Terrain:f.Request with {Seasons=true});
        TerrainGeneration.Run(request.Terrain!);
        string original=OutputSnapshot.Capture(f.Output).Digest,cache=Path.Combine(f.Root,"cache");
        using var cancel=new CancellationTokenSource();
        var progress=new Callback(text=>{if(text.StartsWith("Completed terrain tile"))cancel.Cancel();});
        Assert.Throws<OperationCanceledException>(()=>GenerationPipeline.Run(request,cache,progress,cancel.Token));
        Assert.Equal(original,OutputSnapshot.Capture(f.Output).Digest);
        Assert.Single(Directory.GetFiles(cache,"entry.json",SearchOption.AllDirectories));
        var resumed=GenerationPipeline.Run(request,cache);
        Assert.True(resumed.Success,resumed.Error);Assert.Equal(1,resumed.Terrain!.Performance!.BuiltTiles);Assert.Equal(1,resumed.Terrain.Performance.ReusedTiles);
        Assert.Equal(2,resumed.Terrain.Tiles.Count);Assert.Contains(resumed.Terrain.Tiles,t=>t.Season=="WIN");
        Assert.True(resumed.Journal.Stages.Single().Timings["terrain-cache-verification"]>0);
        Assert.True(resumed.Terrain.Performance.Timings["terrain-sampling"]>0);
    }

    [Theory]
    [InlineData("contents")] [InlineData("missing")] [InlineData("manifest")] [InlineData("link")]
    public void DamagedTileIsRebuiltWhileOtherVerifiedTilesAreRetained(string damage)
    {
        if(damage=="link"&&OperatingSystem.IsWindows())return;
        using var f=new TerrainFixture(seasons:true);string cache=Path.Combine(f.Root,"cache");
        var request=new GenerationPipelineRequest(Terrain:f.Request with {Seasons=true});
        var first=GenerationPipeline.Run(request,cache);Assert.True(first.Success,first.Error);
        string metadata=Directory.GetFiles(cache,"entry.json",SearchOption.AllDirectories).First();
        var entry=JsonSerializer.Deserialize<TerrainTileReceipt>(File.ReadAllText(metadata))!;
        string relative=entry.Output.Files.Keys.First(k=>k.EndsWith(".dds"));
        string cached=OutputSnapshot.Resolve(Path.GetDirectoryName(metadata)!,relative);
        string sentinel=Path.Combine(f.Root,"sentinel");File.WriteAllText(sentinel,"untouched");
        switch(damage)
        {
            case "contents": File.WriteAllBytes(cached,[1,2,3]);break;
            case "missing": File.Delete(cached);break;
            case "manifest":
                entry.Output.Files.Clear();entry.Output.Files["../sentinel"]="bad";
                File.WriteAllText(metadata,JsonSerializer.Serialize(entry));break;
            case "link": File.Delete(cached);File.CreateSymbolicLink(cached,sentinel);break;
        }
        // Force stage repair: a published output disappeared, but the source profile is unchanged.
        File.Delete(Directory.GetFiles(f.Output,"*.dds",SearchOption.AllDirectories).First());
        var repaired=GenerationPipeline.Run(request,cache);
        Assert.True(repaired.Success,repaired.Error);Assert.False(repaired.Journal.Stages.Single().Reused);
        Assert.Equal(1,repaired.Terrain!.Performance!.BuiltTiles);Assert.Equal(1,repaired.Terrain.Performance.ReusedTiles);
        Assert.Equal("untouched",File.ReadAllText(sentinel));
        foreach(var (path,hash) in first.Terrain!.Output.Files.Where(p=>p.Key!=TerrainGeneration.ManifestName))
            Assert.Equal(hash,repaired.Terrain.Output.Files[path]);
        repaired.Terrain.Output.Verify(f.Output);
    }

    [Fact]
    public void SettingsSourceChangesAndDisabledResumeRegenerateTiles()
    {
        using var f=new TerrainFixture();string cache=Path.Combine(f.Root,"cache");var request=new GenerationPipelineRequest(Terrain:f.Request);
        Assert.True(GenerationPipeline.Run(request,cache).Success);
        var changed=GenerationPipeline.Run(request with {Terrain=f.Request with {Brightness=.8f}},cache);
        Assert.True(changed.Success,changed.Error);Assert.Equal(0,changed.Terrain!.Performance!.ReusedTiles);
        Assert.Equal(1,changed.Terrain.Performance.BuiltTiles);
        TextureFixture.Put(f.Textures.Data,@"textures\red.dds",TextureFixture.Solid(20,230,20));
        changed=GenerationPipeline.Run(request,cache);Assert.True(changed.Success,changed.Error);Assert.Equal(0,changed.Terrain!.Performance!.ReusedTiles);
        var fresh=GenerationPipeline.Run(request with {Resume=false},cache);Assert.True(fresh.Success,fresh.Error);
        Assert.Equal(1,fresh.Terrain!.Performance!.BuiltTiles);Assert.Equal(0,fresh.Terrain.Performance.ReusedTiles);
    }

    [Fact]
    public void CachedFallbackTilesRetainMaterialWarningsAndCoverage()
    {
        using var f=new TerrainFixture(missingCell:true);string cache=Path.Combine(f.Root,"cache");
        TextureFixture.Put(f.Textures.Data,@"meshes\terrain\FixtureWorld\FixtureWorld.4.-4.-4.btr",
            AnvilLOD.Meshes.TerrainTileBuilder.Build(new("FixtureWorld",LodLevel.Lod4,-4,-4),(_,_)=>0,(_,_)=>64).Bytes);
        TextureFixture.Put(f.Textures.Data,@"textures\terrain\FixtureWorld\FixtureWorld.4.-4.-4.dds",TextureFixture.Solid(40,50,60));
        var request=new GenerationPipelineRequest(Terrain:f.Request);var first=GenerationPipeline.Run(request,cache);Assert.True(first.Success,first.Error);
        File.Delete(Directory.GetFiles(f.Output,"*.dds",SearchOption.AllDirectories).First());
        var restored=GenerationPipeline.Run(request,cache);Assert.True(restored.Success,restored.Error);
        Assert.Equal(1,restored.Terrain!.Performance!.ReusedTiles);Assert.Equal(0,restored.Terrain.Performance.BuiltTiles);
        Assert.Equal(first.Terrain!.Worlds,restored.Terrain.Worlds);Assert.Equal(first.Terrain.Materials,restored.Terrain.Materials);
        Assert.Equal(first.Terrain.Warnings.Order(),restored.Terrain.Warnings.Order());
    }

    [Fact]
    public void PipelineRejectsCacheInsideSourceBeforeWritingAJob()
    {
        using var f=new TerrainFixture();string cache=Path.Combine(f.Textures.Data,"textures","cache");
        Assert.Throws<InvalidOperationException>(()=>GenerationPipeline.Run(new(Terrain:f.Request),cache));
        Assert.False(Directory.Exists(cache));Assert.False(Directory.Exists(f.Output));
    }

    [Fact]
    public void OptimizedHeightSamplingMatchesGlobalVerticesAtNegativeEdgesAndSlopes()
    {
        using var f=new TerrainFixture(editMod:mod=>
        {
            foreach(var cell in mod.Worldspaces.Single().SubCells.SelectMany(b=>b.Items).SelectMany(b=>b.Items))
            {
                var height=cell.Landscape!.VertexHeightMap!;var coord=cell.Grid!.Point;height.Offset=coord.X*32+coord.Y*32;
                for(int y=0;y<33;y++)for(int x=0;x<33;x++)height.HeightMap[x,y]=1;
            }
        });
        using var game=GameContext.Open(f.Game);var world=game.LoadOrder.PriorityOrder.Worldspace().WinningOverrides().Single();
        var source=new TerrainSource(game,game.BuildAssets(_=>true),world,new("FixtureWorld",new(-4,-4,4,4,4)),false,default);
        for(int gy=-129;gy<=1;gy++)for(int gx=-129;gx<=1;gx++)
            Assert.Equal(source.Heights.VertexAt("FixtureWorld",gx,gy),source.Height(gx*128,gy*128));
        for(int i=1;i<200;i++)
        {
            float x=-16384+i*80.25f,y=-16384+i*70.5f;
            Assert.Equal(source.Heights.HeightAt("FixtureWorld",x,y)!.Value,source.Height(x,y)!.Value,3);
        }
    }
    private sealed class Callback(Action<string> run):IProgress<string> {public void Report(string value)=>run(value);}
}
