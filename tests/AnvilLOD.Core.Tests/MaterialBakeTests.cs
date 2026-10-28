using System.Numerics;
using AnvilLOD.Meshes;
using AnvilLOD.Plugins.Authoring;
using Xunit;

namespace AnvilLOD.Core.Tests;

public class MaterialBakeTests
{
    private static LodMaterial Material => new([@"textures\pbr\leaf.dds", "normal", "", "", "", "rmaos"], 0, 0, 3, true, 4844, 128, Vector3.Zero, 1);
    private static LodMesh Card(LodMaterial? material = null) => new()
    {
        Path = "card", Parts = [new() { Material = material ?? Material,
            Positions = [new(0,0,0), new(100,0,0), new(0,0,100), new(100,0,100)],
            UVs = [new(0,1), new(1,1), new(0,0), new(1,0)],
            Normals = Enumerable.Repeat(-Vector3.UnitY,4).ToArray(), Tangents = Enumerable.Repeat(Vector3.UnitX,4).ToArray(),
            Bitangents = Enumerable.Repeat(-Vector3.UnitZ,4).ToArray(), Triangles = [0,1,2,1,3,2] }]
    };
    private static (byte[],int,int)? Texture(string path) => path switch
    { "normal" => (new byte[]{218,128,218,0},1,1), "rmaos" => (new byte[]{128,0,128,255},1,1), _ => (new byte[]{200,200,200,255},1,1) };

    [Fact]
    public void NormalTexturePerturbsTheCardAndCalibrationHasIndependentControls()
    {
        var baseImage = BillboardRenderer.Render(Card(), Texture, 32, shaded:false, materials:new(SourceNormals:false,PbrAlbedo:false))!;
        var baked = BillboardRenderer.Render(Card(), Texture, 32, shaded:false, materials:new(PbrScale:1,PbrGamma:1,OcclusionStrength:1))!;
        int p = (16*32+16)*4;
        Assert.InRange(baseImage.Rgba[p],199,201);
        Assert.InRange(baked.Rgba[p],99,102);
        Assert.InRange(baseImage.NormalRgba![p],127,129);
        Assert.InRange(baked.NormalRgba![p],210,225);
        Assert.Equal(255,baked.Rgba[p+3]);
    }

    [Fact]
    public void AlphaTestRemainsIndependentOfPbrOcclusion()
    {
        (byte[],int,int)? Transparent(string path) => path.Contains("leaf") ? (new byte[]{255,255,255,0},1,1) : Texture(path);
        Assert.Null(BillboardRenderer.Render(Card(),Transparent,32));
        (byte[],int,int)? BlackAo(string path) => path=="rmaos" ? (new byte[]{128,0,0,255},1,1) : Texture(path);
        var image=BillboardRenderer.Render(Card(),BlackAo,32,shaded:false,materials:new(OcclusionStrength:1))!;
        Assert.Equal(1,image.Coverage);
        Assert.Equal(0,image.Rgba[0]);
    }

    [Fact]
    public void UvTransformAndClampPreventSamplingTheOppositeEdge()
    {
        var shader=new LodShaderPassthru(0,2,0,1,1,1,0,0,Vector3.Zero,0,0,0);
        var material=Material with { Textures=["stripe"],ClampMode=0,Passthru=shader };
        var result=BillboardRenderer.Render(Card(material),_ => (new byte[]{255,0,0,255,0,255,0,255},2,1),32,shaded:false)!;
        Assert.All(Enumerable.Range(0,result.Width*result.Height),i=>{Assert.Equal(0,result.Rgba[i*4]);Assert.Equal(255,result.Rgba[i*4+1]);});
    }

    [Fact]
    public void CopyingAMaterialCannotReuseItsOldGroupingKey()
    {
        var original=Material;string before=original.Key;
        var changed=original with { Textures=["different"] };
        Assert.NotEqual(before,changed.Key);Assert.NotEqual(original,changed);
        Assert.Equal(original,original with {});
    }
}
