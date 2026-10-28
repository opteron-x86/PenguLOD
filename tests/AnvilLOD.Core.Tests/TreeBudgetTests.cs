using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using Mutagen.Bethesda;

namespace AnvilLOD.Core.Tests;

public sealed class TreeBudgetTests
{
    private static LodMeshPart Grid(bool alpha,int n=20)
    {
        var positions=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<ushort>();
        for(int y=0;y<=n;y++)for(int x=0;x<=n;x++)
        {positions.Add(new(x*20,20*MathF.Sin(x*.7f)*MathF.Cos(y*.3f),y*20));uv.Add(new(x/(float)n,y/(float)n));}
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {int a=y*(n+1)+x;indices.AddRange([(ushort)a,(ushort)(a+1),(ushort)(a+n+1),(ushort)(a+1),(ushort)(a+n+2),(ushort)(a+n+1)]);}
        int count=positions.Count;
        return new LodMeshPart {Material=new([@"textures\red.dds",@"textures\red_n.dds"],0,0,3,alpha,4844,128,Vector3.Zero,1),
            Positions=positions.ToArray(),UVs=uv.ToArray(),Normals=Enumerable.Repeat(-Vector3.UnitY,count).ToArray(),
            Tangents=Enumerable.Repeat(Vector3.UnitX,count).ToArray(),Bitangents=Enumerable.Repeat(Vector3.UnitZ,count).ToArray(),Triangles=indices.ToArray()};
    }
    [Fact]
    public void AlphaGeometryAndUvIslandsArePreservedEvenWhenBudgetCannotBeMet()
    {
        var alpha=Grid(true);var result=TreeMeshReducer.Reduce(new(){Path="tree",Parts=[alpha]},10,256);
        Assert.True(result.OverBudget);Assert.Same(alpha,result.Mesh.Parts[0]);Assert.Equal(alpha.TriangleCount,result.Triangles);
    }
    [Fact]
    public void NativeTrunksProduceDistinctNearAndFarMeshesWithLowerFarCost()
    {
        using var fixture=new TextureFixture();const string path=@"meshes\trees\native.nif";
        TextureFixture.Put(fixture.Data,path,LodNifWriter.Write("trunk",[Grid(false)]));
        var source=new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE,fixture.Data),settings:new(true,Lod8:true,NearMaxError:0,FarMaxError:30));
        Assert.Null(source.Validate(path));var near=source.Select(path,4,true);var far=source.Select(path,8,true);
        Assert.NotNull(near.MeshPath);Assert.NotNull(far.MeshPath);Assert.NotEqual(near.MeshPath,far.MeshPath);Assert.True(far.Triangles<near.Triangles);
        var original=source.Build(near.MeshPath)!.Parts[0];var reduced=source.Build(far.MeshPath)!.Parts[0];
        Assert.All(reduced.UVs,uv=>Assert.Contains(uv,original.UVs));Assert.Equal(original.Material,reduced.Material);
        Assert.NotEqual(source.Fingerprint(near.MeshPath),new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE,fixture.Data),settings:new(true,NearTriangleBudget:1)).Fingerprint(near.MeshPath));
    }
    [Fact]
    public void AuthoredFarModelIsSelectedAndCoverageAccountsForEveryInstance()
    {
        using var fixture=new TextureFixture();const string nearPath=@"meshes\trees\pine_lod_0.nif";const string farPath=@"meshes\trees\pine_lod_1.nif";
        TextureFixture.Put(fixture.Data,nearPath,LodNifWriter.Write("near",[Grid(false)]));
        TextureFixture.Put(fixture.Data,farPath,LodNifWriter.Write("far",[TextureFixture.Card("far",@"textures\red.dds")]));
        var settings=new Tree3DSettings(true,Lod8:true,NearMaxError:0);var source=new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE,fixture.Data),settings:settings);
        var tree=new TreeReference("1", "Test",Vector3.Zero,0,1,1,new(@"textures\tree.dds",100,200,0),Model3D:nearPath);
        var report=source.Report([tree,tree with{FormKey="2"}],settings);
        var far=Assert.Single(report,r=>r.Level==8);Assert.Equal(farPath,far.SourceModel);Assert.Equal("Authored far model",far.Method);Assert.Equal(4,far.PlacedTriangles);
        var reference=Assert.Single(source.ToReferences([tree],settings));Assert.NotEqual(reference.Meshes.Lod4,reference.Meshes.Lod8);
    }
    [Fact]
    public void OverBudgetFoliageUsesBillboardsAndPlainBillboardTexturesAreUsable()
    {
        using var fixture=new TextureFixture();const string path=@"meshes\trees\leaves.nif";
        TextureFixture.Put(fixture.Data,path,LodNifWriter.Write("leaves",[Grid(true)]));
        var assets=AssetIndex.Build(GameRelease.SkyrimSE,fixture.Data);var settings=new Tree3DSettings(true,Lod8:true,NearTriangleBudget:10);
        var source=new Tree3DSource(assets,settings:settings);Assert.Null(source.Select(path,4).MeshPath);
        const string image=@"textures\terrain\lodgen\plain.dds";TextureFixture.Put(fixture.Data,image,TextureFixture.Solid(12,200,18));
        TextureFixture.Put(fixture.Data,@"textures\terrain\lodgen\plain.txt",System.Text.Encoding.UTF8.GetBytes("Width=100\nHeight=200\nShiftZ=0\n"));
        // A fresh asset index sees newly created fixture assets.
        var cards=new TreeCardSource(AssetIndex.Build(GameRelease.SkyrimSE,fixture.Data));Assert.NotNull(cards.Build(TreeCardSource.Prefix+image));
    }
}
