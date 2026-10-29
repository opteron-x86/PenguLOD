using System.Numerics;

namespace AnvilLOD.Meshes.Nif;

/// <summary>
/// Extracts drawable geometry from an SSE NIF: walks the scene graph from the root,
/// accumulates node transforms, and reads packed/legacy triangle geometry and self-contained
/// static skin poses into mesh space. Texture renderers can opt into static effect materials.
/// </summary>
public static partial class NifGeometryReader
{
    // NiNode and subclasses whose children we follow. Subclass-specific fields come after the
    // children list, so the NiNode layout is enough for traversal.
    private static readonly HashSet<string> NodeTypes = new(StringComparer.Ordinal)
    {
        "NiNode", "BSFadeNode", "BSMultiBoundNode", "BSLeafAnimNode", "BSTreeNode", "BSOrderedNode",
        "BSValueNode", "NiBillboardNode", "NiSwitchNode", "NiLODNode", "BSDebrisNode", "BSBlastNode",
        "BSDamageStage", "BSRangeNode", "BSWArray", "NiSortAdjustNode", "BSMasterParticleSystem",
    };

    private static readonly HashSet<string> ShapeTypes = new(StringComparer.Ordinal)
    {
        "BSTriShape", "BSSubIndexTriShape", "BSMeshLODTriShape", "BSDynamicTriShape",
    };

    // Shader flag bits LOD keeps from the source (everything else is set the LODGen way).
    private const uint KeepF1 = 0x0000_1000 /* Model_Space_Normals */ | 0x0000_0008 /* Vertex_Alpha */;
    private const uint KeepF2 = 0x0000_0010 /* Double_Sided */ | 0x0000_0040 /* Glow_Map */;

    /// <summary>Prefix of the warning added to a mesh for every water-shader shape read as lit fake water.</summary>
    public const string WaterPlaneWarning = "WaterPlane:";

    /// <summary>Colour (RGBA, little-endian) of fake water: CS Water Mod's dark teal at ~69% opacity (0xAF33342A), made 10% more transparent and 10% darker so it sits closer to the water around it.</summary>
    public const uint FakeWaterColor = 0x9E2E2F26u;

    /// <summary>
    /// A BSWaterShaderProperty plane drawn outside the engine's water system comes out black, so LOD stand-ins draw it with
    /// a lit shader instead, the way CS Water Mod's stream twins do ("FakeWater": FXwaterTile01 with vertex alpha, alpha
    /// blended). Same flags, alpha property and shader values as that shape.
    /// </summary>
    public static readonly LodMaterial FakeWaterMaterial = new(
        [FakeWaterDiffuse, FakeWaterNormal, "", "", "", "", "", "", ""],
        0x8E40_0309u, 0x0000_8020u, 3,
        true, 0x10ED, 128,
        Vector3.Zero, 1f,
        new LodShaderPassthru(0, 0f, 0f, 1f, 1f, 1f, 0f, 202f, Vector3.One, 2f, 0.3f, 2f));

    /// <summary>The smooth water tile AnvilLOD ships and writes into its output (assets\water, CS Water Mod's flat tile).</summary>
    public const string FakeWaterDiffuse = "textures\\anvillod\\water\\watertile_d.dds";
    public const string FakeWaterNormal = "textures\\anvillod\\water\\watertile_n.dds";

    /// <summary>The fake-water plane's UVs are tiled this many times over the source plane's 0-1 range.</summary>
    public const float FakeWaterUvTiling = 4f;

    /// <param name="passthru">Keep every shape's own shader settings (LODGen's "passthru" LOD models) instead of
    /// reducing them to the standard LOD shader.</param>
    /// <param name="waterAsLit">Read BSWaterShaderProperty shapes as lit fake water (<see cref="FakeWaterMaterial"/>) instead of skipping them.</param>
    public static LodMesh Read(string path, byte[] data, bool passthru = false, bool waterAsLit = false, bool includeTexGenPlane = false, bool includeUnshaded = false, bool renderEffects = false)
    {
        var nif = NifFile.Read(data);
        var parts = new List<LodMeshPart>();
        var warnings = new List<string>();
        var visited = new HashSet<int>();
        var scene = new Lazy<Dictionary<int, Matrix4x4>>(() => SceneTransforms(nif));

        foreach (var root in nif.Roots)
            Walk(nif, root, Matrix4x4.Identity, parts, warnings, visited, passthru, waterAsLit, includeTexGenPlane, includeUnshaded, scene, renderEffects);

        bool anyShape = nif.Blocks.Any(b => ShapeTypes.Contains(b.Type) || b.Type is "NiTriShape" or "BSLODTriShape" or "NiTriStrips");
        return new LodMesh { Path = path, Parts = parts, Warnings = warnings, NoShapesInFile = !anyShape && warnings.Count == 0 };
    }

    /// <summary>
    /// The vertex descriptor (layout of the vertex buffer: stride, attribute offsets and which attributes exist) of the first triangle shape in an
    /// SSE mesh, or null if it has none. Two shapes with the same descriptor can swap vertex buffers without the shader noticing.
    /// </summary>
    public static ulong? FirstShapeVertexDescriptor(byte[] data)
    {
        var nif = NifFile.Read(data);
        foreach (var block in nif.Blocks)
        {
            if (!ShapeTypes.Contains(block.Type)) continue;
            var r = nif.ReaderFor(block);
            ReadAvObject(nif, ref r);
            r.Skip(16);                 // bounding sphere
            r.I32(); r.I32(); r.I32();  // skin, shader, alpha
            return r.U64();
        }
        return null;
    }

    /// <summary>Marker for unshaded terrain water geometry; only returned when explicitly requested.</summary>
    public static readonly LodMaterial UnshadedMaterial = new([], 0, 0, 0, false, 0, 0, Vector3.Zero, 0);

    // ---------- traversal ----------

    private struct AvObject
    {
        public string? Name;
        public uint Flags;
        public float Scale;
        public Matrix4x4 Local;
    }

    private static AvObject ReadAvObject(NifFile nif, ref NifSpanReader r)
    {
        var o = new AvObject();
        o.Name = nif.String(r.I32());
        uint numExtra = r.U32();
        r.Skip(checked((int)numExtra * 4));
        r.I32(); // controller
        o.Flags = r.U32();
        var t = new Vector3(r.F32(), r.F32(), r.F32());
        // 9 floats, read as rows; v' = R * v (nifly / Gamebryo NiMatrix3 convention).
        float m11 = r.F32(), m12 = r.F32(), m13 = r.F32();
        float m21 = r.F32(), m22 = r.F32(), m23 = r.F32();
        float m31 = r.F32(), m32 = r.F32(), m33 = r.F32();
        float s = r.F32(); o.Scale = s;
        r.I32(); // collision object
        o.Local = Transforms.Compose(m11, m12, m13, m21, m22, m23, m31, m32, m33, s, t);
        return o;
    }

    private static void Walk(NifFile nif, int index, Matrix4x4 parent, List<LodMeshPart> parts, List<string> warnings,
        HashSet<int> visited, bool passthru, bool waterAsLit, bool includeTexGenPlane, bool includeUnshaded,
        Lazy<Dictionary<int, Matrix4x4>> scene, bool renderEffects, int depth = 0)
    {
        if (index == -1) return;
        if (index < 0 || index >= nif.Blocks.Count) { warnings.Add($"Scene child {index} could not be read: invalid block reference."); return; }
        if (depth > 256 || !visited.Add(index)) { warnings.Add($"Scene child {index} could not be read: repeated, cyclic or excessively deep node."); return; }
        var block = nif.Blocks[index];
        try
        {
            if (NodeTypes.Contains(block.Type))
            {
                var r = nif.ReaderFor(block);
                var av = ReadAvObject(nif, ref r);
                if ((av.Flags & 1) != 0) return;
                var world = av.Local * parent;
                int count = checked((int)r.U32());
                r.Require(checked(count * 4));
                var children = new int[count];
                for (int i = 0; i < count; i++) children[i] = r.I32();
                if (block.Type is "NiSwitchNode" or "NiLODNode")
                {
                    int effects = checked((int)r.U32()); r.Skip(checked(effects * 4));
                    r.U16(); int active = r.I32();
                    if (active == -1 && block.Type == "NiSwitchNode") return;
                    if (active < 0 || active >= count) throw new NotSupportedException("Switch/LOD node has no valid serialized active child.");
                    children = [children[active]];
                }
                foreach (int child in children)
                    Walk(nif, child, world, parts, warnings, visited, passthru, waterAsLit, includeTexGenPlane, includeUnshaded, scene, renderEffects, depth + 1);
            }
            else if (ShapeTypes.Contains(block.Type))
            {
                var part = ReadShape(nif, block, parent, warnings, passthru, waterAsLit, includeTexGenPlane, includeUnshaded, scene, renderEffects);
                if (part is not null) parts.Add(part);
            }
            else if (block.Type is "NiTriShape" or "BSLODTriShape" or "NiTriStrips")
            {
                var part = ReadLeShape(nif, block, parent, warnings, passthru, waterAsLit, includeTexGenPlane, includeUnshaded, scene, renderEffects);
                if (part is not null) parts.Add(part);
            }
            else warnings.Add($"Scene child {block.Index} ({block.Type}) is not supported; skipped.");
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException or NotSupportedException)
        {
            warnings.Add($"Shape/node {block.Index} ({block.Type}) could not be read: {ex.Message}");
        }
    }

    // ---------- shapes ----------

    private static LodMeshPart? ReadShape(NifFile nif, NifBlock block, Matrix4x4 parent, List<string> warnings, bool passthru, bool waterAsLit, bool includeTexGenPlane, bool includeUnshaded, Lazy<Dictionary<int, Matrix4x4>> scene, bool renderEffects)
    {
        var r = nif.ReaderFor(block);
        var av = ReadAvObject(nif, ref r);
        if ((av.Flags & 1) != 0 && !(includeTexGenPlane && av.Name?.Contains("TexGen Plane", StringComparison.OrdinalIgnoreCase) == true)) return null;

        r.Skip(16); // bounding sphere
        int skin = r.I32();
        int shaderRef = r.I32();
        int alphaRef = r.I32();
        ulong desc = r.U64();
        int numTris = r.U16();
        int numVerts = r.U16();
        uint dataSize = r.U32();

        SkinInfo? skinInfo = skin >= 0 ? ReadSkin(nif, skin, av.Name) : null;
        SkinBuffer? partition = null;
        if (skinInfo is not null && dataSize == 0)
        {
            partition = ReadSkinBuffer(nif, skinInfo.Partition);
            numVerts = partition.Vertices;
            numTris = partition.Triangles.Length / 3;
            desc = partition.Descriptor;
        }
        if (numVerts == 0 || numTris == 0) return null;
        if (dataSize == 0 && partition is null) throw new InvalidDataException("Shape has counts but no vertex/triangle buffer.");

        bool viewportPlane = includeTexGenPlane && av.Name?.Contains("TexGen Plane", StringComparison.OrdinalIgnoreCase) == true;
        var material = viewportPlane ? new LodMaterial([""], 0, 0, 0, alphaRef >= 0, 0, 0, Vector3.Zero, 0)
            : ReadMaterial(nif, shaderRef, alphaRef, passthru, waterAsLit, renderEffects);
        if (includeUnshaded && shaderRef < 0) material = UnshadedMaterial;
        if (material is null) { warnings.Add($"Shape '{av.Name}' has no lighting shader; skipped."); return null; }
        bool water = waterAsLit && ReferenceEquals(material, FakeWaterMaterial);
        if (water) warnings.Add(WaterPlaneWarning + av.Name);

        uint attrs = (uint)(desc >> 44);
        int vertexSize = (int)(desc & 0xF) * 4;
        bool hasPos = (attrs & 0x1) != 0, hasUV = (attrs & 0x2) != 0, hasNormal = (attrs & 0x8) != 0,
             hasTangent = (attrs & 0x18) == 0x18, hasColor = (attrs & 0x20) != 0, hasSkin = (attrs & 0x40) != 0,
             hasEye = (attrs & 0x100) != 0;
        if (!hasPos && block.Type != "BSDynamicTriShape") throw new NotSupportedException("Vertex positions are absent.");
        int attributesSize = (hasPos ? 16 : 0) + (hasUV ? 4 : 0) + (hasNormal ? 4 : 0) + (hasTangent ? 4 : 0)
            + (hasColor ? 4 : 0) + (hasSkin ? 12 : 0) + (hasEye ? 4 : 0);
        if (vertexSize < attributesSize || vertexSize == 0) throw new InvalidDataException("Vertex descriptor is shorter than its attributes.");
        int bufferSize = checked(vertexSize * numVerts + (partition is null ? numTris * 6 : 0));
        if (partition is null && dataSize != bufferSize) throw new InvalidDataException("Vertex/triangle data size does not match the descriptor and counts.");
        var vr = partition is null ? r : nif.ReaderFor(nif.Blocks[skinInfo!.Partition]);
        if (partition is not null) vr.Position = partition.Offset;
        vr.Require(bufferSize);

        var world = av.Local * parent;

        var pos = new Vector3[numVerts];
        var uv = new Vector2[numVerts];
        var nrm = new Vector3[numVerts];
        var tan = new Vector3[numVerts];
        var bit = new Vector3[numVerts];
        uint[]? col = hasColor ? new uint[numVerts] : null;
        var influences = skinInfo is null ? null : new List<Influence>[numVerts];

        for (int i = 0; i < numVerts; i++)
        {
            int start = vr.Position;
            var p = hasPos ? new Vector3(vr.F32(), vr.F32(), vr.F32()) : Vector3.Zero;
            float bx = 0;
            if ((attrs & 0x11) == 0x11) bx = vr.F32();
            else if ((attrs & 0x11) == 0x1) vr.Skip(4);
            if (hasUV) uv[i] = new Vector2((float)vr.F16(), (float)vr.F16());
            Vector3 n = Vector3.UnitZ, t = Vector3.UnitX;
            float by = 0, bz = 0;
            if (hasNormal) { n = Transforms.DecodeDir(vr.U8(), vr.U8(), vr.U8()); by = Transforms.DecodeByte(vr.U8()); }
            if (hasTangent) { t = Transforms.DecodeDir(vr.U8(), vr.U8(), vr.U8()); bz = Transforms.DecodeByte(vr.U8()); }
            if (hasColor) col![i] = vr.U32();
            if (hasSkin)
            {
                float w0 = (float)vr.F16(), w1 = (float)vr.F16(), w2 = (float)vr.F16(), w3 = (float)vr.F16();
                byte b0 = vr.U8(), b1 = vr.U8(), b2 = vr.U8(), b3 = vr.U8();
                if (influences is not null) influences[i] = [new(b0, w0), new(b1, w1), new(b2, w2), new(b3, w3)];
            }
            if (hasEye) vr.Skip(4);
            vr.Position = start + vertexSize;

            pos[i] = p;
            nrm[i] = n;
            tan[i] = t;
            var b = hasTangent ? new Vector3(bx, by, bz) : Vector3.Cross(n, t);
            bit[i] = b;
        }

        var tris = partition?.Triangles ?? new ushort[numTris * 3];
        if (partition is null) for (int i = 0; i < tris.Length; i++) tris[i] = vr.U16();
        foreach (var idx in tris)
            if (idx >= numVerts) throw new InvalidDataException("Triangle index out of range.");

        if (block.Type == "BSDynamicTriShape")
        {
            // Dynamic positions are outside the interleaved buffer, even when that buffer lives in the skin partition.
            r.Position += checked((int)dataSize);
            uint particles = r.U32();
            if (particles != 0) r.Skip(checked(numVerts * 12 + numTris * 6));
            if (r.U32() != numVerts * 16) throw new InvalidDataException("Dynamic vertex count does not match the shape.");
            for (int i = 0; i < numVerts; i++) { pos[i] = new(r.F32(), r.F32(), r.F32()); bit[i].X = r.F32(); }
        }
        if (!hasNormal) nrm = FaceNormals(pos, tris);
        if (!hasTangent) (tan, bit) = TangentsFromUv(pos, uv, nrm, tris);
        if (skinInfo is not null)
        {
            if (!hasSkin) throw new NotSupportedException("Skinned shape has no vertex weights in its buffer.");
            BakeSkin(nif, skinInfo, scene.Value, pos, nrm, tan, bit, influences!, tris);
            material = StaticSkinMaterial(material);
        }
        else TransformVertices(world, pos, nrm, tan, bit);
        ValidateVertices(pos, uv);

        if (water)
        {
            // A flat water surface: face up, one body colour with the fake water's alpha, tiled texture.
            col = new uint[numVerts];
            for (int i = 0; i < numVerts; i++)
            {
                col[i] = FakeWaterColor;
                nrm[i] = Vector3.UnitZ; tan[i] = Vector3.UnitX; bit[i] = Vector3.UnitY;
                uv[i] *= FakeWaterUvTiling;
            }
        }

        return new LodMeshPart
        {
            Name = av.Name, TexGenScale = av.Scale,
            Material = material,
            Positions = pos, UVs = uv, Normals = nrm, Tangents = tan, Bitangents = bit,
            Colors = col, Triangles = tris,
        };
    }

    /// <summary>
    /// A Skyrim LE (Oldrim) shape: NiTriShape (or BSLODTriShape) with a NiTriShapeData block. Tangents come from the data block when it has
    /// them, otherwise they are worked out from the UVs (LE keeps them in an extra data block we don't read).
    /// </summary>
    private static LodMeshPart? ReadLeShape(NifFile nif, NifBlock block, Matrix4x4 parent, List<string> warnings, bool passthru, bool waterAsLit, bool includeTexGenPlane, bool includeUnshaded, Lazy<Dictionary<int, Matrix4x4>> scene, bool renderEffects)
    {
        var r = nif.ReaderFor(block);
        var av = ReadAvObject(nif, ref r);
        if ((av.Flags & 1) != 0 && !(includeTexGenPlane && av.Name?.Contains("TexGen Plane", StringComparison.OrdinalIgnoreCase) == true)) return null;

        int dataRef = r.I32();
        int skin = r.I32();
        uint numMaterials = r.U32();
        r.Skip(checked((int)numMaterials * 8));   // material names and extra data
        r.I32();                         // active material
        r.U8();                          // material needs update
        int shaderRef = r.I32();
        int alphaRef = r.I32();

        bool strips = block.Type == "NiTriStrips";
        if (dataRef < 0 || dataRef >= nif.Blocks.Count || nif.Blocks[dataRef].Type != (strips ? "NiTriStripsData" : "NiTriShapeData"))
            throw new InvalidDataException("Geometry data block is missing or has the wrong type.");
        SkinInfo? skinInfo = skin >= 0 ? ReadSkin(nif, skin, av.Name) : null;

        bool viewportPlane = includeTexGenPlane && av.Name?.Contains("TexGen Plane", StringComparison.OrdinalIgnoreCase) == true;
        var material = viewportPlane ? new LodMaterial([""], 0, 0, 0, alphaRef >= 0, 0, 0, Vector3.Zero, 0)
            : ReadMaterial(nif, shaderRef, alphaRef, passthru, waterAsLit, renderEffects);
        if (includeUnshaded && shaderRef < 0) material = UnshadedMaterial;
        if (material is null) { warnings.Add($"Shape '{av.Name}' has no lighting shader; skipped."); return null; }
        bool water = waterAsLit && ReferenceEquals(material, FakeWaterMaterial);
        if (water) warnings.Add(WaterPlaneWarning + av.Name);

        var d = nif.ReaderFor(nif.Blocks[dataRef]);
        d.I32();                          // group id
        int numVerts = d.U16();
        d.U8(); d.U8();                   // keep and compress flags
        bool hasVerts = d.U8() != 0;
        if (numVerts == 0) return null;
        if (!hasVerts) throw new InvalidDataException("Geometry declares vertices but has no vertex data.");
        var src = new Vector3[numVerts];
        for (int i = 0; i < numVerts; i++) src[i] = new Vector3(d.F32(), d.F32(), d.F32());
        ushort vectorFlags = d.U16();
        d.U32();                          // material CRC (Skyrim LE geometry data carries one)
        bool hasNormals = d.U8() != 0;
        var srcN = new Vector3[numVerts];
        Vector3[]? srcT = null, srcB = null;
        if (hasNormals)
        {
            for (int i = 0; i < numVerts; i++) srcN[i] = new Vector3(d.F32(), d.F32(), d.F32());
            if ((vectorFlags & 0x1000) != 0)
            {
                srcT = new Vector3[numVerts]; srcB = new Vector3[numVerts];
                for (int i = 0; i < numVerts; i++) srcT[i] = new Vector3(d.F32(), d.F32(), d.F32());
                for (int i = 0; i < numVerts; i++) srcB[i] = new Vector3(d.F32(), d.F32(), d.F32());
            }
        }
        d.Skip(16);                       // bounding sphere
        bool hasColors = d.U8() != 0;
        uint[]? col = null;
        if (hasColors)
        {
            col = new uint[numVerts];
            for (int i = 0; i < numVerts; i++)
            {
                byte cr = ToByte(d.F32()), cg = ToByte(d.F32()), cb = ToByte(d.F32()), ca = ToByte(d.F32());
                col[i] = (uint)(ca << 24 | cb << 16 | cg << 8 | cr);
            }
        }
        int numUvSets = vectorFlags & 1; // Bethesda geometry uses a single UV-set bit, not the generic NiGeometryData mask.
        var uv = new Vector2[numVerts];
        for (int set = 0; set < numUvSets; set++)
            for (int i = 0; i < numVerts; i++)
            {
                var uvv = new Vector2(d.F32(), d.F32());
                if (set == 0) uv[i] = uvv;
            }
        d.U16();                          // consistency flags
        d.I32();                          // additional data

        // NiTriShapeData
        int numTris = d.U16();
        ushort[] tris;
        if (strips) tris = ReadStrips(ref d, numVerts);
        else
        {
            if (d.U32() != numTris * 3) throw new InvalidDataException("Triangle point count does not match the triangle count.");
            bool hasTris = d.U8() != 0;
            if (numTris == 0) return null;
            if (!hasTris) throw new InvalidDataException("Geometry declares triangles but has no triangle data.");
            d.Require(numTris * 6);
            tris = new ushort[numTris * 3];
            for (int i = 0; i < tris.Length; i++) tris[i] = d.U16();
        }
        foreach (var idx in tris)
            if (idx >= numVerts) throw new InvalidDataException("Triangle index out of range.");

        if (!hasNormals) srcN = FaceNormals(src, tris);
        if (srcT is null || srcB is null) (srcT, srcB) = TangentsFromUv(src, uv, srcN, tris);

        var pos = src; var nrm = srcN; var tan = srcT; var bit = srcB;
        if (skinInfo is not null)
        {
            BakeSkin(nif, skinInfo, scene.Value, pos, nrm, tan, bit, null, tris);
            material = StaticSkinMaterial(material);
        }
        else TransformVertices(av.Local * parent, pos, nrm, tan, bit);
        ValidateVertices(pos, uv);
        if (water)
        {
            col = new uint[numVerts];
            for (int i = 0; i < numVerts; i++)
            {
                col[i] = FakeWaterColor;
                nrm[i] = Vector3.UnitZ; tan[i] = Vector3.UnitX; bit[i] = Vector3.UnitY;
                uv[i] *= FakeWaterUvTiling;
            }
        }

        return new LodMeshPart
        {
            Name = av.Name, TexGenScale = av.Scale,
            Material = material,
            Positions = pos, UVs = uv, Normals = nrm, Tangents = tan, Bitangents = bit,
            Colors = col, Triangles = tris,
        };
    }

    private static byte ToByte(float f) => (byte)Math.Clamp((int)MathF.Round(f * 255f), 0, 255);

    private static Vector3[] FaceNormals(Vector3[] pos, ushort[] tris)
    {
        var n = new Vector3[pos.Length];
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            var fn = Vector3.Cross(pos[tris[t + 1]] - pos[tris[t]], pos[tris[t + 2]] - pos[tris[t]]);
            n[tris[t]] += fn; n[tris[t + 1]] += fn; n[tris[t + 2]] += fn;
        }
        for (int i = 0; i < n.Length; i++) n[i] = Transforms.SafeNormalize(n[i], Vector3.UnitZ);
        return n;
    }

    /// <summary>Per-vertex tangent and bitangent from the UV layout, made orthogonal to the normal.</summary>
    private static (Vector3[] Tangents, Vector3[] Bitangents) TangentsFromUv(Vector3[] pos, Vector2[] uv, Vector3[] nrm, ushort[] tris)
    {
        var tan = new Vector3[pos.Length];
        var bit = new Vector3[pos.Length];
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
            var e1 = pos[b] - pos[a]; var e2 = pos[c] - pos[a];
            var d1 = uv[b] - uv[a]; var d2 = uv[c] - uv[a];
            float det = d1.X * d2.Y - d2.X * d1.Y;
            if (MathF.Abs(det) < 1e-12f) continue;
            float inv = 1f / det;
            var tv = (e1 * d2.Y - e2 * d1.Y) * inv;
            var bv = (e2 * d1.X - e1 * d2.X) * inv;
            tan[a] += tv; tan[b] += tv; tan[c] += tv;
            bit[a] += bv; bit[b] += bv; bit[c] += bv;
        }
        for (int i = 0; i < pos.Length; i++)
        {
            var n = nrm[i];
            var t = tan[i] - n * Vector3.Dot(n, tan[i]);
            t = Transforms.SafeNormalize(t, Vector3.UnitX);
            var b = Transforms.SafeNormalize(Vector3.Cross(n, t), Vector3.UnitY);
            if (Vector3.Dot(b, bit[i]) < 0) b = -b;
            tan[i] = t; bit[i] = b;
        }
        return (tan, bit);
    }

    private static LodMaterial? ReadMaterial(NifFile nif, int shaderRef, int alphaRef, bool passthru, bool waterAsLit, bool renderEffects)
    {
        if (shaderRef < 0 || shaderRef >= nif.Blocks.Count) return null;
        var sb = nif.Blocks[shaderRef];
        if (waterAsLit && sb.Type == "BSWaterShaderProperty") return FakeWaterMaterial;
        if (renderEffects && sb.Type == "BSEffectShaderProperty") return ReadStaticEffect(nif, sb, alphaRef);
        if (sb.Type != "BSLightingShaderProperty") throw new NotSupportedException($"Shader {sb.Type} is not supported for this operation.");

        var r = nif.ReaderFor(sb);
        uint shaderType = r.U32();
        r.I32();            // name
        uint ne = r.U32(); r.Skip((int)ne * 4);
        r.I32();            // controller
        uint f1 = r.U32();
        uint f2 = r.U32();
        float uvOx = r.F32(), uvOy = r.F32(), uvSx = r.F32(), uvSy = r.F32();
        int texSetRef = r.I32();
        var emissive = new Vector3(r.F32(), r.F32(), r.F32());
        float emissiveMult = r.F32();
        uint clamp = r.U32();

        LodShaderPassthru? keep = null;
        if (passthru)
        {
            float alpha = r.F32(), refraction = r.F32(), gloss = r.F32();
            var spec = new Vector3(r.F32(), r.F32(), r.F32());
            float specStrength = r.F32(), le1 = r.F32(), le2 = r.F32();
            keep = new LodShaderPassthru(shaderType, uvOx, uvOy, uvSx, uvSy, alpha, refraction, gloss, spec, specStrength, le1, le2);
        }

        var textures = new List<string>();
        if (texSetRef >= 0 && texSetRef < nif.Blocks.Count && nif.Blocks[texSetRef].Type == "BSShaderTextureSet")
        {
            var tr = nif.ReaderFor(nif.Blocks[texSetRef]);
            uint n = tr.U32();
            for (int i = 0; i < n; i++) textures.Add(AnvilLOD.Core.World.GamePath.NormalizeTexture(tr.SizedString()));
        }
        if (textures.Count == 0 || string.IsNullOrWhiteSpace(textures[0])) return null;
        while (textures.Count < 9) textures.Add("");

        bool hasAlpha = false; ushort alphaFlags = 0; byte threshold = 0;
        if (alphaRef >= 0 && alphaRef < nif.Blocks.Count && nif.Blocks[alphaRef].Type == "NiAlphaProperty")
        {
            var ar = nif.ReaderFor(nif.Blocks[alphaRef]);
            ar.I32(); uint aen = ar.U32(); ar.Skip((int)aen * 4); ar.I32();
            alphaFlags = ar.U16();
            threshold = ar.U8();
            hasAlpha = true;
        }

        if (keep is not null)
            return new LodMaterial(textures, f1, f2, clamp, hasAlpha, alphaFlags, threshold, emissive, emissiveMult, keep);

        bool glow = (f2 & 0x40) != 0;
        return new LodMaterial(
            textures,
            0x8000_0300u | (f1 & KeepF1),
            0x0000_0005u | (f2 & KeepF2),
            clamp,
            hasAlpha, alphaFlags, threshold,
            glow ? emissive : Vector3.Zero,
            glow ? emissiveMult : 1f);
    }
}
