using System.Numerics;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Meshes;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using AnvilLOD.Textures.Dds;
using Xunit;

namespace AnvilLOD.Core.Tests;

public class ObjectAtlasTests
{
    [Fact]
    public void PagesAreDeterministicPairedAndKeepSeparateMaterialsAtMipEdges()
    {
        var red=TextureImages.Decode(TextureFixture.Solid(240,8,8));var green=TextureImages.Decode(TextureFixture.Solid(8,240,8));
        var entries=new[]{new ObjectTextureAtlas.Input("red",red,null,false,0),new ObjectTextureAtlas.Input("green",green,null,false,0)};
        var result=ObjectTextureAtlas.Build(entries,64,128);var reverse=ObjectTextureAtlas.Build(entries.Reverse().ToArray(),64,128);
        Assert.Equal(result.Pages[0].Diffuse,reverse.Pages[0].Diffuse);Assert.Equal(1,result.Pages.Count);
        var pixels=TextureImages.Decode(result.Pages[0].Diffuse);
        foreach(var (key,place) in result.Placements)
        {
            int x=(int)(place.Rect.U0*pixels.Width),y=(int)(place.Rect.V0*pixels.Height),p=(y*pixels.Width+x)*4;
            Assert.True(pixels.Pixels[p+(key=="red"?0:1)]>220);
        }
        Assert.Equal(4,result.Pages[0].Mips); // 64px cells stop at 8px before neighbouring materials mix
        Assert.Equal(BitConverter.ToInt32(result.Pages[0].Diffuse,28),BitConverter.ToInt32(result.Pages[0].Normal,28));
        var normals=TextureImages.Decode(result.Pages[0].Normal);
        Assert.InRange(normals.Pixels[0],126,130);Assert.InRange(normals.Pixels[2],250,255);
    }

    [Fact]
    public void RemappingPreservesGeometryAndLeavesTiledUvMaterialsAlone()
    {
        var original=TextureFixture.Card("part","original.dds");
        var tiled=new LodMeshPart{Material=original.Material,Positions=original.Positions,Normals=original.Normals,Tangents=original.Tangents,Bitangents=original.Bitangents,
            Triangles=original.Triangles,UVs=[new(-1,0),new(2,0),new(0,1),new(2,1)]};
        var mesh=new LodMesh{Path="test",Parts=[original,tiled]};
        var result=ObjectAtlasStage.Remap(mesh,new Dictionary<string,ObjectAtlasStage.Mapping>{{original.Material.Key,new("atlas.dds","atlas_n.dds",new(.1f,.2f,.4f,.6f))}});
        Assert.Same(tiled,result.Parts[1]);Assert.Same(original.Positions,result.Parts[0].Positions);
        Assert.Equal(new Vector2(.1f,.6f),result.Parts[0].UVs[0]);Assert.NotEqual(original.Material.Key,result.Parts[0].Material.Key);
        Assert.Contains("tiling",ObjectAtlasStage.Ineligible(tiled));
    }

    [Fact]
    public void CompatibleSurfacesShareMaterialAfterPackingWhileShaderDifferencesRemain()
    {
        using var fixture=new TextureFixture();using var game=GameContext.Open(fixture.Game);var assets=game.BuildAssets(_=>true);
        var red=TextureFixture.Card("a",@"textures\red.dds");var green=TextureFixture.Card("b",@"textures\green.dds");
        var model=new LodMesh{Path="objects",Parts=[red,green]};string output=Path.Combine(fixture.Root,"atlas");Directory.CreateDirectory(output);
        var atlas=ObjectAtlasStage.Build([model],assets,output,64);
        Assert.Equal(2,atlas.Report.PackedMaterials);Assert.Equal(1,atlas.Report.Pages);
        var mapped=atlas.Transform(model);Assert.Equal(mapped.Parts[0].Material,mapped.Parts[1].Material);
        Assert.NotEqual(mapped.Parts[0].UVs[0],mapped.Parts[1].UVs[0]);
        Assert.True(File.Exists(AnvilLOD.Core.World.GamePath.ToFileSystemPath(output,mapped.Parts[0].Material.Textures[0])));
    }
}
