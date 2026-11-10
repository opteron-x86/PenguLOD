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

public class ExpandedObjectAtlasTests
{
    private static LodMeshPart Change(LodMeshPart p, LodMaterial? material = null, Vector2[]? uvs = null) => new()
    {
        Name = p.Name, Material = material ?? p.Material, UVs = uvs ?? p.UVs, Positions = p.Positions,
        Normals = p.Normals, Tangents = p.Tangents, Bitangents = p.Bitangents, Triangles = p.Triangles, Colors = p.Colors
    };

    [Fact]
    public void AuxiliaryChannelsBatchTogetherWithoutLosingShaderOrNormalGloss()
    {
        using var f = new TextureFixture();
        TextureFixture.Put(f.Data, @"textures\red_n.dds", TextureFixture.Solid(128, 128, 255, 62));
        TextureFixture.Put(f.Data, @"textures\green_n.dds", TextureFixture.Solid(128, 128, 255, 62));
        using var game = GameContext.Open(f.Game); var assets = game.BuildAssets(_ => true);
        var parts = new[] { "red", "green" }.Select(name =>
        {
            var p = TextureFixture.Card(name, $@"textures\{name}.dds");
            var slots = p.Material.Textures.ToArray(); slots[1] = $@"textures\{name}_n.dds";
            slots[2] = slots[3] = slots[5] = slots[7] = slots[8] = slots[0]; slots[4] = @"textures\cubemap.dds";
            return Change(p, p.Material with { Textures = slots, EmissiveColor = new(1, .5f, .2f), EmissiveMultiple = 2 });
        }).ToArray();
        var mesh = new LodMesh { Path = "fixture", Parts = parts };
        var atlas = ObjectAtlasStage.Build([mesh], assets, f.Output, 64); var mapped = atlas.Transform(mesh);
        Assert.Equal(mapped.Parts[0].Material, mapped.Parts[1].Material);
        Assert.Equal(parts[0].Material.EmissiveColor, mapped.Parts[0].Material.EmissiveColor);
        Assert.Equal(parts[0].Material.EmissiveMultiple, mapped.Parts[0].Material.EmissiveMultiple);
        foreach (var p in mapped.Parts)
        {
            Assert.Equal(@"textures\cubemap.dds", p.Material.Textures[4]);
            foreach (int slot in new[] { 0, 1, 2, 3, 5, 7, 8 }) Assert.True(File.Exists(AnvilLOD.Core.World.GamePath.ToFileSystemPath(f.Output, p.Material.Textures[slot])));
            var normal = TextureImages.Decode(File.ReadAllBytes(AnvilLOD.Core.World.GamePath.ToFileSystemPath(f.Output, p.Material.Textures[1])));
            int x = (int)(p.UVs[0].X * normal.Width), y = (int)(p.UVs[0].Y * normal.Height);
            Assert.InRange(normal.Pixels[(y * normal.Width + x) * 4 + 3], 58, 66);
        }
        var reference = new AnvilLOD.Core.World.LodReference("1", "2", "Fixture", "T", "fixture.esm", Vector3.Zero, Vector3.Zero, 1,
            new("fixture", null, null, null), 0);
        var quad = new AnvilLOD.Core.World.QuadKey("T", AnvilLOD.Core.World.LodLevel.Lod4, 0, 0);
        var before = BtoBuilder.Build(quad, [reference], _ => mesh); var after = BtoBuilder.Build(quad, [reference], _ => mapped);
        Assert.Equal(2, before.Shapes); Assert.Equal(1, after.Shapes); Assert.Equal(before.Triangles, after.Triangles);
    }

    [Fact]
    public void IntegerShiftedWrappingFitsButTrueTilingAndDisplacementStayUntouched()
    {
        var p = TextureFixture.Card("wall", "red.dds");
        var shifted = Change(p, uvs: p.UVs.Select(v => v + new Vector2(-2, 3)).ToArray());
        Assert.Null(ObjectAtlasStage.Ineligible(shifted));
        var mesh = new LodMesh { Path = "fixture", Parts = [shifted] };
        var mapped = ObjectAtlasStage.Remap(mesh, new Dictionary<string, ObjectAtlasStage.Mapping> { [p.Material.Key] = new("atlas", "atlas_n", new(.1f, .2f, .4f, .6f)) });
        Assert.Equal(new Vector2(.1f, .6f), mapped.Parts[0].UVs[0]); Assert.Same(p.Triangles, mapped.Parts[0].Triangles);
        Assert.NotNull(ObjectAtlasStage.Ineligible(Change(p, uvs: p.UVs.Select(v => v * 2).ToArray())));
        Assert.NotNull(ObjectAtlasStage.Ineligible(Change(shifted, p.Material with { ClampMode = 0 })));
        Assert.NotNull(ObjectAtlasStage.Ineligible(Change(p, p.Material with { ShaderFlags1 = 1u << 11 })));
    }

    [Fact]
    public void NonstandardCutoutThresholdsAndSnowMaterialClassificationArePreserved()
    {
        using var f = new TextureFixture(); using var game = GameContext.Open(f.Game); var assets = game.BuildAssets(_ => true);
        var p = TextureFixture.Card("lod", @"textures\lod\object.dds");
        var parts = new[] { 10, 85, 127, 200 }.Select(t => Change(p, p.Material with { HasAlpha = true, AlphaFlags = 4844, AlphaThreshold = (byte)t })).ToArray();
        var mesh = new LodMesh { Path = "fixture", Parts = parts }; var atlas = ObjectAtlasStage.Build([mesh], assets, f.Output, 64);
        var mapped = atlas.Transform(mesh); Assert.Equal(4, atlas.Report.PackedMaterials);
        for (int i = 0; i < parts.Length; i++)
        {
            Assert.Equal(parts[i].Material.AlphaFlags, mapped.Parts[i].Material.AlphaFlags);
            Assert.Equal(parts[i].Material.AlphaThreshold, mapped.Parts[i].Material.AlphaThreshold);
            Assert.Equal(BtoBuilder.ShapeNameFor(AnvilLOD.Core.World.LodShaderKind.Snow, parts[i].Material, AnvilLOD.Core.World.LodLevel.Lod4),
                BtoBuilder.ShapeNameFor(AnvilLOD.Core.World.LodShaderKind.Snow, mapped.Parts[i].Material, AnvilLOD.Core.World.LodLevel.Lod4));
        }
        Assert.Equal(4, mapped.Parts.Select(p => p.Material.Key).Distinct().Count());
        Assert.NotNull(ObjectAtlasStage.Ineligible(Change(p, p.Material with { HasAlpha = true, AlphaFlags = 4845 })));
    }

    [Fact]
    public void UnsupportedTextureDoesNotPreventOtherMaterialsFromPackingOrDeleteRetainedPages()
    {
        using var f = new TextureFixture();
        TextureFixture.Put(f.Output, @"textures\pengulod\objects\retained.dds", TextureFixture.Solid(1, 2, 3));
        var bad = TextureFixture.Card("bad", @"textures\missing.dds");
        var mesh = new LodMesh { Path = "fixture", Parts = [bad, TextureFixture.Card("red", @"textures\red.dds"), TextureFixture.Card("green", @"textures\green.dds")] };
        using var game = GameContext.Open(f.Game); var assets = game.BuildAssets(_ => true);
        var atlas = ObjectAtlasStage.Build([mesh], assets, f.Output, 64);
        Assert.Same(bad, atlas.Transform(mesh).Parts[0]); Assert.Equal(2, atlas.Report.PackedMaterials);
        Assert.Single(atlas.Report.Warnings!); Assert.True(File.Exists(AnvilLOD.Core.World.GamePath.ToFileSystemPath(f.Output, @"textures\pengulod\objects\retained.dds")));
    }
}
