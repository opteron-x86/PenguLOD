using System.Numerics;
using AnvilLOD.Core.Pipeline;

namespace AnvilLOD.Meshes.Authoring;

/// <summary>Prunes complete leaf cards while bounding projected alpha-coverage loss per material and view.</summary>
public static class FoliageReducer
{
    public sealed record Result(LodMesh Mesh, int RemovedTriangles, float CoverageLoss, int Cards, string? Note = null);
    private const int Resolution = 64, MaxSamples = 16_000_000, MaxMaskPixels = 8_000_000;
    private const int PixelsPerView = Resolution * Resolution;
    private static readonly Vector3[] Views = Enumerable.Range(0, 8)
        .Select(i => new Vector3(MathF.Cos(i * MathF.PI / 4), MathF.Sin(i * MathF.PI / 4), 0))
        .Concat([Vector3.UnitZ, -Vector3.UnitZ,
            Vector3.Normalize(new(1, 1, 1)), Vector3.Normalize(new(-1, 1, 1)),
            Vector3.Normalize(new(1, -1, 1)), Vector3.Normalize(new(-1, -1, 1))]).ToArray();

    private sealed record Card(int Part, int[] Triangles, Vector3 Center, bool Removable)
    {
        public int[] Pixels { get; set; } = [];
        public bool Removed { get; set; }
    }
    private readonly record struct Projection(Vector3 Right, Vector3 Up, Vector2 Min, Vector2 Scale)
    {
        public Vector2 Point(Vector3 p) => (new Vector2(Vector3.Dot(p, Right), Vector3.Dot(p, Up)) - Min) * Scale + Vector2.One;
    }

    /// <param name="opacity">Returns a material's alpha sampler, including UV transforms and addressing. Null leaves that material unchanged.</param>
    public static Result Reduce(LodMesh mesh, int targetTriangles, float maxCoverageLoss,
        Func<LodMaterial, Func<Vector2, float>?> opacity)
    {
        if (targetTriangles < 1 || !float.IsFinite(maxCoverageLoss) || maxCoverageLoss is < 0 or > .2f)
            throw new ArgumentException("Invalid foliage budget or coverage loss.");
        int triangles = mesh.Parts.Sum(p => p.TriangleCount);
        if (triangles <= targetTriangles) return new(mesh, 0, 0, 0);
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        var cards = new List<Card>();
        var samplers = new Dictionary<int, Func<Vector2, float>>();
        var groupForPart = new Dictionary<int, int>();
        for (int i = 0; i < mesh.Parts.Count; i++)
        {
            RunLimits.Check();
            var part = mesh.Parts[i];
            // Blended foliage cannot be assessed with a binary alpha-coverage mask.
            if (!part.Material.HasAlpha || (part.Material.AlphaFlags & 1) != 0
                || (part.Material.AlphaFlags & 512) == 0 || ((part.Material.AlphaFlags >> 10) & 7) is not (4 or 6)
                || opacity(part.Material) is not { } sampler) continue;
            if (!groups.TryGetValue(part.Material.Key, out int group)) groups.Add(part.Material.Key, group = groups.Count);
            groupForPart[i] = group; samplers[i] = sampler;
            cards.AddRange(Components(part, i));
        }
        long requiredPixels = (long)groups.Count * Views.Length * PixelsPerView;
        if (cards.Count == 0 || requiredPixels > MaxMaskPixels)
            return new(mesh, 0, 0, 0, "No eligible cards or the coverage workspace limit was reached.");
        int maskSize = (int)requiredPixels;
        var positions = mesh.Parts.SelectMany(p => p.Positions).ToArray();
        if (positions.Length == 0) return new(mesh, 0, 0, 0);
        var projections = Views.Select(view => Project(positions, view)).ToArray();
        var counts = new int[maskSize];
        long samples = 0;
        foreach (var card in cards)
        {
            RunLimits.Check();
            var part = mesh.Parts[card.Part];
            var covered = new HashSet<int>();
            int group = groupForPart[card.Part];
            float threshold = part.Material.AlphaThreshold / 255f;
            for (int view = 0; view < Views.Length; view++)
                foreach (int triangle in card.Triangles)
                {
                    int a = part.Triangles[triangle * 3], b = part.Triangles[triangle * 3 + 1], c = part.Triangles[triangle * 3 + 2];
                    var normal = Vector3.Cross(part.Positions[b] - part.Positions[a], part.Positions[c] - part.Positions[a]);
                    if ((part.Material.ShaderFlags2 & 0x10) == 0 && Vector3.Dot(normal, Views[view]) <= 0) continue;
                    var p0 = projections[view].Point(part.Positions[a]);
                    var p1 = projections[view].Point(part.Positions[b]);
                    var p2 = projections[view].Point(part.Positions[c]);
                    float area = Edge(p0, p1, p2);
                    if (MathF.Abs(area) < 1e-6f) continue;
                    int x0 = Math.Clamp((int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))), 0, Resolution - 1);
                    int x1 = Math.Clamp((int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))), 0, Resolution - 1);
                    int y0 = Math.Clamp((int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))), 0, Resolution - 1);
                    int y1 = Math.Clamp((int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))), 0, Resolution - 1);
                    samples += (long)(x1 - x0 + 1) * (y1 - y0 + 1);
                    if (samples > MaxSamples) return new(mesh, 0, 0, cards.Count, "Coverage sampling limit reached; foliage retained.");
                    for (int y = y0; y <= y1; y++)
                    {
                        RunLimits.Check();
                        for (int x = x0; x <= x1; x++)
                        {
                            var point = new Vector2(x + .5f, y + .5f);
                            float w0 = Edge(p1, p2, point) / area, w1 = Edge(p2, p0, point) / area, w2 = 1 - w0 - w1;
                            if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                            var uv = part.UVs[a] * w0 + part.UVs[b] * w1 + part.UVs[c] * w2;
                            float alpha = samplers[card.Part](uv);
                            if (((part.Material.AlphaFlags >> 10) & 7) == 4 ? alpha <= threshold : alpha < threshold) continue;
                            covered.Add((group * Views.Length + view) * PixelsPerView + y * Resolution + x);
                        }
                    }
                }
            card.Pixels = covered.Order().ToArray();
            foreach (int pixel in card.Pixels) counts[pixel]++;
        }
        var boundary = new bool[maskSize];
        var original = new int[groups.Count * Views.Length];
        var lost = new int[original.Length];
        for (int pixel = 0; pixel < maskSize; pixel++)
        {
            if (counts[pixel] == 0) continue;
            original[pixel / PixelsPerView]++;
            int x = pixel % Resolution, y = pixel % PixelsPerView / Resolution;
            boundary[pixel] = x == 0 || y == 0 || x == Resolution - 1 || y == Resolution - 1
                || counts[pixel - 1] == 0 || counts[pixel + 1] == 0
                || counts[pixel - Resolution] == 0 || counts[pixel + Resolution] == 0;
        }
        var center = (positions.Aggregate(Vector3.Min) + positions.Aggregate(Vector3.Max)) / 2;
        var extremes = new HashSet<Card>();
        for (int axis = 0; axis < 3; axis++)
            foreach (float extreme in new[] { positions.Min(p => p[axis]), positions.Max(p => p[axis]) })
            {
                var support = cards.FirstOrDefault(c => c.Triangles.Any(t => Enumerable.Range(0, 3)
                    .Any(k => mesh.Parts[c.Part].Positions[mesh.Parts[c.Part].Triangles[t * 3 + k]][axis] == extreme)));
                if (support is not null) extremes.Add(support);
            }
        // A subpixel card has no measurable coverage. Retain it instead of treating it as invisible.
        var candidates = cards.Where(c => c.Removable && c.Pixels.Length > 0 && !extremes.Contains(c))
            .OrderBy(c => c.Pixels.Count(p => counts[p] == 1))
            .ThenBy(c => Vector3.DistanceSquared(c.Center, center)).ThenBy(c => c.Part).ThenBy(c => c.Triangles[0]).ToArray();
        int removed = 0;
        var loss = new int[original.Length];
        foreach (var card in candidates)
        {
            RunLimits.Check();
            if (triangles - removed <= targetTriangles) break;
            Array.Clear(loss);
            bool allowed = true;
            foreach (int pixel in card.Pixels)
            {
                if (counts[pixel] != 1) continue;
                int bucket = pixel / PixelsPerView;
                if (boundary[pixel] || lost[bucket] + ++loss[bucket] > MathF.Floor(original[bucket] * maxCoverageLoss))
                { allowed = false; break; }
            }
            if (!allowed) continue;
            card.Removed = true; removed += card.Triangles.Length;
            foreach (int pixel in card.Pixels) if (--counts[pixel] == 0) lost[pixel / PixelsPerView]++;
        }
        if (removed == 0) return new(mesh, 0, 0, cards.Count, "Coverage constraints retain the available foliage.");
        var parts = new List<LodMeshPart>();
        for (int i = 0; i < mesh.Parts.Count; i++)
        {
            var omit = cards.Where(c => c.Part == i && c.Removed).SelectMany(c => c.Triangles).ToHashSet();
            if (omit.Count == 0) { parts.Add(mesh.Parts[i]); continue; }
            var part = Compact(mesh.Parts[i], omit);
            if (part.TriangleCount > 0) parts.Add(part);
        }
        float maximumLoss = Enumerable.Range(0, original.Length).Max(i => original[i] == 0 ? 0 : lost[i] / (float)original[i]);
        return new(new() { Path = mesh.Path, Parts = parts, Warnings = mesh.Warnings }, removed, maximumLoss, cards.Count);
    }

    private static IEnumerable<Card> Components(LodMeshPart part, int partIndex)
    {
        var roots = Enumerable.Range(0, part.VertexCount).ToArray();
        int Find(int v) { while (roots[v] != v) { roots[v] = roots[roots[v]]; v = roots[v]; } return v; }
        for (int t = 0; t < part.TriangleCount; t++)
        {
            int a = Find(part.Triangles[t * 3]);
            roots[Find(part.Triangles[t * 3 + 1])] = a; roots[Find(part.Triangles[t * 3 + 2])] = a;
        }
        foreach (var group in Enumerable.Range(0, part.TriangleCount).GroupBy(t => Find(part.Triangles[t * 3])))
        {
            RunLimits.Check();
            int[] triangles = group.ToArray();
            var vertices = triangles.SelectMany(t => part.Triangles.AsSpan(t * 3, 3).ToArray()).Distinct().ToArray();
            var center = vertices.Aggregate(Vector3.Zero, (sum, i) => sum + part.Positions[i]) / vertices.Length;
            bool planar = triangles.Length <= 32 && vertices.Length <= 64;
            Vector3 normal = Vector3.Zero;
            foreach (int t in triangles)
            {
                var n = Vector3.Cross(part.Positions[part.Triangles[t * 3 + 1]] - part.Positions[part.Triangles[t * 3]],
                    part.Positions[part.Triangles[t * 3 + 2]] - part.Positions[part.Triangles[t * 3]]);
                if (n.LengthSquared() > 1e-8f) { normal = Vector3.Normalize(n); break; }
            }
            float radius = vertices.Max(i => Vector3.Distance(part.Positions[i], center));
            planar &= normal.LengthSquared() > .5f && vertices.All(i => MathF.Abs(Vector3.Dot(part.Positions[i] - center, normal)) <= MathF.Max(.001f, radius * .005f));
            yield return new(partIndex, triangles, center, planar);
        }
    }

    private static Projection Project(Vector3[] positions, Vector3 view)
    {
        var right = Vector3.Normalize(Vector3.Cross(MathF.Abs(view.Z) > .99f ? Vector3.UnitY : Vector3.UnitZ, view));
        var up = Vector3.Cross(view, right);
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        foreach (var p in positions) { var v = new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up)); min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
        var span = Vector2.Max(max - min, Vector2.One);
        return new(right, up, min, new Vector2(Resolution - 3) / span);
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p) => (p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X);

    private static LodMeshPart Compact(LodMeshPart part, HashSet<int> omitted)
    {
        var indices = Enumerable.Range(0, part.TriangleCount).Where(t => !omitted.Contains(t))
            .SelectMany(t => part.Triangles.AsSpan(t * 3, 3).ToArray()).ToArray();
        var keep = indices.Distinct().Order().ToArray();
        var map = keep.Select((old, index) => (old, index)).ToDictionary(p => p.old, p => (ushort)p.index);
        T[] Select<T>(T[] items) => keep.Select(i => items[i]).ToArray();
        return new() { Name = part.Name, TexGenScale = part.TexGenScale, Tier = part.Tier, Material = part.Material,
            Positions = Select(part.Positions), UVs = Select(part.UVs), Normals = Select(part.Normals),
            Tangents = Select(part.Tangents), Bitangents = Select(part.Bitangents), Colors = part.Colors is null ? null : Select(part.Colors),
            Triangles = indices.Select(i => map[i]).ToArray() };
    }
}
