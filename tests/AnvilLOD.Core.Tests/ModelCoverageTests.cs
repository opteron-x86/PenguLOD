using System.Numerics;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Testing;

namespace AnvilLOD.Core.Tests;

public class ModelCoverageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StripsKeepWindingAcrossDegenerateJoinsAndResetAtNextStrip(bool le)
    {
        var mesh = NifGeometryReader.Read("strips", ModelNifFixture.Create(ModelNifFixture.Geometry.Strips, le: le));
        Assert.Empty(mesh.Warnings);
        var part = Assert.Single(mesh.Parts);
        Assert.Equal(new ushort[] { 0, 1, 2, 1, 2, 3, 0, 1, 2 }, part.Triangles);
        Assert.Equal(new Vector3(107, 0, 0), part.Positions[0]);
        Assert.Equal(4, part.UVs.Length);
        Assert.All(part.Normals, n => Assert.True(float.IsFinite(n.Length()) && MathF.Abs(n.Length() - 1) < .001));
    }

    [Theory]
    [InlineData(ModelNifFixture.Geometry.Triangles)]
    [InlineData(ModelNifFixture.Geometry.Strips)]
    [InlineData(ModelNifFixture.Geometry.Packed)]
    [InlineData(ModelNifFixture.Geometry.Partition)]
    [InlineData(ModelNifFixture.Geometry.Dynamic)]
    [InlineData(ModelNifFixture.Geometry.DynamicPartition)]
    public void StaticSkinUsesBoneInverseBindsAndSceneTransformsExactlyOnce(ModelNifFixture.Geometry geometry)
    {
        var mesh = NifGeometryReader.Read("skin", ModelNifFixture.Create(geometry, skinned: true), passthru: true);
        Assert.Empty(mesh.Warnings);
        var part = Assert.Single(mesh.Parts);
        float z = geometry is ModelNifFixture.Geometry.Dynamic or ModelNifFixture.Geometry.DynamicPartition ? 17 : 0;
        Assert.Equal(new Vector3(121.5f, 0, z), part.Positions[0]);
        Assert.Equal(new Vector3(521.5f, 0, z + 400), part.Positions[3]);
        Assert.Equal(0u, part.Material.ShaderFlags1 & 2u);
        Assert.Equal(0u, part.Material.ShaderFlags2 & 0x20000000u);
        var roundtrip = NifGeometryReader.Read("static", LodNifWriter.Write("static", mesh.Parts));
        Assert.Empty(roundtrip.Warnings);
        Assert.Equal(part.Positions, Assert.Single(roundtrip.Parts).Positions);
        Assert.NotNull(BillboardRenderer.Render(mesh, _ => (new byte[] { 10, 200, 10, 255 }, 1, 1), 32));
    }

    [Fact]
    public void DynamicPositionsAreReadFromTheSeparateBuffer()
    {
        var mesh = NifGeometryReader.Read("dynamic", ModelNifFixture.Create(ModelNifFixture.Geometry.Dynamic));
        Assert.Empty(mesh.Warnings);
        Assert.Equal(new Vector3(107, 0, 17), Assert.Single(mesh.Parts).Positions[0]);
    }

    [Theory]
    [InlineData(0, 107f)]
    [InlineData(1, 127f)]
    public void SwitchNodesRenderOnlyTheActiveBranch(int active, float x)
    {
        var bytes = ModelNifFixture.WithSwitch(ModelNifFixture.Create(ModelNifFixture.Geometry.Packed), active, brokenInactive: active == 0);
        var mesh = NifGeometryReader.Read("switch", bytes);
        Assert.Empty(mesh.Warnings);
        Assert.Equal(new Vector3(x, 0, 0), Assert.Single(mesh.Parts).Positions[0]);
    }

    [Fact]
    public void InvalidActiveSwitchChildIsDiagnosed()
    {
        var bytes = ModelNifFixture.WithSwitch(ModelNifFixture.Create(ModelNifFixture.Geometry.Packed), 8);
        var mesh = NifGeometryReader.Read("switch", bytes);
        Assert.True(mesh.HasIncompleteGeometry);
        Assert.Contains(mesh.Warnings, w => w.Contains("active child"));
    }

    [Theory]
    [InlineData(ModelNifFixture.Geometry.Triangles)]
    [InlineData(ModelNifFixture.Geometry.Partition)]
    public void ExternalSkeletonOrIncompleteWeightsHaveExplicitDiagnostics(ModelNifFixture.Geometry geometry)
    {
        var missing = NifGeometryReader.Read("external", ModelNifFixture.Create(geometry, skinned: true, missingBone: true));
        Assert.True(missing.HasIncompleteGeometry); Assert.Empty(missing.Parts);
        Assert.Contains(missing.Warnings, w => w.Contains("external skeleton"));
        var bad = NifGeometryReader.Read("weight", ModelNifFixture.Create(geometry, skinned: true, weight: 0));
        Assert.True(bad.HasIncompleteGeometry); Assert.Empty(bad.Parts);
        Assert.Contains(bad.Warnings, w => w.Contains("weights") && w.Contains("expected"));
    }

    [Theory]
    [InlineData(ModelNifFixture.Geometry.Triangles)]
    [InlineData(ModelNifFixture.Geometry.Strips)]
    [InlineData(ModelNifFixture.Geometry.Packed)]
    [InlineData(ModelNifFixture.Geometry.Partition)]
    public void OutOfRangeIndicesCannotProducePartialModels(ModelNifFixture.Geometry geometry)
    {
        var bad = NifGeometryReader.Read("invalid", ModelNifFixture.Create(geometry, badIndex: true));
        Assert.True(bad.HasIncompleteGeometry); Assert.Empty(bad.Parts);
        Assert.Contains(bad.Warnings, w => w.Contains("index out of range"));
    }

    [Fact]
    public void TruncatedShapeDoesNotReadIntoTheFollowingShaderBlock()
    {
        var bad = NifGeometryReader.Read("truncated", ModelNifFixture.Create(ModelNifFixture.Geometry.Packed, truncate: true));
        Assert.True(bad.HasIncompleteGeometry); Assert.Empty(bad.Parts);
        Assert.Throws<InvalidDataException>(() => LodMeshAuthor.Build(bad));
        var nif = NifFile.Read(ModelNifFixture.Create(ModelNifFixture.Geometry.Packed));
        var r = nif.ReaderFor(nif.Blocks[0]); r.Position += nif.Blocks[0].Size;
        Assert.Throws<InvalidDataException>(() => r.U8());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextureGenerationPublishesCompleteStaticSkinAndStrips(bool strips)
    {
        using var f = new TextureFixture();
        var geometry = strips ? ModelNifFixture.Geometry.Strips : ModelNifFixture.Geometry.Partition;
        TextureFixture.Put(f.Data, @"meshes\trees\testtree.nif", ModelNifFixture.Create(geometry));
        var result = TextureGeneration.Run(f.Request with { Stitched = false, Trees = true });
        Assert.True(result.Published, string.Join("; ", result.Items.Select(i => i.Detail)));
        Assert.Contains(result.Items, i => i.Kind == "Tree" && i.Status == "Written");
    }

    [Fact]
    public void UnreadableShapeCannotReplaceAnExistingBillboard()
    {
        using var f = new TextureFixture();
        var valid = TextureGeneration.Run(f.Request with { Stitched = false, Trees = true });
        Assert.True(valid.Published);
        string target = Path.Combine(f.Output, valid.Items.Single(i => i.Kind == "Tree").Output.Replace('\\', Path.DirectorySeparatorChar));
        var before = File.ReadAllBytes(target);
        TextureFixture.Put(f.Data, @"meshes\trees\testtree.nif", ModelNifFixture.Create(ModelNifFixture.Geometry.Packed, truncate: true));
        var result = TextureGeneration.Run(f.Request with { Stitched = false, Trees = true });
        Assert.DoesNotContain(result.Items, i => i.Kind == "Tree" && i.Status == "Written");
        // The item must carry the actual failure, not a success with empty/partial geometry.
        Assert.Contains(result.Items, i => i.Kind == "Tree" && i.Detail.Contains("could not be read"));
        Assert.Equal(before, File.ReadAllBytes(target));
    }

    [Fact]
    public void StaticEffectTextureTintAndUnlitMaterialRenderOnlyWhenRequested()
    {
        var bytes = ModelNifFixture.Create(ModelNifFixture.Geometry.Packed, effect: true);
        Assert.True(NifGeometryReader.Read("effect", bytes).HasIncompleteGeometry);
        var mesh = NifGeometryReader.Read("effect", bytes, passthru: true, renderEffects: true);
        Assert.Empty(mesh.Warnings);
        var image = BillboardRenderer.Render(mesh, _ => (new byte[] { 200, 200, 200, 255 }, 1, 1), 32)!;
        int center = (16 * 32 + 16) * 4;
        Assert.InRange(image.Rgba[center], 99, 101);
        Assert.InRange(image.Rgba[center + 1], 49, 51);
        Assert.InRange(image.Rgba[center + 2], 199, 201);
        Assert.Throws<NotSupportedException>(() => LodNifWriter.Write("effect", mesh.Parts));
    }

    [Fact]
    public void ViewDependentEffectsRemainExplicitFailures()
    {
        var bytes = ModelNifFixture.Create(ModelNifFixture.Geometry.Packed, effect: true, unsupportedEffect: true);
        var mesh = NifGeometryReader.Read("effect", bytes, renderEffects: true);
        Assert.True(mesh.HasIncompleteGeometry);
        Assert.Contains(mesh.Warnings, w => w.Contains("view-dependent"));
        Assert.Throws<NotSupportedException>(() => BillboardRenderer.Render(mesh, _ => null));
    }

    [Fact]
    public void OpaqueDiffuseAlphaIsNotTreatedAsTransparency()
    {
        var mesh = NifGeometryReader.Read("opaque", ModelNifFixture.Create(ModelNifFixture.Geometry.Packed));
        Assert.NotNull(BillboardRenderer.Render(mesh, _ => (new byte[] { 10, 100, 10, 0 }, 1, 1), 32));
        Assert.Throws<FileNotFoundException>(() => BillboardRenderer.Render(mesh, _ => null));
    }
}
