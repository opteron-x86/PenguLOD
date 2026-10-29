using System.Numerics;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda;

namespace AnvilLOD.Core.Tests;

public sealed class FoliageReductionTests
{
    private static LodMaterial Material(string name = "leaves", bool doubleSided = true) =>
        new([@"textures\" + name + ".dds"], 0, doubleSided ? 16u : 0, 0, true, 4844, 128, Vector3.Zero, 1);

    private static LodMeshPart Cards(int count, Func<int, Vector3, Vector3>? transform = null,
        Func<int, Vector2, Vector2>? uvTransform = null, bool reverseLast = false, LodMaterial? material = null)
    {
        var card = TextureFixture.Card("leaf", "unused");
        var positions = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<ushort>();
        for (int i = 0; i < count; i++)
        {
            positions.AddRange(card.Positions.Select(p => transform?.Invoke(i, p) ?? p));
            uv.AddRange(card.UVs.Select(p => uvTransform?.Invoke(i, p) ?? p));
            var triangles = card.Triangles.ToArray();
            if (reverseLast && i == count - 1)
                for (int t = 0; t < triangles.Length; t += 3) (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
            indices.AddRange(triangles.Select(v => checked((ushort)(v + i * 4))));
        }
        return new() { Name = "leaves", Material = material ?? Material(), Positions = positions.ToArray(), UVs = uv.ToArray(),
            Normals = Enumerable.Repeat(-Vector3.UnitY, positions.Count).ToArray(),
            Tangents = Enumerable.Repeat(Vector3.UnitX, positions.Count).ToArray(),
            Bitangents = Enumerable.Repeat(Vector3.UnitZ, positions.Count).ToArray(),
            Colors = Enumerable.Repeat(0xffddeeffu, positions.Count).ToArray(), Triangles = indices.ToArray() };
    }
    private static LodMesh Mesh(params LodMeshPart[] parts) => new() { Path = "foliage", Parts = parts };
    private static float Hole(Vector2 uv) => uv.X is > .4f and < .6f && uv.Y is > .4f and < .6f ? 0 : 1;
    private static float[] AlphaImage(LodMesh mesh)
    {
        var pixels = new byte[64 * 64 * 4];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        { int i = (y * 64 + x) * 4; pixels[i] = 30; pixels[i + 1] = 200; pixels[i + 2] = 50; pixels[i + 3] = (byte)(255 * Hole(new((x + .5f) / 64, (y + .5f) / 64))); }
        var rendered = BillboardRenderer.Render(mesh, _ => (pixels, 64, 64), maxPixels: 128, shaded: false)!;
        return Enumerable.Range(0, rendered.Rgba.Length / 4).Select(i => rendered.Rgba[i * 4 + 3] / 255f).ToArray();
    }

    [Fact]
    public void RedundantCardsReduceWithoutChangingRenderedCutoutUvsOrBounds()
    {
        var part = Cards(12); var mesh = Mesh(part);
        var reduced = FoliageReducer.Reduce(mesh, 2, 0, _ => Hole);
        Assert.Equal(22, reduced.RemovedTriangles); Assert.Equal(0, reduced.CoverageLoss);
        var output = Assert.Single(reduced.Mesh.Parts);
        Assert.Equal(2, output.TriangleCount); Assert.Equal(part.Material, output.Material);
        Assert.Equal(part.Positions.Aggregate(Vector3.Min), output.Positions.Aggregate(Vector3.Min));
        Assert.Equal(part.Positions.Aggregate(Vector3.Max), output.Positions.Aggregate(Vector3.Max));
        Assert.All(output.UVs, value => Assert.Contains(value, part.UVs));
        Assert.All(output.Colors!, value => Assert.Contains(value, part.Colors!));
        Assert.Equal(AlphaImage(mesh), AlphaImage(reduced.Mesh));
        Assert.Equal(24, part.TriangleCount);
        var repeat = FoliageReducer.Reduce(mesh, 2, 0, _ => Hole);
        Assert.Equal(output.Positions, repeat.Mesh.Parts[0].Positions);
        Assert.Equal(output.Triangles, repeat.Mesh.Parts[0].Triangles);
    }

    [Fact]
    public void InteriorCoverageLossIsMeasuredAndRespectsTheConfiguredLimit()
    {
        var mesh = Mesh(Cards(2, (i, p) => i == 0 ? p : p * .2f + new Vector3(160, 0, 160),
            (i, uv) => i == 0 ? uv : Vector2.Zero));
        Assert.Equal(0, FoliageReducer.Reduce(mesh, 2, .01f, _ => Hole).RemovedTriangles);
        var reduced = FoliageReducer.Reduce(mesh, 2, .1f, _ => Hole);
        Assert.Equal(2, reduced.RemovedTriangles); Assert.InRange(reduced.CoverageLoss, .01f, .1f);
        var before = AlphaImage(mesh); var after = AlphaImage(reduced.Mesh);
        float loss = before.Zip(after).Count(p => p.First > .5f && p.Second < .5f) / (float)before.Count(p => p > .5f);
        Assert.InRange(loss, .01f, .1f);
    }

    [Fact]
    public void ComplementaryTransparentCardsCannotBeReplacedByOneOpaqueRectangle()
    {
        var mesh = Mesh(Cards(3, uvTransform: (i, uv) => i == 1 ? new(1 - uv.X, uv.Y) : uv));
        var result = FoliageReducer.Reduce(mesh, 2, .2f, _ => uv => uv.X < .5f ? 1 : 0);
        Assert.Equal(2, result.RemovedTriangles); Assert.Equal(4, result.Mesh.Parts.Sum(p => p.TriangleCount));
        Assert.Equal(0, result.CoverageLoss);
    }

    [Fact]
    public void SeparateMaterialsAndOppositeSingleSidedCardsRetainTheirCoverage()
    {
        var materials = Mesh(Cards(2), Cards(1, material: Material("other")));
        var reduced = FoliageReducer.Reduce(materials, 2, 0, _ => _ => 1);
        Assert.Equal(4, reduced.Mesh.Parts.Sum(p => p.TriangleCount));
        Assert.Equal(2, reduced.Mesh.Parts.Count);
        var sides = Mesh(Cards(2, reverseLast: true, material: Material(doubleSided: false)));
        Assert.Equal(0, FoliageReducer.Reduce(sides, 2, .2f, _ => _ => 1).RemovedTriangles);
    }

    [Fact]
    public void IsolatedCardsAndUnknownAlphaRemainIntact()
    {
        var mesh = Mesh(Cards(5, (i, p) => p + new Vector3(i * 800, 0, 0)));
        Assert.Equal(0, FoliageReducer.Reduce(mesh, 2, .2f, _ => _ => 1).RemovedTriangles);
        Assert.Equal(0, FoliageReducer.Reduce(mesh, 2, .2f, _ => null).RemovedTriangles);
    }

    [Fact]
    public void CancellationDoesNotMutateSourceGeometry()
    {
        var mesh = Mesh(Cards(8)); using var cancel = new CancellationTokenSource();
        using var limits = RunLimits.Enter(cancellation: cancel.Token);
        int samples = 0;
        Assert.Throws<OperationCanceledException>(() => FoliageReducer.Reduce(mesh, 2, .1f, _ => uv =>
        { if (++samples == 100) cancel.Cancel(); return 1; }));
        Assert.Equal(16, mesh.Parts[0].TriangleCount);
    }

    [Fact]
    public void NativeTreeUsesReducedFoliageAtTheFarLevelAndReportsItsCost()
    {
        using var f = new TextureFixture(); const string model = @"meshes\trees\foliage.nif";
        TextureFixture.Put(f.Data, @"textures\leaves.dds", TextureFixture.Solid(30, 200, 50));
        TextureFixture.Put(f.Data, model, LodNifWriter.Write("leaves", [Cards(10)]));
        var settings = new Tree3DSettings(true, Lod8: true, NearTriangleBudget: 100, FarTriangleBudget: 4);
        var source = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: settings);
        var near = source.Select(model, 4, true); var far = source.Select(model, 8, true);
        Assert.NotNull(near.MeshPath); Assert.NotNull(far.MeshPath);
        Assert.Equal(20, near.Triangles); Assert.InRange(far.Triangles, 2, 4);
        Assert.Equal(near.Triangles - far.Triangles, far.FoliageTrianglesRemoved);
        Assert.Equal(0, far.FoliageCoverageLoss);
        var disabled = new Tree3DSource(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings: settings with { ReduceFoliage = false });
        Assert.Null(disabled.Select(model, 8, true).MeshPath);
    }
}
