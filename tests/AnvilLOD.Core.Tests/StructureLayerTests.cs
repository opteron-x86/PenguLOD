using System.Buffers.Binary;
using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;

namespace AnvilLOD.Core.Tests;

public class StructureLayerTests
{
    // Keep a real structural shape beside a separate layer, as exported architecture models do.
    private static byte[] Model(string layer)
    {
        bool legacy = layer is "empty" or "bad-data";
        byte[] bytes = ModelNifFixture.Create(legacy ? ModelNifFixture.Geometry.Triangles : ModelNifFixture.Geometry.Packed);
        var nif = NifFile.Read(bytes); var w = new NifWriter();
        foreach (string s in nif.Strings) w.String(s);
        foreach (var block in nif.Blocks) w.Reserve(block.Type);
        foreach (var block in nif.Blocks) w.Set(block.Index, b => b.Write(bytes, block.Offset, block.Size));
        int child = w.Reserve(nif.Blocks[3].Type);
        var shape = bytes.AsSpan(nif.Blocks[3].Offset, nif.Blocks[3].Size).ToArray();
        if (legacy) BinaryPrimitives.WriteInt32LittleEndian(shape.AsSpan(72), layer == "empty" ? -1 : 0);
        else if (layer is "shaderless" or "bad-shader")
            BinaryPrimitives.WriteInt32LittleEndian(shape.AsSpan(92), layer == "shaderless" ? -1 : 1000);
        else
        {
            int shader = w.Reserve("BSEffectShaderProperty"), alpha = w.Reserve("NiAlphaProperty");
            int controller = layer == "animated" ? w.Reserve("BSEffectShaderPropertyFloatController") : -1;
            if (controller >= 0) w.Set(controller, b => b.Write(0));
            w.Set(shader, b =>
            {
                b.Write(-1); b.Write(0u); b.Write(controller);
                b.Write(0x80000000u); b.Write(0u);
                b.Write(0f); b.Write(0f); b.Write(1f); b.Write(1f);
                NifWriter.WriteSizedString(b, @"textures\overlay.dds");
                b.Write(3u); for (int i = 0; i < 10; i++) b.Write(1f);
                NifWriter.WriteSizedString(b, "");
            });
            w.Set(alpha, b => { NifWriter.WriteObjectNet(b, -1); b.Write((ushort)(layer == "blended" ? 0x100d : 0x12ec)); b.Write((byte)128); });
            BinaryPrimitives.WriteInt32LittleEndian(shape.AsSpan(92), shader);
            BinaryPrimitives.WriteInt32LittleEndian(shape.AsSpan(96), alpha);
        }
        w.Set(child, b => b.Write(shape));
        w.Set(0, b => { NifWriter.WriteObjectNet(b, -1); NifWriter.WriteAvObject(b, 14, Vector3.Zero, 1); b.Write(2u); b.Write(3); b.Write(child); b.Write(0u); });
        return w.ToBytes(0);
    }

    [Theory]
    [InlineData("blended")]
    [InlineData("animated")]
    [InlineData("shaderless")]
    public void StaticStructuresSurviveNonstructuralLayersWithExplicitDiagnostics(string layer)
    {
        var bytes = Model(layer);
        Assert.True(NifGeometryReader.Read("structure", bytes).HasIncompleteGeometry);
        Assert.True(NifGeometryReader.Read("render", bytes, renderEffects: true).HasIncompleteGeometry);
        var mesh = NifGeometryReader.Read("structure", bytes, staticObjectLod: true);
        Assert.False(mesh.HasIncompleteGeometry);
        var part = Assert.Single(mesh.Parts);
        Assert.Equal(2, part.TriangleCount);
        Assert.StartsWith(NifGeometryReader.LodOmissionWarning, Assert.Single(mesh.Warnings));
        var back = NifGeometryReader.Read("written", LodNifWriter.Write("structure", mesh.Parts));
        Assert.Empty(back.Warnings); Assert.Equal(part.Positions, Assert.Single(back.Parts).Positions);
        var generator = new LodGenerator(new Source(bytes));
        Assert.Single(generator.GetMesh(@"meshes\structure.nif")!.Parts);
        var stats = generator.Generate(new Dictionary<QuadKey, IReadOnlyList<LodReference>>(), [], Path.GetTempPath());
        Assert.Empty(stats.MeshErrors); Assert.Single(stats.MeshWarnings!);
    }

    [Fact]
    public void NullLegacyGeometryHasNoSurfaceToLose()
    {
        var mesh = NifGeometryReader.Read("empty child", Model("empty"));
        Assert.Empty(mesh.Warnings); Assert.Single(mesh.Parts);
    }

    [Theory]
    [InlineData("opaque-effect")]
    [InlineData("bad-data")]
    [InlineData("bad-shader")]
    public void UnknownMaterialsAndCorruptReferencesStillRejectTheWholeStructure(string layer)
    {
        byte[] bytes = Model(layer);
        Assert.True(NifGeometryReader.Read("corrupt", bytes, staticObjectLod: true).HasIncompleteGeometry);
        Assert.Null(new LodGenerator(new Source(bytes)).GetMesh(@"meshes\structure.nif"));
    }

    private sealed class Source(byte[] bytes) : IAssetSource
    {
        public bool Exists(string path) => true;
        public string? Fingerprint(string path) => "fixture";
        public bool TryOpen(string path, out Stream stream) { stream = new MemoryStream(bytes); return true; }
    }
}
