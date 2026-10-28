using System.Buffers.Binary;
using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;

namespace AnvilLOD.Core.Tests;

public sealed class TerrainGenerationTests
{
    [Fact]
    public void SameLevelAndMixedLevelEdgesSampleTheSameWorldSurface()
    {
        float? Height(float x,float y)=>x*.125f+y*.25f+MathF.Sin(y/1000)*30;
        var west=TerrainTileBuilder.Surface(new("Test",LodLevel.Lod4,-4,-4),Height,512);
        var east=TerrainTileBuilder.Surface(new("Test",LodLevel.Lod4,0,-4),Height,512);
        var coarse=TerrainTileBuilder.Surface(new("Test",LodLevel.Lod8,0,-4),Height,512);
        for(int y=0;y<=128;y++)Assert.Equal(west.Positions[y*129+128].Z,east.Positions[y*129].Z);
        for(int y=0;y<=64;y++)Assert.Equal(east.Positions[y*2*129].Z*4,coarse.Positions[y*129].Z*8);
        Assert.Equal(129*129,west.SurfaceVertices);
        Assert.True(west.Positions.Length>west.SurfaceVertices);
        Assert.All(west.Indices,i=>Assert.True(i<west.Positions.Length));
    }
    [Theory]
    [InlineData(4)] [InlineData(8)] [InlineData(16)] [InlineData(32)]
    public void TerrainAndWaterNifRoundTripWithModelSpaceNormalShader(int level)
    {
        var result=TerrainTileBuilder.Build(new("Test",(LodLevel)level,-4,-4),(x,y)=>x*.1f+y*.2f,(x,y)=>x==-4&&y==-4?64:null);
        var mesh=NifGeometryReader.Read("terrain.btr",result.Bytes,passthru:true,includeUnshaded:true);
        Assert.Empty(mesh.Warnings);Assert.Equal(2,mesh.Parts.Count);Assert.Equal(1,result.WaterCells);
        var land=Assert.Single(mesh.Parts.Where(p=>p.Name=="land"));
        Assert.Equal(18u,land.Material.Passthru!.ShaderType);Assert.Equal(0x80401000u,land.Material.ShaderFlags1);
        Assert.Equal(new Vector2(0,1),land.UVs[0]);Assert.Equal(new Vector2(1,0),land.UVs[129*129-1]);
        var water=Assert.Single(mesh.Parts.Where(p=>ReferenceEquals(p.Material,NifGeometryReader.UnshadedMaterial)));
        Assert.All(water.Positions,p=>Assert.Equal(64,p.Z));
        var nif=NifFile.Read(result.Bytes);
        if(level==4)
        {
            var block=Assert.Single(nif.Blocks.Where(b=>b.Type=="BSSubIndexTriShape"));
            int start=block.Offset+block.Size-(4+16*9);Assert.Equal(16,BinaryPrimitives.ReadInt32LittleEndian(result.Bytes.AsSpan(start,4)));
            var segments=Enumerable.Range(0,16).Select(i=>BinaryPrimitives.ReadUInt32LittleEndian(result.Bytes.AsSpan(start+4+i*9+5,4))).ToArray();
            Assert.Equal(2u,segments[0]);Assert.All(segments.Skip(1),n=>Assert.Equal(0u,n));
        }
        else Assert.DoesNotContain(nif.Blocks,b=>b.Type=="BSSubIndexTriShape");
    }
    [Fact]
    public void HeightmapHasShaderBoundsNorthFirstRowsAndR16Precision()
    {
        var map=TerrainHeightmap.Export("Test",-4,-2,-1,1,128,(x,y)=>y/8);
        Assert.StartsWith(@"textures\terrain\Test\Test.Terrain.HeightMap.-4.-2.-1.1.",map.Path);
        Assert.Equal(56,BinaryPrimitives.ReadInt32LittleEndian(map.Bytes.AsSpan(128,4)));Assert.Equal(128,map.Width);
        float Decode(int offset)=>BinaryPrimitives.ReadUInt16LittleEndian(map.Bytes.AsSpan(offset,2))/65535f*524272-262136;
        Assert.InRange(Decode(148),1012,1020);Assert.InRange(Decode(map.Bytes.Length-2),-1020,-1012);
        Assert.Throws<InvalidDataException>(()=>TerrainHeightmap.Export("Test",0,0,0,0,128,(_,_)=>null));
    }
    [Fact]
    public void RealLandRecordsGenerateTerrainTexturesWaterAndReusableCheckpoint()
    {
        using var fixture=new TerrainFixture();var request=new GenerationPipelineRequest(Terrain:fixture.Request);
        var result=GenerationPipeline.Run(request,Path.Combine(fixture.Root,"cache"));
        Assert.True(result.Success,result.Error);Assert.NotNull(result.Terrain);var tile=Assert.Single(result.Terrain.Tiles);Assert.Equal(16,tile.WaterCells);
        string root=Path.Combine(fixture.Output,"textures","terrain","FixtureWorld");
        var diffuse=TextureImages.Decode(File.ReadAllBytes(Path.Combine(root,"FixtureWorld.4.-4.-4.dds")));
        Assert.InRange(diffuse.Pixels[0],230,245);Assert.InRange(diffuse.Pixels[1],0,20);
        var normal=TextureImages.Decode(File.ReadAllBytes(Path.Combine(root,"FixtureWorld.4.-4.-4_n.dds")));
        Assert.InRange(normal.Pixels[0],125,131);Assert.InRange(normal.Pixels[1],250,255);Assert.InRange(normal.Pixels[2],125,131);
        Assert.True(File.Exists(GamePath.ToFileSystemPath(fixture.Output,result.Terrain.Worlds[0].Heightmap!)));
        var again=GenerationPipeline.Run(request,Path.Combine(fixture.Root,"cache"));Assert.True(again.Success,again.Error);Assert.True(again.Journal.Stages[0].Reused);
    }
    [Fact]
    public void MissingLandscapeStopsBeforePublishing()
    {
        using var fixture=new TerrainFixture(missingCell:true);
        var result=GenerationPipeline.Run(new(Terrain:fixture.Request with{UseExistingTerrain=false}),Path.Combine(fixture.Root,"cache"));
        Assert.False(result.Success);Assert.Equal("terrain",result.FailedStage);Assert.Contains("Missing terrain",result.Error);Assert.False(Directory.Exists(fixture.Output));
    }
    [Fact]
    public void MissingLandCanUseExistingBtrAndItsTextures()
    {
        using var fixture=new TerrainFixture(missingCell:true);
        TextureFixture.Put(fixture.Textures.Data,@"meshes\terrain\FixtureWorld\FixtureWorld.4.-4.-4.btr",
            TerrainTileBuilder.Build(new("FixtureWorld",LodLevel.Lod4,-4,-4),(_,_)=>0,(_,_)=>64).Bytes);
        TextureFixture.Put(fixture.Textures.Data,@"textures\terrain\FixtureWorld\FixtureWorld.4.-4.-4.dds",TextureFixture.Solid(8,235,10));
        var result=TerrainGeneration.Run(fixture.Request);
        Assert.Equal(1,result.Worlds[0].ExistingTilesUsed);Assert.Equal(16,result.Tiles[0].WaterCells);
        var tile=TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(fixture.Output,@"textures\terrain\FixtureWorld\FixtureWorld.4.-4.-4.dds")));
        // Missing north-east cell uses green source terrain; the winning south-west LAND remains red.
        Assert.InRange(tile.Pixels[(0*64+63)*4+1],225,245);Assert.InRange(tile.Pixels[(63*64+0)*4],230,245);
    }
}
