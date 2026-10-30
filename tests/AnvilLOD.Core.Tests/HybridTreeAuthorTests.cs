using System.Numerics;
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

namespace AnvilLOD.Core.Tests;

public class HybridTreeAuthorTests
{
    private const string Model = @"meshes\trees\testtree.nif";

    [Fact]
    public void NativeHybridKeepsCrownCoordinatesAndAlignsBothTrunkViews()
    {
        using var f = new TextureFixture(); Put(f, Model);
        using var scope = RunLimits.Enter(2, cacheDirectory: Path.Combine(f.Root, "cache"));
        var source = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: new(true, HybridTrunks: true));
        var selection = source.Select(Model, 4, native: true);
        Assert.Equal("Native hybrid", selection.Method); Assert.Equal(14, selection.OriginalTriangles); Assert.Equal(6, selection.Triangles);
        var mesh = source.Build(selection.MeshPath!)!;
        Assert.Equal(3, mesh.Parts.Count);
        Assert.Equal(Parts().Last().Positions, mesh.Parts.Single(p => p.Name == "crown").Positions);
        var front = mesh.Parts.Single(p => p.Name == "Native trunk front"); var side = mesh.Parts.Single(p => p.Name == "Native trunk side");
        Assert.Equal(100, front.Positions.Max(p => p.X) - front.Positions.Min(p => p.X));
        Assert.Equal(160, side.Positions.Max(p => p.Y) - side.Positions.Min(p => p.Y));
        Assert.All(front.Positions.Concat(side.Positions), p => Assert.InRange(p.Z, 10, 510));
        Assert.Equal(-Vector3.UnitY, front.Normals[0]); Assert.Equal(Vector3.UnitX, side.Normals[0]);
        string output = Path.Combine(f.Root, "lod"); Assert.Equal(4, source.PublishHybridTextures(output));
        foreach (var part in new[] { front, side })
        {
            var image = TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(output, part.Material.Textures[0])));
            Assert.Contains(Enumerable.Range(0, image.Pixels.Length / 4), i => image.Pixels[i * 4 + 3] > 200 && image.Pixels[i * 4] > image.Pixels[i * 4 + 1]);
            Assert.Contains(Enumerable.Range(0, image.Pixels.Length / 4), i => image.Pixels[i * 4 + 3] == 0);
            Assert.True(File.Exists(GamePath.ToFileSystemPath(output, part.Material.Textures[1])));
        }
    }

    [Fact]
    public void MaterialsAndSourceContentInvalidateTrunksAndCorruptCachesAreRepaired()
    {
        using var f = new TextureFixture(); Put(f, Model);
        string cache = Path.Combine(f.Root, "cache"); using var scope = RunLimits.Enter(2, cacheDirectory: cache);
        (Tree3DSource Source, Tree3DSource.Selection Selected) Build(TreeMaterialVariant? variant = null)
        {
            var s = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: new(true, HybridTrunks: true));
            return (s, s.Select(Model, 4, true, variant));
        }
        string Texture((Tree3DSource Source, Tree3DSource.Selection Selected) v) => v.Source.Build(v.Selected.MeshPath!)!.Parts.Single(p => p.Name == "Native trunk front").Material.Textures[0];
        var first = Build(); string path = Texture(first); string fingerprint = first.Source.Fingerprint(first.Selected.MeshPath!)!;
        string physical = GamePath.ToFileSystemPath(Path.Combine(cache, "HybridTrees"), path); File.WriteAllBytes(physical, [0]);
        Assert.Equal(path, Texture(Build())); Assert.True(new FileInfo(physical).Length > 148);
        var variant = TreeMaterialVariant.Create(Model, [new("bark front", 0, [@"textures\green.dds"])]);
        Assert.NotEqual(path, Texture(Build(variant)));
        TextureFixture.Put(f.Data, @"textures\red.dds", TextureFixture.Solid(80, 30, 10));
        var changed = Build(); Assert.NotEqual(path, Texture(changed)); Assert.NotEqual(fingerprint, changed.Source.Fingerprint(changed.Selected.MeshPath!));
    }

    [Fact]
    public void HybridBudgetsIncludeCardsAndUnsupportedSeparationKeepsGeometryFallback()
    {
        using var f = new TextureFixture(); Put(f, Model); using var scope = RunLimits.Enter(2, cacheDirectory: Path.Combine(f.Root, "cache"));
        var tight = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: new(true, NearTriangleBudget: 4, HybridTrunks: true));
        Assert.Null(tight.Select(Model, 4, true).MeshPath); Assert.Equal(0, tight.PublishHybridTextures(Path.Combine(f.Root, "unused")));
        var levels = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: new(true, Lod8: true, FarTriangleBudget: 4, HybridTrunks: true));
        Assert.NotNull(levels.Select(Model, 4, true).MeshPath); Assert.Null(levels.Select(Model, 8, true).MeshPath);
        var installed = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: new(true, HybridTrunks: true));
        Assert.NotEqual("Native hybrid", installed.Select(Model, 4, native: false).Method);
        Assert.Equal(0, installed.PublishHybridTextures(Path.Combine(f.Root, "unused")));
        TextureFixture.Put(f.Data, Model, LodNifWriter.Write("tree", [Parts().Last()], preserveShapeNames: true));
        var crownOnly = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: new(true, HybridTrunks: true));
        var fallback = crownOnly.Select(Model, 4, true);
        Assert.NotNull(fallback.MeshPath); Assert.Equal("Native source reduction", fallback.Method); Assert.Contains("separate opaque trunk", fallback.Reason);
        Assert.Equal(0, crownOnly.PublishHybridTextures(Path.Combine(f.Root, "unused")));
    }

    [Fact]
    public void CombinedSeasonalJobPublishesEveryTextureNamedByGeneratedHybridCards()
    {
        using var f = new TerrainFixture(seasons: true); Put(f.Textures, Model); Put(f.Textures, @"meshes\trees\wintertree.nif");
        string output = Path.Combine(f.Root, "lod"), cache = Path.Combine(f.Root, "cache");
        using var limits = RunLimits.Enter(2, cacheDirectory: cache);
        var request = new GenerationPipelineRequest(Textures: f.Textures.Request with { Stitched = false, Trees = true, TreeTrunks = false },
            Lod: new(f.Game, new(Tree3D: new(true, HybridTrunks: true), DynamicLod: false, GridObjects: false), output,
                Generate: true, Seasons: true, GrassLod: false, ChildWorlds: false, RemoveBuried: false, RemoveUnderwater: false, DetailedShips: false, SkseDll: SkseDllChoice.None, CleanOutput: false));
        var result = GenerationPipeline.Run(request, cache); Assert.True(result.Success, result.Error);
        var blocks = Directory.GetFiles(output, "*.bto", SearchOption.AllDirectories); Assert.Equal(2, blocks.Length);
        var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string block in blocks)
        {
            var mesh = NifGeometryReader.Read(block, File.ReadAllBytes(block), passthru: true);
            var cards = mesh.Parts.Where(p => p.Material.Textures[0].Contains(@"pengulod\hybrid\", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.Equal(2, cards.Length);
            foreach (string path in cards.SelectMany(p => p.Material.Textures.Take(2)))
            { Assert.True(File.Exists(GamePath.ToFileSystemPath(output, path))); textures.Add(path); }
        }
        Assert.Equal(8, textures.Count);
        var second = GenerationPipeline.Run(request, cache); Assert.True(second.Success, second.Error); Assert.All(second.Journal.Stages, s => Assert.True(s.Reused));
    }

    private static void Put(TextureFixture f, string path) => TextureFixture.Put(f.Data, path, LodNifWriter.Write("tree", Parts(), preserveShapeNames: true));
    private static LodMeshPart[] Parts()
    {
        Vector3 a = new(10, -20, 10), b = new(50, 80, 510);
        var front = Face("bark front", [new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z)], -Vector3.UnitY);
        var back = Face("bark back", [new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z)], Vector3.UnitY);
        var right = Face("bark right", [new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z)], Vector3.UnitX);
        var left = Face("bark left", [new(a.X, b.Y, a.Z), new(a.X, a.Y, a.Z), new(a.X, b.Y, b.Z), new(a.X, a.Y, b.Z)], -Vector3.UnitX);
        var top = Face("bark top", [new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(a.X, b.Y, b.Z), new(b.X, b.Y, b.Z)], Vector3.UnitZ);
        var bottom = Face("bark bottom", [new(a.X, b.Y, a.Z), new(b.X, b.Y, a.Z), new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z)], -Vector3.UnitZ);
        var crown = Face("crown", [new(-100, 0, 400), new(150, 0, 400), new(-100, 0, 800), new(150, 0, 800)], -Vector3.UnitY, true);
        return [front, back, right, left, top, bottom, crown];
    }
    private static LodMeshPart Face(string name, Vector3[] positions, Vector3 normal, bool leaves = false)
    {
        var card = TextureFixture.Card(name, leaves ? @"textures\green.dds" : @"textures\red.dds");
        var tangent = Vector3.Normalize(positions[1] - positions[0]);
        return new() { Name = name, Positions = positions, UVs = card.UVs, Triangles = card.Triangles,
            Normals = Enumerable.Repeat(normal, 4).ToArray(), Tangents = Enumerable.Repeat(tangent, 4).ToArray(),
            Bitangents = Enumerable.Repeat(Vector3.Normalize(positions[2] - positions[0]), 4).ToArray(),
            Material = card.Material with { HasAlpha = leaves, AlphaFlags = GrassPatchBuilder.AlphaTestFlags, AlphaThreshold = 128 } };
    }
}
