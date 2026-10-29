using System.Diagnostics;
using System.Text.Json;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;

int size=args.Length>0?int.Parse(args[0]):512, iterations=args.Length>1?int.Parse(args[1]):3;
bool fallback=args.Length>2&&args[2]=="fallback";
if(iterations is <1 or >100)throw new ArgumentException("Use 1–100 measured iterations.");
using var limits=RunLimits.Enter(4);
var runs=new List<object>();
for(int i=0;i<=iterations;i++)
{
    using var fixture=new TerrainFixture(editLandscape:fallback?land=>land.VertexHeightMap=null:null);
    if(fallback)
    {
        TextureFixture.Put(fixture.Textures.Data,@"meshes\terrain\FixtureWorld\FixtureWorld.4.-4.-4.btr",
            TerrainTileBuilder.Build(new("FixtureWorld",LodLevel.Lod4,-4,-4),(_,_)=>0,(_,_)=>64).Bytes);
        TextureFixture.Put(fixture.Textures.Data,@"textures\terrain\FixtureWorld\FixtureWorld.4.-4.-4.dds",TextureFixture.Solid(240,12,6));
    }
    var timings=new Dictionary<string,double>();
    using var measurement=WorkTiming.Begin(timings);
    var watch=Stopwatch.StartNew();var result=TerrainGeneration.Run(fixture.Request with {TextureSize=size,ExportHeightmaps=false});
    watch.Stop();
    if(i>0)runs.Add(new {Seconds=watch.Elapsed.TotalSeconds,Timings=timings,Files=result.Output.Files.Count,
        TileDigest=OutputSnapshot.HashText(string.Join('\n',result.Output.Files.Where(p=>p.Key.EndsWith(".btr")||p.Key.EndsWith(".dds")).OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value)))});
}
Console.WriteLine(JsonSerializer.Serialize(new {TextureSize=size,Threads=4,LandscapeCells=fallback?0:16,ExistingTerrain=fallback,Warmup=1,Runs=runs},PersistentPipeline.Json));
