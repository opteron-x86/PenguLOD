using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PenguLOD.Core.Pipeline;
using PenguLOD.Core.World;
using PenguLOD.Meshes;
using PenguLOD.Textures;
using PenguLOD.Textures.Dds;

namespace PenguLOD.Plugins;

/// <summary>Packs shader-compatible object surfaces; unsupported materials retain their original sampling.</summary>
public static class ObjectAtlasStage
{
    public const int Version = 8; // cutouts on shapes without an alpha property keep their alpha
    public const int PageSize = 4096;
    public const string LayoutFileName = "PenguLOD.atlas-layout.json";

    public sealed record Report(int PackedMaterials, int Pages, int EligibleParts, int SkippedParts,
        IReadOnlyDictionary<string, int> Reasons, int SourceMaterials = 0, int AtlasMaterials = 0, long TextureBytes = 0,
        IReadOnlyList<string>? Warnings = null, int CutParts = 0, int CutTriangles = 0, int ReusedPages = 0);
    public sealed record Mapping(string Diffuse, string Normal, AtlasRect Rect, IReadOnlyDictionary<int, string>? Extra = null);
    public sealed record Result(Report Report, Func<LodMesh, LodMesh> Transform, IReadOnlyDictionary<string, Mapping>? Mappings = null);

    private sealed record Candidate(LodMaterial Material, string Group, string SourceHash);
    private sealed record SourceInfo(DdsFile.Info Info, string Hash);
    private sealed record TileCacheEntry((LodMeshPart Part, Vector2[] Shifts)? Value);
    private static readonly ConditionalWeakTable<LodMeshPart, TileCacheEntry> TileCache = new();
    private static readonly int[] ExtraSlots = [2, 3, 5, 7, 8];

    private sealed record LayoutCache(int Version, int CellSize, int MaxPageSize, Dictionary<string, CachedGroup> Groups);
    private sealed record CachedGroup(int Epoch, List<CachedPage> Pages);
    private sealed record CachedPage(string Stem, int Width, int Height, int[] ExtraSlots, List<CachedEntry> Entries);
    private sealed record CachedEntry(string Key, string SourceHash, int Ox, int Oy, int OuterW, int OuterH, int InnerW, int InnerH, int Gutter);

    public static string? Ineligible(LodMeshPart part)
    {
        var m = part.Material;
        if (m.Passthru is not null || m.RenderEffect is not null) return "passthrough shader";
        if (m.Textures.Count == 0 || string.IsNullOrWhiteSpace(m.Textures[0])) return "no diffuse texture";
        if (m.Textures.Count > 9 || !string.IsNullOrWhiteSpace(m.Textures.ElementAtOrDefault(6))) return "unsupported shader channels";
        // Projected UVs, parallax displacement, refraction and multilayer sampling are not affine UV lookups.
        if ((m.ShaderFlags1 & ((1u << 11) | (1u << 15) | (1u << 16) | (1u << 23) | (1u << 28))) != 0
            || (m.ShaderFlags2 & (1u << 24)) != 0) return "non-affine shader sampling";
        if (m.HasAlpha && ObjectTextureAssets.AlphaCutoff(m) is null) return "unsupported alpha blend/test";
        if (CachedTiles(part) is not { } tiled || FoldedVertices(tiled.Part, tiled.Shifts) > ushort.MaxValue) return "tiling or invalid UVs";
        if (tiled.Part.TriangleCount - part.TriangleCount > MaxCutTriangles(part.TriangleCount)) return "cutting at tile edges adds too many triangles";
        return null;
    }

    /// <summary>Triangles that cutting at tile edges adds to a part; 0 when it needs no cut or its coordinates cannot be packed.</summary>
    public static int CutTriangles(LodMeshPart part) => CachedTiles(part) is { } tiled ? tiled.Part.TriangleCount - part.TriangleCount : 0;

    private static (LodMeshPart Part, Vector2[] Shifts)? CachedTiles(LodMeshPart part)
        => TileCache.GetValue(part, static p => new TileCacheEntry(Tiles(p))).Value;

    /// <summary>How far a triangle may reach past its tile, in tiles: the rounding of half-precision texture coordinates near a tile edge.</summary>
    private const float FoldTolerance = 0.002f;
    public const int MaxTilesPerTriangle = 64;
    public static int MaxCutTriangles(int triangles) => Math.Max(64, triangles / 10);

    private static (LodMeshPart Part, Vector2[] Shifts)? Tiles(LodMeshPart part)
    {
        var uvs = part.UVs; var triangles = part.Triangles;
        if (part.Material.ClampMode > 3 || uvs.Length != part.Positions.Length || uvs.Length == 0 || triangles.Length % 3 != 0) return null;
        bool wrapU = (part.Material.ClampMode & 2) != 0, wrapV = (part.Material.ClampMode & 1) != 0;
        var shifts = new List<Vector2>(triangles.Length / 3);
        Cutter? cutter = null;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            if (a >= uvs.Length || b >= uvs.Length || c >= uvs.Length) return null;
            var min = Vector2.Min(uvs[a], Vector2.Min(uvs[b], uvs[c])); var max = Vector2.Max(uvs[a], Vector2.Max(uvs[b], uvs[c]));
            if (!float.IsFinite(min.X) || !float.IsFinite(min.Y) || !float.IsFinite(max.X) || !float.IsFinite(max.Y)) return null;
            var first = new Vector2(wrapU ? MathF.Floor(min.X + FoldTolerance) : 0, wrapV ? MathF.Floor(min.Y + FoldTolerance) : 0);
            if (min.X - first.X < -FoldTolerance || min.Y - first.Y < -FoldTolerance) return null;
            float tilesU = MathF.Max(1, MathF.Ceiling(max.X - FoldTolerance) - first.X), tilesV = MathF.Max(1, MathF.Ceiling(max.Y - FoldTolerance) - first.Y);
            if ((!wrapU && tilesU > 1) || (!wrapV && tilesV > 1) || tilesU * tilesV > MaxTilesPerTriangle) return null;
            if (tilesU == 1 && tilesV == 1)
            {
                shifts.Add(first); cutter?.Keep(a, b, c);
                continue;
            }
            cutter ??= new Cutter(part, t);
            cutter.Cut(a, b, c, first, (int)tilesU, (int)tilesV, shifts);
        }
        if (cutter is null) return (part, shifts.ToArray());
        return cutter.VertexCount > ushort.MaxValue ? null : (cutter.Build(), shifts.ToArray());
    }

    private sealed class Cutter
    {
        private readonly LodMeshPart _part;
        private readonly List<Vector3> _positions, _normals, _tangents, _bitangents;
        private readonly List<Vector2> _uvs;
        private readonly List<uint>? _colors;
        private readonly List<int> _triangles;
        private readonly Dictionary<(int Low, int High, int Axis, float Line), int> _cuts = [];
        private List<int> _polygon = [], _below = [], _above = [], _strip = [];

        public Cutter(LodMeshPart part, int kept)
        {
            _part = part;
            _positions = [.. part.Positions]; _uvs = [.. part.UVs]; _normals = [.. part.Normals]; _tangents = [.. part.Tangents];
            _bitangents = [.. part.Bitangents]; _colors = part.Colors is null ? null : [.. part.Colors];
            _triangles = new List<int>(part.Triangles.Length + 64);
            for (int i = 0; i < kept; i++) _triangles.Add(part.Triangles[i]);
        }

        public int VertexCount => _positions.Count;
        public void Keep(int a, int b, int c) { _triangles.Add(a); _triangles.Add(b); _triangles.Add(c); }

        public void Cut(int a, int b, int c, Vector2 first, int tilesU, int tilesV, List<Vector2> shifts)
        {
            _polygon.Clear(); _polygon.Add(a); _polygon.Add(b); _polygon.Add(c);
            for (int u = 0; u < tilesU; u++)
            {
                if (u < tilesU - 1) { Split(_polygon, 0, first.X + u + 1); (_strip, _below) = (_below, _strip); (_polygon, _above) = (_above, _polygon); }
                else (_strip, _polygon) = (_polygon, _strip);
                for (int v = 0; v < tilesV; v++)
                {
                    List<int> piece;
                    if (v < tilesV - 1) { Split(_strip, 1, first.Y + v + 1); piece = _below; (_strip, _above) = (_above, _strip); }
                    else piece = _strip;
                    var shift = new Vector2(first.X + u, first.Y + v);
                    for (int i = 1; i + 1 < piece.Count; i++)
                    {
                        int p = piece[0], q = piece[i], r = piece[i + 1];
                        if (p == q || q == r || p == r) continue;
                        Keep(p, q, r); shifts.Add(shift);
                    }
                }
            }
        }

        private void Split(List<int> polygon, int axis, float line)
        {
            _below.Clear(); _above.Clear();
            for (int i = 0; i < polygon.Count; i++)
            {
                int p = polygon[i], q = polygon[(i + 1) % polygon.Count];
                int sideP = Side(p, axis, line), sideQ = Side(q, axis, line);
                if (sideP <= 0) _below.Add(p);
                if (sideP >= 0) _above.Add(p);
                if (sideP * sideQ < 0) { int cut = CutVertex(p, q, axis, line); _below.Add(cut); _above.Add(cut); }
            }
        }

        private int Side(int vertex, int axis, float line)
        {
            float d = (axis == 0 ? _uvs[vertex].X : _uvs[vertex].Y) - line;
            return d < -FoldTolerance ? -1 : d > FoldTolerance ? 1 : 0;
        }

        private int CutVertex(int p, int q, int axis, float line)
        {
            var key = (Math.Min(p, q), Math.Max(p, q), axis, line);
            if (_cuts.TryGetValue(key, out int cut)) return cut;
            int low = key.Item1, high = key.Item2;
            float from = axis == 0 ? _uvs[low].X : _uvs[low].Y, to = axis == 0 ? _uvs[high].X : _uvs[high].Y;
            float t = (line - from) / (to - from);
            var uv = Vector2.Lerp(_uvs[low], _uvs[high], t);
            _uvs.Add(axis == 0 ? new Vector2(line, uv.Y) : new Vector2(uv.X, line));
            _positions.Add(Vector3.Lerp(_positions[low], _positions[high], t));
            _normals.Add(Direction(_normals[low], _normals[high], t)); _tangents.Add(Direction(_tangents[low], _tangents[high], t));
            _bitangents.Add(Direction(_bitangents[low], _bitangents[high], t));
            _colors?.Add(Color(_colors[low], _colors[high], t));
            return _cuts[key] = _positions.Count - 1;
        }

        private static Vector3 Direction(Vector3 a, Vector3 b, float t)
        {
            var mixed = Vector3.Lerp(a, b, t); float length = mixed.Length();
            return length > 1e-6f ? mixed / length : a;
        }

        private static uint Color(uint a, uint b, float t)
        {
            uint mixed = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                float x = (a >> shift) & 0xFF, y = (b >> shift) & 0xFF;
                mixed |= (uint)Math.Clamp(MathF.Round(x + (y - x) * t), 0, 255) << shift;
            }
            return mixed;
        }

        public LodMeshPart Build() => _part with
        {
            Positions = [.. _positions], UVs = [.. _uvs], Normals = [.. _normals], Tangents = [.. _tangents], Bitangents = [.. _bitangents],
            Colors = _colors is null ? null : [.. _colors], Triangles = _triangles.Select(i => (ushort)i).ToArray(),
        };
    }

    private static int FoldedVertices(LodMeshPart part, Vector2[] shifts)
    {
        if (Uniform(shifts)) return part.VertexCount;
        var seen = new HashSet<(int, float, float)>();
        for (int i = 0; i < part.Triangles.Length; i++) seen.Add((part.Triangles[i], shifts[i / 3].X, shifts[i / 3].Y));
        return seen.Count;
    }

    private static bool Uniform(Vector2[] shifts)
    {
        for (int i = 1; i < shifts.Length; i++) if (shifts[i] != shifts[0]) return false;
        return true;
    }

    private static LodMeshPart Fold(LodMeshPart p, LodMaterial material, Vector2[] shifts, Func<Vector2, Vector2> place)
    {
        if (Uniform(shifts))
        {
            var shift = shifts.Length == 0 ? Vector2.Zero : shifts[0];
            return ObjectTextureAssets.With(p, material, p.UVs.Select(v => place(v - shift)).ToArray());
        }
        var index = new Dictionary<(int, float, float), ushort>();
        var source = new List<int>(p.VertexCount); var uvs = new List<Vector2>(p.VertexCount);
        var triangles = new ushort[p.Triangles.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            int vertex = p.Triangles[i]; var shift = shifts[i / 3];
            if (!index.TryGetValue((vertex, shift.X, shift.Y), out ushort folded))
            {
                index[(vertex, shift.X, shift.Y)] = folded = (ushort)source.Count;
                source.Add(vertex); uvs.Add(place(p.UVs[vertex] - shift));
            }
            triangles[i] = folded;
        }
        T[] Pick<T>(T[] values) => source.Select(v => values[v]).ToArray();
        return p with
        {
            Material = material, Positions = Pick(p.Positions), UVs = [.. uvs], Normals = Pick(p.Normals), Tangents = Pick(p.Tangents),
            Bitangents = Pick(p.Bitangents), Colors = p.Colors is null ? null : Pick(p.Colors), Triangles = triangles,
        };
    }

    public static Result Build(IEnumerable<LodMesh> meshes, AssetIndex assets, string output, int cellSize = 256,
        IProgress<string>? progress = null, CancellationToken ct = default, IReadOnlyDictionary<string, ulong[]>? usage = null,
        int maxPageSize = PageSize)
    {
        if (cellSize is < 64 or > 512 || (cellSize & (cellSize - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        if (maxPageSize is < 256 or > 8192 || (maxPageSize & (maxPageSize - 1)) != 0 || maxPageSize < cellSize)
            throw new ArgumentOutOfRangeException(nameof(maxPageSize));
        Directory.CreateDirectory(output);
        var source = new ObjectTextureAssets(assets, output);
        var materials = new SortedDictionary<string, Candidate>(StringComparer.Ordinal);
        var info = new Dictionary<string, SourceInfo>(StringComparer.OrdinalIgnoreCase);
        var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reasons = new Dictionary<string, int>(); var warnings = new List<string>();
        int eligible = 0, skipped = 0, cutParts = 0, cutTriangles = 0;
        var modelArray = meshes.ToArray();
        // Pre-validate unique candidate textures in parallel (header read, full decode check, SHA-256)
        // instead of decoding multi-megabyte DDS files sequentially on one core.
        var candidateSlots = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mesh in modelArray) foreach (var part in mesh.Parts)
        {
            if (Ineligible(part) is not null) continue;
            var slots = ShaderGuard.TextureSlots(part.Material.Textures);
            foreach (int slot in new[] { 0, 1 }.Concat(ExtraSlots))
            {
                string path = slots[slot]; if (path.Length == 0) continue;
                if (!candidateSlots.TryGetValue(path, out var set)) candidateSlots[path] = set = [];
                set.Add(slot);
            }
        }
        var parallelInfo = new ConcurrentDictionary<string, SourceInfo>(StringComparer.OrdinalIgnoreCase);
        var parallelFailures = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(candidateSlots, RunLimits.Parallel(ct), kv =>
        {
            RunLimits.Check();
            string path = kv.Key;
            try
            {
                var bytes = source.Read(path); var header = LodTextureEncoding.Read2D(bytes);
                if (header.DxgiFormat is 27 or 70 or 73 or 76 or 79 or 81 or 82 or 84 or 90 or 97
                    || (header.Format == PenguLOD.Textures.Bc.BcFormat.Bc5 && kv.Value.Any(s => s != 1)))
                    throw new NotSupportedException("Signed, typeless or two-channel data cannot be atlased faithfully.");
                _ = LodTextureEncoding.Decode(bytes);
                parallelInfo[path] = new(header, Convert.ToHexString(SHA256.HashData(bytes)));
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidDataException)
            { parallelFailures[path] = ex.Message; }
        });
        foreach (var (k, v) in parallelInfo) info[k] = v;
        foreach (var (k, v) in parallelFailures.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        { failures[k] = v; warnings.Add(k + ": " + v); }

        foreach (var mesh in modelArray) foreach (var part in mesh.Parts)
        {
            ct.ThrowIfCancellationRequested(); RunLimits.Check();
            string? reason = Ineligible(part);
            var m = part.Material;
            if (reason is null)
            {
                try
                {
                    var slots = ShaderGuard.TextureSlots(m.Textures);
                    foreach (int slot in new[] { 0, 1 }.Concat(ExtraSlots))
                    {
                        string path = slots[slot]; if (path.Length == 0) continue;
                        if (failures.TryGetValue(path, out var failure)) throw new InvalidDataException(failure);
                        if (info.ContainsKey(path)) continue;
                        try
                        {
                            var bytes = source.Read(path); var header = LodTextureEncoding.Read2D(bytes);
                            if (header.DxgiFormat is 27 or 70 or 73 or 76 or 79 or 81 or 82 or 84 or 90 or 97
                                || (header.Format == PenguLOD.Textures.Bc.BcFormat.Bc5 && slot != 1))
                                throw new NotSupportedException("Signed, typeless or two-channel data cannot be atlased faithfully.");
                            _ = LodTextureEncoding.Decode(bytes);
                            info[path] = new(header, Convert.ToHexString(SHA256.HashData(bytes)));
                        }
                        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidDataException)
                        { failures[path] = ex.Message; warnings.Add(path + ": " + ex.Message); throw; }
                    }
                    var groupSlots = slots.Select((p, i) => i == 4 ? p : i < 2 || p.Length > 0 ? $"slot{i}" : "").ToArray();
                    string group = (m with { Textures = groupSlots, ClampMode = 0 }).Key
                        + "|lod:" + ObjectTextureAssets.IsLodTexture(m)
                        + "|srgb:" + string.Join(',', slots.Select((p, i) => i != 4 && p.Length > 0 && LodTextureEncoding.IsSrgb(info[p].Info) ? i.ToString() : ""));
                    string srcHash = string.Join(':', slots.Where((p, i) => i != 4 && p.Length > 0).Select(p => info[p].Hash));
                    materials[m.Key] = new(m, group, srcHash);
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidDataException)
                { reason = "unavailable or unsupported texture"; }
            }
            if (reason is null)
            {
                eligible++;
                if (CutTriangles(part) is > 0 and var added) { cutParts++; cutTriangles += added; }
            }
            else { skipped++; reasons[reason] = reasons.GetValueOrDefault(reason) + 1; }
        }

        string layoutPath = Path.Combine(output, LayoutFileName);
        var previousLayout = ReadLayout(layoutPath, cellSize, maxPageSize);
        var nextGroups = new Dictionary<string, CachedGroup>(StringComparer.Ordinal);
        var mapped = new Dictionary<string, Mapping>(StringComparer.Ordinal);
        int pages = 0, reusedPages = 0; long textureBytes = 0;
        int perPage = Math.Max(16, (maxPageSize / cellSize) * (maxPageSize / cellSize) * 3); // variable-aspect packs ~2-4x more rects per page

        ObjectTextureAtlas.Input LoadInput(string key, Candidate candidate)
        {
            var m = candidate.Material;
            var slots = ShaderGuard.TextureSlots(m.Textures);
            bool alpha = source.KeepsAlpha(m, slots[0]);
            int? cutoff = source.CoverageCutoff(m, slots[0]);
            TextureImage Load(string path, int slot) => LodTextureEncoding.Limit(LodTextureEncoding.Decode(source.Read(path)), cellSize,
                slot == 0 ? alpha ? LodTextureEncoding.Channels.Color : LodTextureEncoding.Channels.OpaqueColor : slot == 1 ? LodTextureEncoding.Channels.Normal : LodTextureEncoding.Channels.Data,
                LodTextureEncoding.IsSrgb(info[path].Info), slot == 0 ? cutoff : null);
            return new(key, Load(slots[0], 0), slots[1].Length > 0 ? Load(slots[1], 1) : null, alpha, m.ClampMode,
                cutoff ?? 128,
                ExtraSlots.Where(i => slots[i].Length > 0).ToDictionary(i => i, i => Load(slots[i], i)),
                slots.Select((p, i) => (p, i)).Where(t => t.i != 4 && t.p.Length > 0 && LodTextureEncoding.IsSrgb(info[t.p].Info)).Select(t => t.i).ToHashSet());
        }

        bool PageFilesExist(CachedPage cp)
        {
            if (!File.Exists(GamePath.ToFileSystemPath(output, cp.Stem + ".dds"))) return false;
            if (!File.Exists(GamePath.ToFileSystemPath(output, cp.Stem + "_n.dds"))) return false;
            foreach (int slot in cp.ExtraSlots)
                if (!File.Exists(GamePath.ToFileSystemPath(output, cp.Stem + $"_s{slot}.dds"))) return false;
            return true;
        }

        long ExistingPageBytes(CachedPage cp)
        {
            long sum = new FileInfo(GamePath.ToFileSystemPath(output, cp.Stem + ".dds")).Length
                     + new FileInfo(GamePath.ToFileSystemPath(output, cp.Stem + "_n.dds")).Length;
            foreach (int slot in cp.ExtraSlots)
                sum += new FileInfo(GamePath.ToFileSystemPath(output, cp.Stem + $"_s{slot}.dds")).Length;
            return sum;
        }

        foreach (var group in materials.GroupBy(kv => kv.Value.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Object atlas: {mapped.Count:N0}/{materials.Count:N0} materials...");
            var groupDict = group.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var firstMat = group.First().Value.Material;
            int[] activeExtraSlots = ExtraSlots.Where(i => ShaderGuard.TextureSlots(firstMat.Textures)[i].Length > 0).ToArray();
            string groupHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"atlas{Version}:{cellSize}:{maxPageSize}:{group.Key}"))).ToLowerInvariant()[..20];

            // Check incremental layout cache for this group: reuse when <=25% stale area and files exist on disk.
            if (previousLayout is not null && previousLayout.Groups.TryGetValue(group.Key, out var cachedGroup)
                && cachedGroup.Pages.Count > 0 && cachedGroup.Pages.All(PageFilesExist))
            {
                long totalArea = 0, staleArea = 0;
                foreach (var cp in cachedGroup.Pages)
                    foreach (var ce in cp.Entries)
                    {
                        long area = (long)ce.OuterW * ce.OuterH;
                        totalArea += area;
                        if (!groupDict.TryGetValue(ce.Key, out var cur) || cur.SourceHash != ce.SourceHash)
                            staleArea += area;
                    }

                if (totalArea > 0 && staleArea * 4 <= totalArea)
                {
                    var pinnedSpecs = cachedGroup.Pages.Select(cp => (
                        cp.Width,
                        cp.Height,
                        Pinned: (IReadOnlyList<(string Key, int Ox, int Oy, int OuterW, int OuterH, int InnerW, int InnerH, int Gutter)>)
                            cp.Entries.Where(ce => groupDict.TryGetValue(ce.Key, out var cur) && cur.SourceHash == ce.SourceHash)
                                .Select(ce => (ce.Key, ce.Ox, ce.Oy, ce.OuterW, ce.OuterH, ce.InnerW, ce.InnerH, ce.Gutter)).ToList()
                    )).ToList();

                    var pinnedKeys = pinnedSpecs.SelectMany(p => p.Pinned.Select(x => x.Key)).ToHashSet(StringComparer.Ordinal);
                    bool anyNewOrChanged = groupDict.Keys.Any(k => !pinnedKeys.Contains(k));

                    if (!anyNewOrChanged)
                    {
                        // Fast path: 0 new/changed materials in this group -> reuse every surviving slot without decoding or encoding!
                        var keptPages = new List<CachedPage>();
                        foreach (var cp in cachedGroup.Pages)
                        {
                            var surviving = cp.Entries.Where(ce => pinnedKeys.Contains(ce.Key)).ToList();
                            if (surviving.Count == 0) continue;
                            string diffuse = cp.Stem + ".dds", normal = cp.Stem + "_n.dds";
                            var extraMap = cp.ExtraSlots.ToDictionary(s => s, s => cp.Stem + $"_s{s}.dds");
                            foreach (var ce in surviving)
                            {
                                var rect = new AtlasRect(
                                    (ce.Ox + ce.Gutter + 0.5f) / cp.Width,
                                    (ce.Oy + ce.Gutter + 0.5f) / cp.Height,
                                    (ce.Ox + ce.OuterW - ce.Gutter - 0.5f) / cp.Width,
                                    (ce.Oy + ce.OuterH - ce.Gutter - 0.5f) / cp.Height);
                                mapped[ce.Key] = new(diffuse, normal, rect, extraMap);
                            }
                            keptPages.Add(cp with { Entries = surviving });
                            pages++; reusedPages++;
                            textureBytes += ExistingPageBytes(cp);
                        }
                        nextGroups[group.Key] = new(cachedGroup.Epoch, keptPages);
                        continue;
                    }

                    // Append new/changed materials into free Skyline slots of pinned pages or overflow pages, preserving pinned rects & stems!
                    var allInputs = groupDict.Select(kv => LoadInput(kv.Key, kv.Value)).ToArray();
                    var incremental = ObjectTextureAtlas.Build(allInputs, cellSize, maxPageSize, ct, variableAspect: true, pinnedPages: pinnedSpecs);
                    var updatedPages = new List<CachedPage>(incremental.Pages.Count);
                    for (int page = 0; page < incremental.Pages.Count; page++)
                    {
                        string pageStem = page < cachedGroup.Pages.Count
                            ? cachedGroup.Pages[page].Stem
                            : ObjectTextureAssets.Root(firstMat, "objects") + $"{groupHash}_e{cachedGroup.Epoch}_{page}";
                        var pageBoxes = incremental.Boxes!.Where(kv => kv.Value.Page == page)
                            .Select(kv => new CachedEntry(kv.Key, groupDict[kv.Key].SourceHash, kv.Value.Ox, kv.Value.Oy, kv.Value.OuterW, kv.Value.OuterH, kv.Value.InnerW, kv.Value.InnerH, kv.Value.Gutter))
                            .OrderBy(e => e.Key, StringComparer.Ordinal).ToList();

                        // If an existing page gained no new entries and none of its entries changed, keep the existing DDS on disk untouched.
                        bool unchangedExisting = page < cachedGroup.Pages.Count
                            && pageBoxes.All(pb => pinnedKeys.Contains(pb.Key));
                        string diffuse = pageStem + ".dds", normal = pageStem + "_n.dds";
                        Dictionary<int, string> extraMap;
                        if (unchangedExisting)
                        {
                            extraMap = cachedGroup.Pages[page].ExtraSlots.ToDictionary(s => s, s => pageStem + $"_s{s}.dds");
                            pages++; reusedPages++;
                            textureBytes += ExistingPageBytes(cachedGroup.Pages[page]);
                        }
                        else
                        {
                            Write(diffuse, incremental.Pages[page].Diffuse);
                            Write(normal, incremental.Pages[page].Normal);
                            pages++;
                            extraMap = (incremental.Pages[page].Extra ?? new Dictionary<int, byte[]>()).ToDictionary(k => k.Key, k => pageStem + $"_s{k.Key}.dds");
                            foreach (var (slot, path) in extraMap) Write(path, incremental.Pages[page].Extra![slot]);
                        }
                        foreach (var (key, placement) in incremental.Placements.Where(p => p.Value.Page == page))
                            mapped[key] = new(diffuse, normal, placement.Rect, extraMap);
                        updatedPages.Add(new(pageStem, incremental.Pages[page].Size, incremental.Pages[page].Height, activeExtraSlots, pageBoxes));
                    }
                    nextGroups[group.Key] = new(cachedGroup.Epoch, updatedPages);
                    continue;
                }
            }

            // Fresh pack (or repack when >25% stale): cluster by block usage and pack variable-aspect Skyline pages.
            int epoch = (previousLayout?.Groups.TryGetValue(group.Key, out var prevGrp) == true ? prevGrp.Epoch + 1 : 0);
            var freshPages = new List<CachedPage>();
            int batchPageOffset = 0;
            foreach (var batch in Batches(group.ToList(), perPage, usage))
            {
                ct.ThrowIfCancellationRequested();
                var inputs = batch.Select(kv => LoadInput(kv.Key, kv.Value)).ToList();
                var result = ObjectTextureAtlas.Build(inputs, cellSize, maxPageSize, ct, variableAspect: true);
                for (int page = 0; page < result.Pages.Count; page++)
                {
                    int globalPage = batchPageOffset + page;
                    string pageStem = ObjectTextureAssets.Root(batch[0].Value.Material, "objects") + $"{groupHash}_e{epoch}_{globalPage}";
                    string diffuse = pageStem + ".dds", normal = pageStem + "_n.dds";
                    Write(diffuse, result.Pages[page].Diffuse);
                    Write(normal, result.Pages[page].Normal);
                    pages++;
                    var extraMap = (result.Pages[page].Extra ?? new Dictionary<int, byte[]>()).ToDictionary(k => k.Key, k => pageStem + $"_s{k.Key}.dds");
                    foreach (var (slot, path) in extraMap) Write(path, result.Pages[page].Extra![slot]);
                    foreach (var (key, placement) in result.Placements.Where(p => p.Value.Page == page))
                        mapped[key] = new(diffuse, normal, placement.Rect, extraMap);
                    var pageBoxes = result.Boxes!.Where(kv => kv.Value.Page == page)
                        .Select(kv => new CachedEntry(kv.Key, groupDict[kv.Key].SourceHash, kv.Value.Ox, kv.Value.Oy, kv.Value.OuterW, kv.Value.OuterH, kv.Value.InnerW, kv.Value.InnerH, kv.Value.Gutter))
                        .OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
                    freshPages.Add(new(pageStem, result.Pages[page].Size, result.Pages[page].Height, activeExtraSlots, pageBoxes));
                }
                batchPageOffset += result.Pages.Count;
            }
            nextGroups[group.Key] = new(epoch, freshPages);
        }

        File.WriteAllText(layoutPath, JsonSerializer.Serialize(new LayoutCache(Version, cellSize, maxPageSize, nextGroups), PersistentPipeline.Json));
        var cache = new ConcurrentDictionary<LodMesh, LodMesh>(ReferenceEqualityComparer.Instance);
        LodMesh Transform(LodMesh mesh) => cache.GetOrAdd(mesh, m => Remap(m, mapped));
        int before = modelArray.SelectMany(m => m.Parts).Select(p => p.Material.Key).Distinct().Count();
        int after = modelArray.Select(Transform).SelectMany(m => m.Parts).Select(p => p.Material.Key).Distinct().Count();
        var report = new Report(mapped.Count, pages, eligible, skipped, reasons, before, after, textureBytes, warnings, cutParts, cutTriangles, reusedPages);
        File.WriteAllText(Path.Combine(output, "PenguLOD.atlas.json"), JsonSerializer.Serialize(report, PersistentPipeline.Json));
        var groupSizes = materials.Values.GroupBy(c => c.Group).Select(g => g.Count()).OrderDescending().ToArray();
        progress?.Report($"Object atlas: {mapped.Count:N0} materials on {pages} page sets ({reusedPages} reused); {before:N0} → {after:N0} distinct materials; {skipped:N0} parts retain original UVs/materials.");
        if (cutParts > 0) progress?.Report($"Object atlas: {cutParts:N0} model parts were cut at texture tile edges to fit, adding {cutTriangles:N0} triangles to those models.");
        progress?.Report($"Object atlas: {groupSizes.Length:N0} groups of compatible materials; the largest hold {string.Join(", ", groupSizes.Take(8))} materials.");
        foreach (var warning in warnings.Take(20)) progress?.Report("Warning: object atlas kept source material: " + warning);
        return new(report, Transform, mapped);

        void Write(string path, byte[] bytes) { ObjectTextureAssets.Write(output, path, bytes); textureBytes += bytes.Length; }
    }

    private static LayoutCache? ReadLayout(string path, int cellSize, int maxPageSize)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var layout = JsonSerializer.Deserialize<LayoutCache>(File.ReadAllText(path), PersistentPipeline.Json);
            return layout is not null && layout.Version == Version && layout.CellSize == cellSize && layout.MaxPageSize == maxPageSize ? layout : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    internal static IEnumerable<KeyValuePair<string, TCandidate>[]> Batches<TCandidate>(IReadOnlyList<KeyValuePair<string, TCandidate>> group, int perPage,
        IReadOnlyDictionary<string, ulong[]>? usage)
    {
        if (usage is null || group.Count <= perPage)
        {
            foreach (var chunk in group.Chunk(perPage)) yield return chunk;
            yield break;
        }
        int words = usage.Values.Select(u => u.Length).DefaultIfEmpty(0).Max();
        ulong[] Bits(string key) => usage.TryGetValue(key, out var u) ? u : [];
        static int Count(ulong[] a) { int n = 0; foreach (ulong w in a) n += BitOperations.PopCount(w); return n; }
        var left = group.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        while (left.Count > 0)
        {
            int seed = 0, seedCount = -1;
            for (int i = 0; i < left.Count; i++) { int c = Count(Bits(left[i].Key)); if (c > seedCount) { seed = i; seedCount = c; } }
            var page = new List<KeyValuePair<string, TCandidate>> { left[seed] };
            var covered = new ulong[words]; Or(covered, Bits(left[seed].Key));
            left.RemoveAt(seed);
            while (page.Count < perPage && left.Count > 0)
            {
                int best = 0, bestNew = int.MaxValue, bestShared = -1;
                for (int i = 0; i < left.Count; i++)
                {
                    var u = Bits(left[i].Key); int added = 0, shared = 0;
                    for (int w = 0; w < u.Length; w++)
                    {
                        added += BitOperations.PopCount(u[w] & ~covered[w]);
                        shared += BitOperations.PopCount(u[w] & covered[w]);
                    }
                    if (added < bestNew || (added == bestNew && shared > bestShared)) { best = i; bestNew = added; bestShared = shared; }
                }
                page.Add(left[best]); Or(covered, Bits(left[best].Key));
                left.RemoveAt(best);
            }
            yield return page.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToArray();
        }

        static void Or(ulong[] into, ulong[] bits) { for (int w = 0; w < bits.Length; w++) into[w] |= bits[w]; }
    }

    public static LodMesh Remap(LodMesh mesh, IReadOnlyDictionary<string, Mapping> mappings) => new()
    {
        Path = mesh.Path, Warnings = mesh.Warnings, NoShapesInFile = mesh.NoShapesInFile,
        Parts = mesh.Parts.Select(p =>
        {
            if (!mappings.TryGetValue(p.Material.Key, out var map) || Ineligible(p) is not null || CachedTiles(p) is not { } tiled) return p;
            var textures = ShaderGuard.TextureSlots(p.Material.Textures); textures[0] = map.Diffuse; textures[1] = map.Normal;
            if (map.Extra is not null) foreach (var (slot, path) in map.Extra) textures[slot] = path;
            return Fold(tiled.Part, p.Material with { Textures = textures, ClampMode = 0 }, tiled.Shifts,
                v => new Vector2(map.Rect.U0 + v.X * (map.Rect.U1 - map.Rect.U0), map.Rect.V0 + v.Y * (map.Rect.V1 - map.Rect.V0)));
        }).ToArray()
    };

    /// <summary>Compact deterministic stamp of a transformed mesh's resolved materials and atlas UV sub-rects, for <c>QuadHasher</c>.</summary>
    public static string TransformStamp(LodMesh original, LodMesh transformed, IReadOnlyDictionary<string, Mapping>? atlasMappings)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int i = 0; i < transformed.Parts.Count; i++)
        {
            var tp = transformed.Parts[i];
            sha.AppendData(Encoding.UTF8.GetBytes(tp.Material.Key));
            if (atlasMappings is not null && i < original.Parts.Count && atlasMappings.TryGetValue(original.Parts[i].Material.Key, out var m))
                sha.AppendData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"|{m.Rect.U0:R},{m.Rect.V0:R},{m.Rect.U1:R},{m.Rect.V1:R}")));
            sha.AppendData("\n"u8);
        }
        return Convert.ToHexString(sha.GetHashAndReset(), 0, 8);
    }
}
