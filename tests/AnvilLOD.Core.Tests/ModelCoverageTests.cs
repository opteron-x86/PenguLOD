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

    [Fact]
    public void RuntimeCopiesStillRecognizeOriginalSkinRequirements()
    {
        var skin = ModelNifFixture.Create(ModelNifFixture.Geometry.Partition);
        Assert.False(NifGeometryReader.Read("skin", skin).HasIncompleteGeometry);
        Assert.True(NifGeometryReader.HasSkinInstances(skin));
        var staticBytes = LodNifWriter.Write("baked", NifGeometryReader.Read("skin", skin).Parts);
        Assert.False(NifGeometryReader.HasSkinInstances(staticBytes));
    }

    [Theory]
    [InlineData("bhkCollisionObject", false)]
    [InlineData("bhkRigidBody", false)]
    [InlineData("NiAlphaProperty", false)]
    [InlineData("NiParticles", true)]
    public void NonvisualChildrenAreIgnoredButUnsupportedVisibleGeometryIsReported(string type, bool incomplete)
    {
        var bytes = ModelNifFixture.WithExtraChild(ModelNifFixture.Create(ModelNifFixture.Geometry.Packed), type);
        var mesh = NifGeometryReader.Read("child", bytes);
        Assert.Equal(incomplete, mesh.HasIncompleteGeometry);
        Assert.Single(mesh.Parts);
        if (incomplete) Assert.Throws<NotSupportedException>(() => BillboardRenderer.Render(mesh, _ => null));
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

    [Theory]
    [InlineData(0x1200, 0, 10, true)]
    [InlineData(0x1200, 128, 128, false)]
    [InlineData(0x0e00, 128, 128, true)]
    [InlineData(0x1e00, 0, 255, false)]
    [InlineData(0, 128, 0, true)]
    public void StaticEffectsRespectAlphaTestFunctionAndZeroThreshold(ushort flags, byte threshold, byte alpha, bool visible)
    {
        var bytes = ModelNifFixture.Create(ModelNifFixture.Geometry.Packed, effect: true, alphaFlags: flags, alphaThreshold: threshold);
        var mesh = NifGeometryReader.Read("effect", bytes, renderEffects: true);
        Assert.Empty(mesh.Warnings);
        var image = BillboardRenderer.Render(mesh, _ => (new byte[] { 10, 100, 10, alpha }, 1, 1), 32);
        Assert.Equal(visible, image is not null);
    }

    [Fact]
    public void BlendedEffectMaterialCannotBeRenderedAsOpaque()
    {
        var bytes = ModelNifFixture.Create(ModelNifFixture.Geometry.Packed, effect: true, alphaFlags: 0xed);
        var mesh = NifGeometryReader.Read("blend", bytes, renderEffects: true);
        Assert.True(mesh.HasIncompleteGeometry);
        Assert.Contains(mesh.Warnings, w => w.Contains("depth-sorted compositing"));
    }

    [Fact]
    public void OpaqueDiffuseAlphaIsNotTreatedAsTransparency()
    {
        var mesh = NifGeometryReader.Read("opaque", ModelNifFixture.Create(ModelNifFixture.Geometry.Packed));
        Assert.NotNull(BillboardRenderer.Render(mesh, _ => (new byte[] { 10, 100, 10, 0 }, 1, 1), 32));
        Assert.Throws<FileNotFoundException>(() => BillboardRenderer.Render(mesh, _ => null));
    }

    [Fact]
    public void GeneratedFullTreeMaterialsDiscardSpecialShaderChannels()
    {
        using var f = new TextureFixture();
        var bytes = ModelNifFixture.Create(ModelNifFixture.Geometry.Packed);
        var nif = NifFile.Read(bytes); var writer = new NifWriter();
        foreach (string s in nif.Strings) writer.String(s);
        foreach (var block in nif.Blocks) writer.Reserve(block.Type);
        foreach (var block in nif.Blocks)
        {
            var payload = bytes.AsSpan(block.Offset, block.Size).ToArray();
            if (block.Type == "BSLightingShaderProperty")
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), 0x80000881u);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), 0x00800040u);
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(44), 1f);
            }
            if (block.Type == "BSShaderTextureSet") writer.Set(block.Index, b =>
            {
                b.Write(6u);
                foreach (string texture in new[] { @"textures\green.dds", @"textures\green_n.dds", "glow", "height", "environment", "rmaos" })
                    NifWriter.WriteSizedString(b, texture);
            });
            else writer.Set(block.Index, b => b.Write(payload));
        }
        const string path = @"meshes\trees\testtree.nif";
        TextureFixture.Put(f.Data, path, writer.ToBytes(0));
        var source = new Tree3DSource(AssetIndex.Build(Mutagen.Bethesda.GameRelease.SkyrimSE, f.Data), settings: new(true));
        var selection = source.Select(path, 4, native: true);
        Assert.NotNull(selection.MeshPath);
        var material = Assert.Single(source.Build(selection.MeshPath)!.Parts).Material;
        Assert.Equal(Vector3.Zero, material.EmissiveColor);
        Assert.Equal(0u, material.ShaderFlags1 & 0x881u);
        Assert.Equal(0u, material.ShaderFlags2 & 0x00800040u);
        Assert.All(material.Textures.Skip(2), texture => Assert.Equal("", texture));
        var authored = NifGeometryReader.Read(path, writer.ToBytes(0), passthru: true);
        Assert.Equal(0x00800040u, authored.Parts[0].Material.ShaderFlags2 & 0x00800040u);
    }
}
