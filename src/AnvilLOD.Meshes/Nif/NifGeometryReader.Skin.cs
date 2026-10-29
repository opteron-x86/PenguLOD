using System.Numerics;

namespace AnvilLOD.Meshes.Nif;

public static partial class NifGeometryReader
{
    private sealed record SkinInfo(int Data, int Partition, int Root, int[] Bones);
    private sealed record SkinBuffer(int Offset, int Vertices, ulong Descriptor, ushort[] Triangles);
    private readonly record struct Influence(int Bone, float Weight);

    // Skyrim's serialized pose is sufficient for a static model only when every influencing
    // bone is in this NIF. External skeletons and animation/controller evaluation are not inferred.
    private static Dictionary<int, Matrix4x4> SceneTransforms(NifFile nif)
    {
        var result = new Dictionary<int, Matrix4x4>();
        foreach (int root in nif.Roots) Visit(root, Matrix4x4.Identity, 0);
        return result;

        void Visit(int index, Matrix4x4 parent, int depth)
        {
            if (index == -1) return;
            if (index < 0 || index >= nif.Blocks.Count) throw new InvalidDataException("Skeleton scene has an invalid child reference.");
            var block = nif.Blocks[index];
            if (!NodeTypes.Contains(block.Type)) return;
            if (depth > 256 || result.ContainsKey(index)) throw new InvalidDataException("Skeleton scene has a cyclic, repeated or excessively deep node.");
            var r = nif.ReaderFor(block);
            var av = ReadAvObject(nif, ref r);
            var world = av.Local * parent;
            ValidateTransform(world);
            result.Add(index, world);
            int count = checked((int)r.U32());
            r.Require(checked(count * 4));
            for (int i = 0; i < count; i++) Visit(r.I32(), world, depth + 1);
        }
    }

    private static SkinInfo ReadSkin(NifFile nif, int index, string? name)
    {
        if (index >= nif.Blocks.Count || nif.Blocks[index].Type is not ("NiSkinInstance" or "BSDismemberSkinInstance"))
            throw new NotSupportedException($"Skinned shape '{name}': skin instance type is not supported.");
        var r = nif.ReaderFor(nif.Blocks[index]);
        int data = r.I32(), partition = r.I32(), root = r.I32();
        int count = checked((int)r.U32());
        r.Require(checked(count * 4));
        if (count == 0) throw new InvalidDataException($"Skinned shape '{name}' has no bones.");
        var bones = new int[count];
        for (int i = 0; i < count; i++) bones[i] = r.I32();
        if (data < 0 || data >= nif.Blocks.Count || nif.Blocks[data].Type != "NiSkinData")
            throw new InvalidDataException($"Skinned shape '{name}' has no NiSkinData.");
        return new(data, partition, root, bones);
    }

    private static SkinBuffer ReadSkinBuffer(NifFile nif, int index)
    {
        if (index < 0 || index >= nif.Blocks.Count || nif.Blocks[index].Type != "NiSkinPartition")
            throw new InvalidDataException("Skinned shape has no NiSkinPartition vertex buffer.");
        var r = nif.ReaderFor(nif.Blocks[index]);
        int count = checked((int)r.U32());
        int bytes = checked((int)r.U32()), stride = checked((int)r.U32());
        ulong desc = r.U64();
        if (stride == 0 || stride != (int)(desc & 15) * 4 || bytes % stride != 0)
            throw new InvalidDataException("Invalid skin partition vertex descriptor/size.");
        int verts = bytes / stride, offset = r.Position;
        if (verts is <= 0 or > ushort.MaxValue) throw new InvalidDataException("Invalid skin partition vertex count.");
        r.Skip(bytes);
        r.Require(checked(count * 10));
        var triangles = new List<ushort>();
        for (int partition = 0; partition < count; partition++)
        {
            int nv = r.U16(), nt = r.U16(), nb = r.U16(), ns = r.U16(), nw = r.U16();
            r.Skip(nb * 2);
            if (r.U8() != 0) r.Skip(nv * 2); // vertex map; SSE's true triangles use global buffer indices
            if (r.U8() != 0) r.Skip(checked(nv * nw * 4));
            int stripPoints = 0;
            for (int i = 0; i < ns; i++) stripPoints = checked(stripPoints + r.U16());
            if (r.U8() != 0) r.Skip(checked((ns == 0 ? nt * 3 : stripPoints) * 2));
            if (r.U8() != 0) r.Skip(checked(nv * nw));
            r.U8(); r.U8(); // LOD level, global VB
            ulong partitionDesc = r.U64();
            if (partitionDesc != desc) throw new InvalidDataException("Skin partition vertex descriptors disagree.");
            r.Require(nt * 6);
            for (int i = 0; i < nt * 3; i++)
            {
                ushort vertex = r.U16();
                if (vertex >= verts) throw new InvalidDataException("Skin partition triangle index out of range.");
                triangles.Add(vertex);
            }
        }
        return new(offset, verts, desc, triangles.ToArray());
    }

    private static void BakeSkin(NifFile nif, SkinInfo skin, Dictionary<int, Matrix4x4> scene,
        Vector3[] positions, Vector3[] normals, Vector3[] tangents, Vector3[] bitangents,
        List<Influence>[]? influences, ushort[] triangles)
    {
        if (!scene.ContainsKey(skin.Root)) throw new NotSupportedException("Skinned shape requires a skeleton root outside the model.");
        var r = nif.ReaderFor(nif.Blocks[skin.Data]);
        ReadSkinTransform(ref r); // global-to-skin bind transform; per-bone inverse binds map directly into each bone
        int count = checked((int)r.U32());
        if (count != skin.Bones.Length) throw new InvalidDataException("Skin instance and skin data bone counts differ.");
        bool hasWeights = r.U8() != 0;
        bool vertexBufferWeights = influences is not null;
        influences ??= new List<Influence>[positions.Length];
        if (!vertexBufferWeights && !hasWeights) throw new NotSupportedException("Legacy skin has no per-bone vertex weights.");
        var matrices = new Matrix4x4[count];
        var directions = new Matrix4x4[count];
        r.Require(checked(count * 70));
        for (int bone = 0; bone < count; bone++)
        {
            var inverseBind = ReadSkinTransform(ref r);
            if (!scene.TryGetValue(skin.Bones[bone], out var boneWorld))
                throw new NotSupportedException($"Skinned shape bone {bone} is missing or needs an external skeleton.");
            matrices[bone] = inverseBind * boneWorld;
            ValidateTransform(matrices[bone]);
            directions[bone] = Transforms.RotationOnly(matrices[bone]);
            r.Skip(16); // bounding sphere
            int weighted = r.U16();
            if (!hasWeights) continue;
            r.Require(weighted * 6);
            for (int j = 0; j < weighted; j++)
            {
                int vertex = r.U16(); float weight = r.F32();
                if (vertex >= positions.Length || !float.IsFinite(weight) || weight < 0)
                    throw new InvalidDataException("Invalid skin weight or vertex index.");
                if (!vertexBufferWeights) (influences[vertex] ??= []).Add(new(bone, weight));
            }
        }
        var used = new bool[positions.Length];
        foreach (int v in triangles) used[v] = true;
        for (int v = 0; v < positions.Length; v++)
        {
            var weights = influences[v];
            if (!used[v] && (weights is null || weights.Count == 0)) continue;
            Vector3 p = default, n = default, t = default, b = default;
            float total = 0;
            foreach (var influence in weights ?? [])
            {
                float weight = influence.Weight; int bone = influence.Bone;
                if (!float.IsFinite(weight) || weight < 0) throw new InvalidDataException("Non-finite or negative skin weight.");
                if (weight == 0) continue;
                if (bone < 0 || bone >= count) throw new InvalidDataException("Skin weight references an invalid bone.");
                total += weight;
                p += Vector3.Transform(positions[v], matrices[bone]) * weight;
                n += Vector3.TransformNormal(normals[v], directions[bone]) * weight;
                t += Vector3.TransformNormal(tangents[v], directions[bone]) * weight;
                b += Vector3.TransformNormal(bitangents[v], directions[bone]) * weight;
            }
            // Half-float rounding is expected; missing influences are not. Never invent a pose.
            if (!float.IsFinite(total) || MathF.Abs(total - 1) > 0.05f)
                throw new InvalidDataException($"Skin weights at vertex {v} sum to {total:G6}, expected approximately 1.");
            positions[v] = p / total;
            normals[v] = Transforms.SafeNormalize(n, Vector3.UnitZ);
            tangents[v] = Transforms.SafeNormalize(t, Vector3.UnitX);
            bitangents[v] = Transforms.SafeNormalize(b, Vector3.UnitY);
        }
    }

    private static Matrix4x4 ReadSkinTransform(ref NifSpanReader r)
    {
        float a = r.F32(), b = r.F32(), c = r.F32(), d = r.F32(), e = r.F32(), f = r.F32(), g = r.F32(), h = r.F32(), i = r.F32();
        var translation = new Vector3(r.F32(), r.F32(), r.F32());
        var matrix = Transforms.Compose(a, b, c, d, e, f, g, h, i, r.F32(), translation);
        ValidateTransform(matrix);
        return matrix;
    }

    private static void ValidateTransform(Matrix4x4 matrix)
    {
        if (!float.IsFinite(matrix.GetDeterminant()) || MathF.Abs(matrix.GetDeterminant()) < 1e-12f
            || !float.IsFinite(matrix.M41) || !float.IsFinite(matrix.M42) || !float.IsFinite(matrix.M43))
            throw new InvalidDataException("Non-finite or singular model transform.");
    }

    private static LodMaterial StaticSkinMaterial(LodMaterial material) => material with
    {
        ShaderFlags1 = material.ShaderFlags1 & ~2u, // Skinned
        ShaderFlags2 = material.ShaderFlags2 & ~0x20000000u // Tree_Anim
    };

    private static void TransformVertices(Matrix4x4 world, Vector3[] pos, Vector3[] nrm, Vector3[] tan, Vector3[] bit)
    {
        ValidateTransform(world);
        var direction = Transforms.RotationOnly(world);
        for (int i = 0; i < pos.Length; i++)
        {
            pos[i] = Vector3.Transform(pos[i], world);
            nrm[i] = Transforms.SafeNormalize(Vector3.TransformNormal(nrm[i], direction), Vector3.UnitZ);
            tan[i] = Transforms.SafeNormalize(Vector3.TransformNormal(tan[i], direction), Vector3.UnitX);
            bit[i] = Transforms.SafeNormalize(Vector3.TransformNormal(bit[i], direction), Vector3.UnitY);
        }
    }

    private static void ValidateVertices(Vector3[] pos, Vector2[] uv)
    {
        if (pos.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
            || uv.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y)))
            throw new InvalidDataException("Non-finite vertex positions or texture coordinates.");
    }

    private static ushort[] ReadStrips(ref NifSpanReader r, int vertices)
    {
        int count = r.U16();
        var lengths = new ushort[count];
        for (int i = 0; i < count; i++) lengths[i] = r.U16();
        if (r.U8() == 0) throw new InvalidDataException("Triangle strips have no point data.");
        r.Require(checked(lengths.Sum(v => (int)v) * 2));
        var triangles = new List<ushort>();
        foreach (int length in lengths)
        {
            ushort a = 0, b = 0;
            for (int j = 0; j < length; j++)
            {
                ushort c = r.U16();
                if (c >= vertices) throw new InvalidDataException("Triangle strip index out of range.");
                if (j >= 2 && a != b && a != c && b != c)
                {
                    // Alternation continues across degenerate joins, but resets at each strip.
                    triangles.Add((j & 1) == 0 ? a : b);
                    triangles.Add((j & 1) == 0 ? b : a);
                    triangles.Add(c);
                }
                a = b; b = c;
            }
        }
        return triangles.ToArray();
    }
}
