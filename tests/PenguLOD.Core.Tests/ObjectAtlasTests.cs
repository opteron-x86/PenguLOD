using System.Numerics;
using PenguLOD.Core.Pipeline;
using PenguLOD.Meshes;
using PenguLOD.Plugins;
using PenguLOD.Testing;
using PenguLOD.Textures;
using PenguLOD.Textures.Dds;
using Xunit;

namespace PenguLOD.Core.Tests;

public class ObjectAtlasTests
{
    [Fact]
    public void Materials_drawn_in_the_same_blocks_share_an_atlas_page()
    {
        // Eight materials, two per page. a/e are drawn in block 0, b/f in block 1, c/g in block 2, d/h in block 3. In key order the
        // pages would be (a, b), (c, d), ... and every block would draw from two pages; by place each block draws from one.
        string[] keys = ["a", "b", "c", "d", "e", "f", "g", "h"];
        var usage = keys.Select((k, i) => (k, bits: new[] { 1UL << (i % 4) })).ToDictionary(t => t.k, t => t.bits);
        var group = keys.Select(k => KeyValuePair.Create(k, 0)).ToList();
        var pages = ObjectAtlasStage.Batches(group, 2, usage).Select(p => string.Concat(p.Select(kv => kv.Key))).ToList();
        Assert.Equal(["ae", "bf", "cg", "dh"], pages.Order().ToList());
        Assert.Equal(["ab", "cd", "ef", "gh"], ObjectAtlasStage.Batches(group, 2, null).Select(p => string.Concat(p.Select(kv => kv.Key))).ToList());
    }

    [Fact]
    public void PagesAreDeterministicPairedAndKeepSeparateMaterialsAtMipEdges()
    {
        var red=TextureImages.Decode(TextureFixture.Solid(240,8,8));var green=TextureImages.Decode(TextureFixture.Solid(8,240,8));
        var entries=new[]{new ObjectTextureAtlas.Input("red",red,null,false,0),new ObjectTextureAtlas.Input("green",green,null,false,0)};
        var result=ObjectTextureAtlas.Build(entries,64,128);var reverse=ObjectTextureAtlas.Build(entries.Reverse().ToArray(),64,128);
        Assert.Equal(result.Pages[0].Diffuse,reverse.Pages[0].Diffuse);Assert.Single(result.Pages);
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
            Triangles=original.Triangles,UVs=[new(-10,0),new(20,0),new(0,10),new(20,10)]};   // the texture repeated hundreds of times over one triangle
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
        Assert.True(File.Exists(PenguLOD.Core.World.GamePath.ToFileSystemPath(output,mapped.Parts[0].Material.Textures[0])));
    }
}

public class ExpandedObjectAtlasTests
{
    [Fact]
    public void A_page_is_as_large_as_its_cells_need_and_half_as_tall_when_they_fill_half()
    {
        ObjectTextureAtlas.Input[] Inputs(int count) => Enumerable.Range(0, count)
            .Select(i => new ObjectTextureAtlas.Input("m" + i.ToString("D3"), TextureImages.Decode(TextureFixture.Solid((byte)(i * 7), 8, 8)), null, false, 0)).ToArray();
        (int Width, int Height) Top(byte[] dds) { var info = DdsFile.ReadInfo(dds); return (info.Width, info.Height); }

        // 5 cells need a 4 x 4 grid but fill only its first two rows; 9 fill three rows, so the page stays square.
        var five = ObjectTextureAtlas.Build(Inputs(5), 64, 4096); var nine = ObjectTextureAtlas.Build(Inputs(9), 64, 4096);
        Assert.Equal((256, 128), (Assert.Single(five.Pages).Size, five.Pages[0].Height));
        Assert.Equal((256, 128), Top(five.Pages[0].Diffuse)); Assert.Equal((256, 128), Top(five.Pages[0].Normal));
        Assert.Equal((256, 256), (Assert.Single(nine.Pages).Size, nine.Pages[0].Height));
        Assert.All(five.Placements.Values, p => { Assert.InRange(p.Rect.V0, 0f, 1f); Assert.InRange(p.Rect.V1, p.Rect.V0, 1f); Assert.InRange(p.Rect.U1, p.Rect.U0, 1f); });
        // The fifth cell is the first of the second row: the lower half of a page two cells tall.
        Assert.InRange(five.Placements["m004"].Rect.V0, .5f, .6f); Assert.InRange(five.Placements["m004"].Rect.U0, 0f, .1f);
        var pixels = TextureImages.Decode(five.Pages[0].Diffuse);
        int x = (int)((five.Placements["m004"].Rect.U0 + five.Placements["m004"].Rect.U1) / 2 * pixels.Width), y = (int)((five.Placements["m004"].Rect.V0 + five.Placements["m004"].Rect.V1) / 2 * pixels.Height);
        Assert.InRange(pixels.Pixels[(y * pixels.Width + x) * 4], 20, 36);       // red = 4 * 7

        // More materials than one page holds go on to the next.
        var many = ObjectTextureAtlas.Build(Inputs(20), 64, 256);
        Assert.Equal(2, many.Pages.Count); Assert.Equal((256, 256), (many.Pages[0].Size, many.Pages[0].Height)); Assert.Equal((128, 128), (many.Pages[1].Size, many.Pages[1].Height));
        var eightK = ObjectTextureAtlas.Build(Inputs(2), 64, 8192);
        Assert.Single(eightK.Pages);
        Assert.Throws<ArgumentException>(() => ObjectTextureAtlas.Build(Inputs(2), 64, 16384));
    }

    [Fact]
    public void Variable_aspect_skyline_packing_preserves_rectangular_ratios_and_incremental_layout_slots()
    {
        // A 4:1 strip (256x64) and a small 32x32 texture pack far tighter than uniform 256x256 square cells.
        var widePixels = new byte[256 * 64 * 4];
        for (int i = 0; i < widePixels.Length; i += 4) { widePixels[i] = 220; widePixels[i + 1] = 40; widePixels[i + 2] = 20; widePixels[i + 3] = 255; }
        var smallPixels = new byte[32 * 32 * 4];
        for (int i = 0; i < smallPixels.Length; i += 4) { smallPixels[i] = 20; smallPixels[i + 1] = 210; smallPixels[i + 2] = 60; smallPixels[i + 3] = 255; }

        var inputs = new[]
        {
            new ObjectTextureAtlas.Input("strip", new TextureImage(widePixels, 256, 64), null, false, 3),
            new ObjectTextureAtlas.Input("small", new TextureImage(smallPixels, 32, 32), null, false, 3),
        };
        var packed = ObjectTextureAtlas.Build(inputs, cellSize: 256, pageSize: 4096, variableAspect: true);
        Assert.Single(packed.Pages);
        var stripBox = packed.Boxes!["strip"];
        var smallBox = packed.Boxes["small"];
        Assert.True(stripBox.InnerW > stripBox.InnerH * 2, $"Expected rectangular inner box, got {stripBox.InnerW}x{stripBox.InnerH}");
        Assert.True(smallBox.OuterW <= 96 && smallBox.OuterH <= 96, $"Expected compact box for 32x32 texture, got {smallBox.OuterW}x{smallBox.OuterH}");

        // Incremental stage build: adding a third material preserves the first two materials' exact atlas rects and page stem!
        using var f = new TextureFixture();
        TextureFixture.Put(f.Data, @"textures\wide.dds", TextureImages.Encode(new(widePixels, 256, 64)));
        TextureFixture.Put(f.Data, @"textures\small.dds", TextureImages.Encode(new(smallPixels, 32, 32)));
        using var game = GameContext.Open(f.Game);
        var assets = game.BuildAssets(_ => true);
        var m1 = new LodMesh { Path = "m1", Parts = [TextureFixture.Card("a", @"textures\wide.dds"), TextureFixture.Card("b", @"textures\small.dds")] };
        var run1 = ObjectAtlasStage.Build([m1], assets, f.Output, cellSize: 256, maxPageSize: 4096);
        Assert.Equal(2, run1.Report.PackedMaterials);
        Assert.True(File.Exists(Path.Combine(f.Output, ObjectAtlasStage.LayoutFileName)));

        // Re-running unchanged reuses cached page without re-encoding.
        var runSame = ObjectAtlasStage.Build([m1], assets, f.Output, cellSize: 256, maxPageSize: 4096);
        Assert.Equal(1, runSame.Report.ReusedPages);
        static (string D, string N, AtlasRect R) Slot(ObjectAtlasStage.Mapping m) => (m.Diffuse, m.Normal, m.Rect);
        Assert.Equal(Slot(run1.Mappings![m1.Parts[0].Material.Key]), Slot(runSame.Mappings![m1.Parts[0].Material.Key]));

        // Adding a new material on a max-sized page (or pinned page with free Skyline space) preserves existing rects & page stem!
        var m2 = new LodMesh { Path = "m2", Parts = [TextureFixture.Card("c", @"textures\red.dds")] };
        var runAppend = ObjectAtlasStage.Build([m1, m2], assets, f.Output, cellSize: 256, maxPageSize: 4096);
        Assert.Equal(3, runAppend.Report.PackedMaterials);
        Assert.Equal(Slot(run1.Mappings[m1.Parts[0].Material.Key]), Slot(runAppend.Mappings![m1.Parts[0].Material.Key]));
        Assert.Equal(Slot(run1.Mappings[m1.Parts[1].Material.Key]), Slot(runAppend.Mappings[m1.Parts[1].Material.Key]));
    }

    [Fact]
    public void A_surface_mapped_over_several_tiles_in_pieces_is_folded_triangle_by_triangle()
    {
        // Two quads side by side sharing an edge: the first uses tile (0, 0) of the texture, the second tile (1, 0), so the two
        // shared vertices have u = 1. As a whole the part covers two tiles; each triangle covers one.
        var card = TextureFixture.Card("wall", "red.dds");
        var part = new LodMeshPart
        {
            Name = "wall", Material = card.Material,
            Positions = [new(0, 0, 0), new(100, 0, 0), new(200, 0, 0), new(0, 0, 100), new(100, 0, 100), new(200, 0, 100)],
            UVs = [new(0, 1), new(1, 1), new(2, 1), new(0, 0), new(1, 0), new(2, 0)],
            Normals = Enumerable.Repeat(-Vector3.UnitY, 6).ToArray(), Tangents = Enumerable.Repeat(-Vector3.UnitZ, 6).ToArray(),
            Bitangents = Enumerable.Repeat(Vector3.UnitX, 6).ToArray(), Colors = [1u, 2u, 3u, 4u, 5u, 6u],
            Triangles = [0, 1, 3, 1, 4, 3, 1, 2, 4, 2, 5, 4],
        };
        Assert.Null(ObjectAtlasStage.Ineligible(part));
        var rect = new AtlasRect(.25f, .5f, .5f, .75f);
        var mapped = ObjectAtlasStage.Remap(new LodMesh { Path = "fixture", Parts = [part] },
            new Dictionary<string, ObjectAtlasStage.Mapping> { [part.Material.Key] = new("atlas", "atlas_n", rect) }).Parts[0];

        Assert.Equal("atlas", mapped.Material.Textures[0]);
        Assert.Equal(8, mapped.VertexCount);                                        // the two shared vertices exist once per tile
        Assert.Equal(part.TriangleCount, mapped.TriangleCount);
        Assert.All(mapped.UVs, uv => { Assert.InRange(uv.X, .25f, .5f); Assert.InRange(uv.Y, .5f, .75f); });
        for (int i = 0; i < part.Triangles.Length; i++)                              // the geometry and everything on it is unchanged
        {
            Assert.Equal(part.Positions[part.Triangles[i]], mapped.Positions[mapped.Triangles[i]]);
            Assert.Equal(part.Colors![part.Triangles[i]], mapped.Colors![mapped.Triangles[i]]);
        }
        // Each quad spans its whole tile: left edge at the rectangle's left, right edge at its right.
        Assert.Equal(.25f, mapped.UVs[mapped.Triangles[0]].X); Assert.Equal(.5f, mapped.UVs[mapped.Triangles[1]].X);
        Assert.Equal(.25f, mapped.UVs[mapped.Triangles[6]].X); Assert.Equal(.5f, mapped.UVs[mapped.Triangles[7]].X);

        // A triangle that crosses a tile edge, or repeats the texture, is cut at the edge. Rounding at the edge needs no cut.
        Assert.Equal(0, ObjectAtlasStage.CutTriangles(part));
        Assert.True(ObjectAtlasStage.CutTriangles(Change(part, uvs: part.UVs.Select(v => v + new Vector2(.5f, 0)).ToArray())) > 0);
        Assert.Null(ObjectAtlasStage.Ineligible(Change(part, uvs: part.UVs.Select(v => v * 3).ToArray())));
        Assert.Equal(0, ObjectAtlasStage.CutTriangles(Change(part, uvs: part.UVs.Select(v => v * 1.0005f).ToArray())));
        Assert.NotNull(ObjectAtlasStage.Ineligible(Change(part, part.Material with { ClampMode = 0 })));   // clamped: tile (1, 0) is not a copy
    }

    [Fact]
    public void A_part_that_cutting_would_grow_too_much_keeps_its_own_texture()
    {
        // The texture repeated 6 x 6 times over a card: each triangle covers 36 tiles, within the per-triangle limit, but cutting
        // would add far more than the card has.
        var card = TextureFixture.Card("rock", "red.dds");
        var repeated = Change(card, uvs: card.Positions.Select(p => new Vector2(p.X / 400, 1 - p.Z / 400) * 6 + new Vector2(.5f, .5f)).ToArray());
        Assert.True(ObjectAtlasStage.CutTriangles(repeated) > ObjectAtlasStage.MaxCutTriangles(2));
        Assert.Equal("cutting at tile edges adds too many triangles", ObjectAtlasStage.Ineligible(repeated));
        Assert.Equal(64, ObjectAtlasStage.MaxCutTriangles(2));
        Assert.Equal(100, ObjectAtlasStage.MaxCutTriangles(1000));
    }

    [Fact]
    public void A_triangle_that_crosses_a_tile_edge_is_cut_at_the_edge_and_shows_the_same_texture()
    {
        // A 400 x 400 card with the texture repeated 2.5 times across and 1.5 times down, starting part-way into a tile.
        var card = TextureFixture.Card("rock", "red.dds");
        Vector2 Uv(Vector3 p) => new Vector2(p.X / 400, 1 - p.Z / 400) * new Vector2(2.5f, 1.5f) + new Vector2(-.75f, .25f);
        var part = Change(card, uvs: card.Positions.Select(Uv).ToArray()) with
        {
            Colors = card.Positions.Select(p => (uint)MathF.Round(p.X / 400 * 200) | 0xFF000000u).ToArray(),   // red follows x
        };
        Assert.Null(ObjectAtlasStage.Ineligible(part));
        int added = ObjectAtlasStage.CutTriangles(part);
        Assert.InRange(added, 8, 40);

        var rect = new AtlasRect(.25f, .5f, .5f, .75f);
        var mapped = ObjectAtlasStage.Remap(new LodMesh { Path = "fixture", Parts = [part] },
            new Dictionary<string, ObjectAtlasStage.Mapping> { [part.Material.Key] = new("atlas", "atlas_n", rect) }).Parts[0];
        Assert.Equal(part.TriangleCount + added, mapped.TriangleCount);
        Assert.All(mapped.UVs, uv => { Assert.InRange(uv.X, .25f - 1e-5f, .5f + 1e-5f); Assert.InRange(uv.Y, .5f - 1e-5f, .75f + 1e-5f); });

        float area = 0; var edges = new Dictionary<(Vector3, Vector3), int>();
        for (int t = 0; t < mapped.Triangles.Length; t += 3)
        {
            Vector3[] p = [mapped.Positions[mapped.Triangles[t]], mapped.Positions[mapped.Triangles[t + 1]], mapped.Positions[mapped.Triangles[t + 2]]];
            Vector2[] uv = [mapped.UVs[mapped.Triangles[t]], mapped.UVs[mapped.Triangles[t + 1]], mapped.UVs[mapped.Triangles[t + 2]]];
            var normal = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            Assert.True(normal.Y < 0);                                              // still facing the way the card faces
            area += normal.Length() / 2;
            // The middle of each piece shows the texel the uncut card shows at that place.
            var middle = (p[0] + p[1] + p[2]) / 3; var shown = (uv[0] + uv[1] + uv[2]) / 3; var expected = Uv(middle);
            Assert.Equal(expected.X - MathF.Floor(expected.X), (shown.X - rect.U0) / (rect.U1 - rect.U0), 3);
            Assert.Equal(expected.Y - MathF.Floor(expected.Y), (shown.Y - rect.V0) / (rect.V1 - rect.V0), 3);
            for (int i = 0; i < 3; i++)
            {
                var (a, b) = (p[i], p[(i + 1) % 3]);
                var key = a.X < b.X || (a.X == b.X && a.Z < b.Z) ? (a, b) : (b, a);
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        }
        Assert.Equal(160000f, area, 0);
        // No gaps: every edge inside the card is shared by exactly two pieces, so the two triangles of the card were cut at the
        // same points along their shared diagonal.
        bool OnBorder(Vector3 a, Vector3 b) => (a.X == b.X && a.X is 0 or 400) || (a.Z == b.Z && a.Z is 0 or 400);
        Assert.All(edges, e => Assert.Equal(OnBorder(e.Key.Item1, e.Key.Item2) ? 1 : 2, e.Value));
        // What the vertices carry is interpolated along the cut edge.
        Assert.All(Enumerable.Range(0, mapped.VertexCount), i =>
        {
            Assert.Equal(-Vector3.UnitY, mapped.Normals[i]); Assert.Equal(Vector3.UnitX, mapped.Bitangents[i]);
            Assert.InRange((int)(mapped.Colors![i] & 0xFF) - mapped.Positions[i].X / 400 * 200, -1f, 1f);
            Assert.Equal(0xFFu, mapped.Colors![i] >> 24);
        });

        // An axis that clamps is never cut, and neither is a triangle the texture repeats over more than 64 times.
        // (Cutting the card at 8 x 8 repeats works, but adds more than a part may gain to be packed.)
        Assert.NotNull(ObjectAtlasStage.Ineligible(Change(part, part.Material with { ClampMode = 0 })));
        Assert.True(ObjectAtlasStage.CutTriangles(Change(card, uvs: card.UVs.Select(v => v * 8).ToArray())) > 0);
        Assert.Equal(0, ObjectAtlasStage.CutTriangles(Change(card, uvs: card.UVs.Select(v => v * 9).ToArray())));
        Assert.Equal("tiling or invalid UVs", ObjectAtlasStage.Ineligible(Change(card, uvs: card.UVs.Select(v => v * 9).ToArray())));
    }

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
            foreach (int slot in new[] { 0, 1, 2, 3, 5, 7, 8 }) Assert.True(File.Exists(PenguLOD.Core.World.GamePath.ToFileSystemPath(f.Output, p.Material.Textures[slot])));
            var normal = TextureImages.Decode(File.ReadAllBytes(PenguLOD.Core.World.GamePath.ToFileSystemPath(f.Output, p.Material.Textures[1])));
            int x = (int)(p.UVs[0].X * normal.Width), y = (int)(p.UVs[0].Y * normal.Height);
            Assert.InRange(normal.Pixels[(y * normal.Width + x) * 4 + 3], 58, 66);
        }
        var reference = new PenguLOD.Core.World.LodReference("1", "2", "Fixture", "T", "fixture.esm", Vector3.Zero, Vector3.Zero, 1,
            new("fixture", null, null, null), 0);
        var quad = new PenguLOD.Core.World.QuadKey("T", PenguLOD.Core.World.LodLevel.Lod4, 0, 0);
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
        Assert.NotNull(ObjectAtlasStage.Ineligible(Change(p, uvs: p.UVs.Select(v => v * 20).ToArray())));   // hundreds of repeats over one triangle
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
            Assert.Equal(BtoBuilder.ShapeNameFor(PenguLOD.Core.World.LodShaderKind.Snow, parts[i].Material, PenguLOD.Core.World.LodLevel.Lod4),
                BtoBuilder.ShapeNameFor(PenguLOD.Core.World.LodShaderKind.Snow, mapped.Parts[i].Material, PenguLOD.Core.World.LodLevel.Lod4));
        }
        Assert.Equal(4, mapped.Parts.Select(p => p.Material.Key).Distinct().Count());
        Assert.NotNull(ObjectAtlasStage.Ineligible(Change(p, p.Material with { HasAlpha = true, AlphaFlags = 4845 })));
    }

    [Fact]
    public void A_cutout_keeps_its_alpha_on_a_shape_without_an_alpha_property_and_a_gloss_alpha_is_dropped()
    {
        // No alpha property, yet the LOD shader alpha-tests the shape: a 0 / 255 mask is its cutout (DLC01IceSnow's lake
        // patches); a gloss channel is not, and stays flattened so it cannot punch holes.
        using var f = new TextureFixture();
        TextureFixture.Put(f.Data, @"textures\cutout.dds", ObjectTextureOptimizationTests.Raw(64, 64, (x, _) => (200, 200, 200, (byte)(x < 32 ? 0 : 255))));
        TextureFixture.Put(f.Data, @"textures\gloss.dds", ObjectTextureOptimizationTests.Raw(64, 64, (x, _) => (90, 90, 90, (byte)(x < 32 ? 90 : 160))));
        using var game = GameContext.Open(f.Game); var assets = game.BuildAssets(_ => true);
        var mesh = new LodMesh { Path = "fixture", Parts = [TextureFixture.Card("cutout", @"textures\cutout.dds"), TextureFixture.Card("gloss", @"textures\gloss.dds")] };
        var atlas = ObjectAtlasStage.Build([mesh], assets, f.Output, 64);
        Assert.Equal(2, atlas.Report.PackedMaterials);
        var mapped = atlas.Transform(mesh);
        byte MinAlpha(LodMeshPart p)
        {
            var page = TextureImages.Decode(File.ReadAllBytes(PenguLOD.Core.World.GamePath.ToFileSystemPath(f.Output, p.Material.Textures[0])));
            float u0 = p.UVs.Min(v => v.X), u1 = p.UVs.Max(v => v.X), v0 = p.UVs.Min(v => v.Y), v1 = p.UVs.Max(v => v.Y);
            byte min = 255;
            for (int y = (int)(v0 * page.Height); y < (int)(v1 * page.Height); y++)
                for (int x = (int)(u0 * page.Width); x < (int)(u1 * page.Width); x++)
                    min = Math.Min(min, page.Pixels[(y * page.Width + x) * 4 + 3]);
            return min;
        }
        Assert.True(MinAlpha(mapped.Parts[0]) < 16);
        Assert.Equal(255, MinAlpha(mapped.Parts[1]));
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
        Assert.Single(atlas.Report.Warnings!); Assert.True(File.Exists(PenguLOD.Core.World.GamePath.ToFileSystemPath(f.Output, @"textures\pengulod\objects\retained.dds")));
    }
}
