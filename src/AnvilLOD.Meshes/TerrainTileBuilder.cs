using System.Numerics;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes.Nif;

namespace AnvilLOD.Meshes;

/// <summary>SSE terrain BTR: engine-positioned chunk, model-space normal tiles and segmented LOD4 water.</summary>
public static class TerrainTileBuilder
{
    public const int Resolution = 128;
    public sealed record Geometry(Vector3[] Positions, ushort[] Indices, int SurfaceVertices);
    public sealed record Result(byte[] Bytes, Geometry Land, int WaterCells);

    public static Geometry Surface(QuadKey tile, Func<float, float, float?> sample, float skirtDepth, CancellationToken ct = default)
    {
        int level = (int)tile.Level, n = Resolution + 1;
        var positions = new List<Vector3>(n * n + n * 4);
        var indices = new List<ushort>(Resolution * Resolution * 6 + Resolution * 24);
        float span = level * CellCoord.CellSize;
        for (int y = 0; y < n; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = 0; x < n; x++)
            {
                float wx = tile.X * CellCoord.CellSize + span * x / Resolution;
                float wy = tile.Y * CellCoord.CellSize + span * y / Resolution;
                float height = sample(wx, wy) ?? throw new InvalidDataException($"Missing terrain at {tile.Worldspace} ({wx}, {wy}); supply landscape records or existing terrain LOD.");
                if (!float.IsFinite(height)) throw new InvalidDataException("Terrain contains a non-finite height.");
                positions.Add(new(span * x / Resolution / level, span * y / Resolution / level, height / level));
                if (x < Resolution && y < Resolution)
                {
                    int a = y * n + x;
                    Triangle(a, a + 1, a + n); Triangle(a + 1, a + n + 1, a + n);
                }
            }
        }
        int surface = positions.Count;
        if (skirtDepth > 0)
        {
            // Same world-space vertices at every level; skirts cover fine/coarse edge interpolation differences.
            var boundary = Enumerable.Range(0, n).Concat(Enumerable.Range(1, Resolution).Select(y => y * n + Resolution))
                .Concat(Enumerable.Range(0, Resolution).Select(x => Resolution * n + Resolution - 1 - x))
                .Concat(Enumerable.Range(1, Resolution - 1).Select(y => (Resolution - y) * n)).ToArray();
            float depth = skirtDepth / level;
            for (int i = 0; i < boundary.Length; i++)
            {
                var previous = positions[boundary[(i + boundary.Length - 1) % boundary.Length]];
                var next = positions[boundary[(i + 1) % boundary.Length]];
                depth = Math.Max(depth, Math.Abs(positions[boundary[i]].Z - (previous.Z + next.Z) * .5f) + 32f / level);
            }
            foreach (int v in boundary) positions.Add(positions[v] - new Vector3(0, 0, depth));
            for (int i = 0; i < boundary.Length; i++)
            { int next = (i + 1) % boundary.Length; Triangle(boundary[i], surface + i, boundary[next]); Triangle(boundary[next], surface + i, surface + next); }
        }
        return new(positions.ToArray(), indices.ToArray(), surface);
        void Triangle(int a, int b, int c) { indices.Add(checked((ushort)a)); indices.Add(checked((ushort)b)); indices.Add(checked((ushort)c)); }
    }

    public static Result Build(QuadKey tile, Func<float, float, float?> sample, Func<int, int, float?> water,
        float skirtDepth = 512, CancellationToken ct = default, string? season = null)
    {
        var land = Surface(tile, sample, skirtDepth, ct);
        int level = (int)tile.Level;
        var w = new NifWriter { Author = "PenguLOD" };
        int root = w.Reserve("BSMultiBoundNode"), shape = w.Reserve("BSTriShape"), shader = w.Reserve("BSLightingShaderProperty"), textures = w.Reserve("BSShaderTextureSet");
        var children = new List<int> { shape };
        var wp = new List<Vector3>(); var wi = new List<ushort>(); var segments = new List<(int Start, int Triangles)>();
        for (int x = 0; x < level; x++) for (int y = 0; y < level; y++)
        {
            ct.ThrowIfCancellationRequested();
            int start = wi.Count;
            float? height = water(tile.X + x, tile.Y + y);
            if (height is { } h && float.IsFinite(h))
            {
                int v = wp.Count; float size = CellCoord.CellSize / level;
                wp.Add(new(x * size, y * size, h / level)); wp.Add(new((x + 1) * size, y * size, h / level));
                wp.Add(new(x * size, (y + 1) * size, h / level)); wp.Add(new((x + 1) * size, (y + 1) * size, h / level));
                wi.AddRange([(ushort)v, (ushort)(v + 1), (ushort)(v + 2), (ushort)(v + 1), (ushort)(v + 3), (ushort)(v + 2)]);
            }
            segments.Add((wi.Count == start ? 0 : start, (wi.Count - start) / 3));
        }
        if (wp.Count > 0)
        {
            int waterNode = w.Reserve("BSMultiBoundNode"), waterShape = w.Reserve(level == 4 ? "BSSubIndexTriShape" : "BSTriShape");
            children.Add(waterNode);
            Node(waterNode, "WATER", [waterShape], wp.ToArray(), 0);
            WriteShape(waterShape, -1, wp.ToArray(), wi.ToArray(), -1, false, level == 4 ? segments : null);
        }
        Node(root, "chunk", children, land.Positions, 1);
        WriteShape(shape, w.String("land"), land.Positions, land.Indices, shader, true, null);
        w.Set(shader, b =>
        {
            b.Write(18u); NifWriter.WriteObjectNet(b, -1);
            b.Write(0x80401000u); b.Write(3u); // Model_Space_Normals, LOD_Landscape, depth test/write.
            b.Write(0f); b.Write(0f); b.Write(1f); b.Write(1f); b.Write(textures);
            b.Write(0f); b.Write(0f); b.Write(0f); b.Write(1f); b.Write(0u);
            b.Write(1f); b.Write(0f); b.Write(1f); b.Write(1f); b.Write(1f); b.Write(1f); b.Write(1f); b.Write(0f); b.Write(0f);
        });
        w.Set(textures, b =>
        {
            string prefix = $"textures\\terrain\\{tile.Worldspace}\\{tile.Worldspace}.{level}.{tile.X}.{tile.Y}";
            if (season is not null) prefix += "." + season;
            b.Write(2u); NifWriter.WriteSizedString(b, prefix + ".dds"); NifWriter.WriteSizedString(b, prefix + "_n.dds");
        });
        return new(w.ToBytes(root), land, wp.Count / 4);

        void Node(int block, string name, IReadOnlyList<int> nodes, Vector3[] vertices, uint culling)
        {
            int bound = w.Reserve("BSMultiBound"), aabb = w.Reserve("BSMultiBoundAABB");
            w.Set(block, b => { NifWriter.WriteObjectNet(b, w.String(name)); NifWriter.WriteAvObject(b, 14, Vector3.Zero, 1);
                b.Write((uint)nodes.Count); foreach (int child in nodes) b.Write(child); b.Write(0u); b.Write(bound); b.Write(culling); });
            w.Set(bound, b => b.Write(aabb));
            Bounds(vertices, out var lo, out var hi);
            var center = (lo + hi) * (.5f * level); var extents = (hi - lo) * (.5f * level);
            w.Set(aabb, b => { Vec(b, center); Vec(b, extents); });
        }
        void WriteShape(int block, int name, Vector3[] vertices, ushort[] triangles, int property, bool uv, List<(int Start, int Triangles)>? parts)
        {
            Bounds(vertices, out var lo, out var hi); var center = (lo + hi) * .5f;
            float radius = vertices.Max(p => Vector3.Distance(p, center));
            w.Set(block, b =>
            {
                NifWriter.WriteObjectNet(b, name); NifWriter.WriteAvObject(b, 14, Vector3.Zero, level);
                Vec(b, center); b.Write(radius); b.Write(-1); b.Write(property); b.Write(-1);
                b.Write(uv ? 0x0000300000000405UL : 0x0000100000000404UL);
                b.Write(checked((ushort)(triangles.Length / 3))); b.Write(checked((ushort)vertices.Length));
                b.Write((uint)(vertices.Length * (uv ? 20 : 16) + triangles.Length * 2));
                foreach (var p in vertices) { Vec(b, p); b.Write(0f); if (uv) { b.Write((Half)(p.X / 4096)); b.Write((Half)(1 - p.Y / 4096)); } }
                foreach (ushort i in triangles) b.Write(i); b.Write(0u);
                if (parts is not null) { b.Write((uint)parts.Count); foreach (var part in parts) { b.Write((byte)0); b.Write((uint)part.Start); b.Write((uint)part.Triangles); } }
            });
        }
    }
    private static void Vec(BinaryWriter b, Vector3 v) { b.Write(v.X); b.Write(v.Y); b.Write(v.Z); }
    private static void Bounds(Vector3[] p, out Vector3 lo, out Vector3 hi)
    { lo = new(float.MaxValue); hi = new(float.MinValue); foreach (var v in p) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); } }
}
