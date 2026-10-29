using System.Buffers.Binary;
using System.Numerics;
using AnvilLOD.Meshes.Nif;

namespace AnvilLOD.Core.Tests;

/// <summary>Synthetic NIFs with independent serialized layouts; no game or mod assets.</summary>
public static class ModelNifFixture
{
    public enum Geometry { Triangles, Strips, Packed, Partition, Dynamic, DynamicPartition }
    internal static readonly Vector3[] Positions = [new(0, 0, 0), new(400, 0, 0), new(0, 0, 400), new(400, 0, 400)];

    internal static byte[] Create(Geometry geometry, bool skinned = false, bool missingBone = false,
        float weight = .25f, bool badIndex = false, bool truncate = false, bool le = false, bool effect = false,
        bool unsupportedEffect = false, bool modelNormals = false, ushort? alphaFlags = null, byte alphaThreshold = 128,
        bool rotatedBones = false)
    {
        bool legacy = geometry is Geometry.Triangles or Geometry.Strips;
        bool partitioned = geometry is Geometry.Partition or Geometry.DynamicPartition;
        bool dynamic = geometry is Geometry.Dynamic or Geometry.DynamicPartition;
        skinned |= partitioned;
        var w = new NifWriter();
        int root = w.Reserve("NiNode"), bone0 = w.Reserve("NiNode"), bone1 = w.Reserve("NiNode");
        int shape = w.Reserve(legacy ? geometry == Geometry.Strips ? "NiTriStrips" : "NiTriShape" : dynamic ? "BSDynamicTriShape" : "BSTriShape");
        int data = legacy ? w.Reserve(geometry == Geometry.Strips ? "NiTriStripsData" : "NiTriShapeData") : -1;
        int skin = skinned ? w.Reserve("BSDismemberSkinInstance") : -1;
        int skinData = skinned ? w.Reserve("NiSkinData") : -1;
        int partition = partitioned ? w.Reserve("NiSkinPartition") : -1;
        int shader = w.Reserve(effect ? "BSEffectShaderProperty" : "BSLightingShaderProperty");
        int textures = w.Reserve("BSShaderTextureSet");
        int alpha = alphaFlags.HasValue ? w.Reserve("NiAlphaProperty") : -1;
        int name = w.String("test shape");
        void Node(int index, float x, params int[] children) => w.Set(index, b =>
        {
            NifWriter.WriteObjectNet(b, -1); NifWriter.WriteAvObject(b, 14, new(x, 0, 0), 1);
            b.Write((uint)children.Length); foreach (int child in children) b.Write(child); b.Write(0u);
        });
        Node(root, 100, bone0, bone1, shape); Node(bone0, 10); Node(bone1, 30);
        const int vertices = 4;
        ushort[] triangles = [0, 1, 2, 2, 1, 3];
        if (badIndex) triangles[2] = 300;
        int stride = (dynamic ? 12 : 28) + (skinned ? 12 : 0);
        ulong descriptor = (ulong)(uint)(stride / 4) | ((ulong)((dynamic ? 0x1A : 0x1B) | (skinned ? 0x40 : 0)) << 44);
        void VertexData(BinaryWriter b)
        {
            foreach (var p in Positions)
            {
                if (!dynamic) { b.Write(p.X); b.Write(p.Y); b.Write(p.Z); b.Write(0f); }
                b.Write((Half)(p.X / 400)); b.Write((Half)(1 - p.Z / 400));
                b.Write(new byte[] { 128, 0, 128, 128, 255, 128, 128, 255 });
                if (skinned)
                {
                    b.Write((Half)weight); b.Write((Half).75f); b.Write((Half)0); b.Write((Half)0);
                    b.Write(new byte[] { 0, 1, 0, 0 });
                }
            }
        }
        w.Set(shape, b =>
        {
            NifWriter.WriteObjectNet(b, name); NifWriter.WriteAvObject(b, 14, new(skinned ? 999 : 7, 0, 0), 1);
            if (legacy)
            {
                b.Write(data); b.Write(skin); b.Write(0u); b.Write(-1); b.Write((byte)0); b.Write(shader); b.Write(alpha);
            }
            else
            {
                for (int i = 0; i < 4; i++) b.Write(0f);
                b.Write(skin); b.Write(shader); b.Write(alpha); b.Write(descriptor);
                b.Write((ushort)2); b.Write((ushort)vertices); b.Write(partitioned ? 0u : (uint)(stride * vertices + 12));
                if (!partitioned) { VertexData(b); foreach (var index in triangles) b.Write(index); }
                b.Write(0u); // no particle data
                if (dynamic)
                {
                    b.Write(vertices * 16u);
                    foreach (var p in Positions) { b.Write(p.X); b.Write(p.Y); b.Write(p.Z + 17); b.Write(0f); }
                }
            }
        });
        if (legacy) w.Set(data, b =>
        {
            b.Write(0); b.Write((ushort)vertices); b.Write((byte)0); b.Write((byte)0); b.Write((byte)1);
            foreach (var p in Positions) { b.Write(p.X); b.Write(p.Y); b.Write(p.Z); }
            b.Write((ushort)1); b.Write(0u); b.Write((byte)0); // UVs, CRC, no normals: reader must reconstruct
            for (int i = 0; i < 4; i++) b.Write(0f);
            b.Write((byte)0); // no colors
            foreach (var p in Positions) { b.Write(p.X / 400); b.Write(1 - p.Z / 400); }
            b.Write((ushort)0); b.Write(-1); b.Write((ushort)2);
            if (geometry == Geometry.Strips)
            {
                b.Write((ushort)2); b.Write((ushort)6); b.Write((ushort)3); b.Write((byte)1);
                // Two valid triangles separated by degenerates, then a second strip with reset winding.
                foreach (ushort index in new ushort[] { 0, 1, 2, 2, 1, 3, 0, 1, 2 }) b.Write(badIndex && index == 3 ? (ushort)300 : index);
            }
            else
            {
                b.Write(6u); b.Write((byte)1); foreach (var index in triangles) b.Write(index); b.Write((ushort)0);
            }
        });
        if (skinned)
        {
            w.Set(skin, b => { b.Write(skinData); b.Write(partition); b.Write(root); b.Write(2u); b.Write(bone0); b.Write(missingBone ? 9000 : bone1); b.Write(0u); });
            w.Set(skinData, b =>
            {
                Transform(b, 0); b.Write(2u); b.Write(legacy);
                for (int bone = 0; bone < 2; bone++)
                {
                    Transform(b, bone == 0 ? -2 : -4);
                    for (int i = 0; i < 4; i++) b.Write(0f);
                    b.Write(legacy ? (ushort)vertices : (ushort)0);
                    if (legacy) for (ushort v = 0; v < vertices; v++) { b.Write(v); b.Write(bone == 0 ? weight : .75f); }
                }
            });
        }
        if (partitioned) w.Set(partition, b =>
        {
            b.Write(1u); b.Write((uint)(stride * vertices)); b.Write((uint)stride); b.Write(descriptor); VertexData(b);
            b.Write((ushort)vertices); b.Write((ushort)2); b.Write((ushort)2); b.Write((ushort)0); b.Write((ushort)4);
            b.Write((ushort)1); b.Write((ushort)0); // deliberately reversed local palette
            b.Write(true); foreach (ushort i in new ushort[] { 3, 2, 1, 0 }) b.Write(i);
            b.Write(false); // no per-partition weights (global VB weights are present)
            b.Write(true); foreach (var i in triangles) b.Write(i);
            b.Write(false); b.Write((byte)0); b.Write(true); b.Write(descriptor);
            foreach (var i in triangles) b.Write(i); // authoritative global indices, not vertex-map indices
        });
        w.Set(shader, b =>
        {
            if (!effect) b.Write(0u);
            NifWriter.WriteObjectNet(b, -1);
            b.Write((skinned ? 2u : 0u) | (modelNormals ? 0x1000u : 0u) | (unsupportedEffect ? 0x80u : 0u));
            b.Write(skinned ? 0x20000000u : 0u);
            b.Write(0f); b.Write(0f); b.Write(1f); b.Write(1f);
            if (effect)
            {
                NifWriter.WriteSizedString(b, @"textures\green.dds");
                b.Write((byte)3); b.Write((byte)0); b.Write((byte)0); b.Write((byte)0);
                b.Write(1f); b.Write(1f); b.Write(1f); b.Write(1f);
                b.Write(.5f); b.Write(.25f); b.Write(1f); b.Write(1f); b.Write(1f); b.Write(100f);
                NifWriter.WriteSizedString(b, "");
            }
            else
            {
                b.Write(textures); b.Write(0f); b.Write(0f); b.Write(0f); b.Write(1f); b.Write(3u);
                b.Write(1f); b.Write(0f); b.Write(0f); b.Write(1f); b.Write(1f); b.Write(1f); b.Write(0f); b.Write(0f); b.Write(0f);
            }
        });
        w.Set(textures, b => { b.Write(2u); NifWriter.WriteSizedString(b, @"textures\green.dds"); NifWriter.WriteSizedString(b, @"textures\green_n.dds"); });
        if (alpha >= 0) w.Set(alpha, b => { NifWriter.WriteObjectNet(b, -1); b.Write(alphaFlags!.Value); b.Write(alphaThreshold); });
        var bytes = w.ToBytes(root);
        if (rotatedBones)
        {
            var blocks = NifFile.Read(bytes).Blocks;
            for (int bone = 0; bone < 2; bone++)
            {
                int offset = blocks[bone == 0 ? bone0 : bone1].Offset;
                float sign = bone == 0 ? 1 : -1;
                float[] rotation = [0, -sign, 0, sign, 0, 0, 0, 0, 1];
                for (int j = 0; j < 9; j++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 28 + j * 4), rotation[j]);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 64), 2);
            }
        }
        if (le) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Array.IndexOf(bytes, (byte)'\n') + 14), 83);
        if (truncate)
        {
            // Keep the file/block table valid, but make the shape's declared payload longer than its block.
            var block = NifFile.Read(bytes).Blocks[shape];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(block.Offset + 112), 0x1000000);
        }
        return bytes;
    }

    private static void Transform(BinaryWriter b, float x)
    {
        foreach (float f in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, x, 0, 0, 1 }) b.Write(f);
    }

    internal static byte[] WithSwitch(byte[] bytes, int active, bool brokenInactive = false)
    {
        var nif = NifFile.Read(bytes); var w = new NifWriter();
        foreach (string s in nif.Strings) w.String(s);
        foreach (var block in nif.Blocks) w.Reserve(block.Type);
        foreach (var block in nif.Blocks) w.Set(block.Index, b => b.Write(bytes, block.Offset, block.Size));
        int node = w.Reserve("NiSwitchNode"), alternate = w.Reserve("BSTriShape");
        w.Set(0, b => { NifWriter.WriteObjectNet(b, -1); NifWriter.WriteAvObject(b, 14, new(100, 0, 0), 1); b.Write(3u); b.Write(1); b.Write(2); b.Write(node); b.Write(0u); });
        w.Set(node, b => { NifWriter.WriteObjectNet(b, -1); NifWriter.WriteAvObject(b, 14, Vector3.Zero, 1); b.Write(2u); b.Write(3); b.Write(alternate); b.Write(0u); b.Write((ushort)3); b.Write(active); });
        var shape = nif.Blocks[3]; var copy = bytes.AsSpan(shape.Offset, shape.Size).ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(copy.AsSpan(16), 27); // AVObject translation X
        if (brokenInactive) BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(88), 0); // invalid skin reference
        w.Set(alternate, b => b.Write(copy));
        return w.ToBytes(0);
    }

    internal static byte[] WithExtraChild(byte[] bytes, string type)
    {
        var nif = NifFile.Read(bytes); var w = new NifWriter();
        foreach (string s in nif.Strings) w.String(s);
        foreach (var block in nif.Blocks) w.Reserve(block.Type);
        foreach (var block in nif.Blocks) w.Set(block.Index, b => b.Write(bytes, block.Offset, block.Size));
        int child = w.Reserve(type);
        w.Set(child, b => { NifWriter.WriteObjectNet(b, -1); NifWriter.WriteAvObject(b, 14, Vector3.Zero, 1); });
        w.Set(0, b => { NifWriter.WriteObjectNet(b, -1); NifWriter.WriteAvObject(b, 14, Vector3.Zero, 1); b.Write(4u); b.Write(1); b.Write(2); b.Write(3); b.Write(child); b.Write(0u); });
        return w.ToBytes(0);
    }
}
