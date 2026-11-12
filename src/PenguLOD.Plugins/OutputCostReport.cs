using System.Globalization;
using System.Text.Json;
using PenguLOD.Core.Pipeline;
using PenguLOD.Meshes.Nif;
using PenguLOD.Textures.Dds;

namespace PenguLOD.Plugins;

public sealed record BlockCost(string File, string World, int Level, string Season, long Bytes,
    IReadOnlyList<GeometryCost>? Layers, string? Unavailable = null)
{
    public long? Triangles => Layers?.Sum(l => l.Triangles);
    public long? MeshGroups => Layers?.Sum(l => (long)l.MeshGroups);
}
public sealed record TextureCost(string File, long Bytes, int? Width, int? Height, int? Mips, string? Format, string? Unavailable = null);
public sealed record CostGroup(string World, int Level, string Season, string Layer, long Triangles, long Vertices, long MeshGroups);
public sealed record CostDelta(string File, long Bytes, long? Triangles, long? MeshGroups);
public sealed record SettingChange(string Setting, string? Before, string? After);
public sealed record TextureDelta(string File, long Bytes, string Before, string After);
public sealed record CostComparison(IReadOnlyList<CostDelta> CommonBlocks, IReadOnlyList<string> AddedBlocks,
    IReadOnlyList<string> RemovedBlocks, IReadOnlyList<SettingChange> Settings, IReadOnlyList<string> Warnings,
    IReadOnlyList<TextureDelta> CommonTextures, IReadOnlyList<string> AddedTextures, IReadOnlyList<string> RemovedTextures,
    IReadOnlyList<CostGroup> CommonGroups)
{
    public string Summary() => $"{CommonBlocks.Count:N0} comparable blocks; {AddedBlocks.Count:N0} added, {RemovedBlocks.Count:N0} removed.\n"
        + $"Common-block changes: {CommonBlocks.Sum(b => b.Bytes):+0;-0;0} bytes, "
        + $"{CommonBlocks.Where(b => b.Triangles.HasValue).Sum(b => b.Triangles!.Value):+0;-0;0} triangles, "
        + $"{CommonBlocks.Where(b => b.MeshGroups.HasValue).Sum(b => b.MeshGroups!.Value):+0;-0;0} mesh groups (measured pairs only).\n"
        + $"Common textures: {CommonTextures.Sum(t => t.Bytes):+0;-0;0} bytes; {AddedTextures.Count:N0} added, {RemovedTextures.Count:N0} removed.\n"
        + $"{Settings.Count} changed settings.\n" + string.Join('\n', Warnings);
}

/// <summary>Measurements of a complete output folder, including retained blocks. No game data is changed.</summary>
public sealed class OutputCostReport
{
    public const string FileName = "PenguLOD.costs.json";
    public int Schema { get; set; } = 1;
    public string Version { get; set; } = PenguLOD.Core.ProductInfo.Version;
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public List<BlockCost> Blocks { get; set; } = [];
    public List<TextureCost> Textures { get; set; } = [];
    public SortedDictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);
    public List<string> Warnings { get; set; } = [];
    public IReadOnlyList<CostGroup> Groups => Blocks.Where(b => b.Layers is not null)
        .SelectMany(b => b.Layers!.Select(l => (b.World, b.Level, b.Season, Cost: l)))
        .GroupBy(x => (x.World, x.Level, x.Season, x.Cost.Layer))
        .Select(g => new CostGroup(g.Key.World, g.Key.Level, g.Key.Season, g.Key.Layer,
            g.Sum(x => x.Cost.Triangles), g.Sum(x => x.Cost.Vertices), g.Sum(x => (long)x.Cost.MeshGroups))).ToArray();
    public IReadOnlyList<BlockCost> LargestBlocks => Blocks.OrderByDescending(b => b.Layers?.Sum(l => l.Triangles) ?? -1)
        .ThenBy(b => b.File, StringComparer.Ordinal).Take(30).ToArray();

    public static OutputCostReport Analyze(string root, SortedDictionary<string, string>? settings = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        var result = new OutputCostReport { Settings = settings ?? new(StringComparer.Ordinal) };
        foreach (string relative in OutputFiles.Enumerate(root, ct))
        {
            ct.ThrowIfCancellationRequested();
            string file = OutputSnapshot.Resolve(root, relative), extension = Path.GetExtension(file).ToLowerInvariant();
            long bytes = new FileInfo(file).Length;
            if (extension is ".bto" or ".btr" or ".btt")
            {
                string[] parts = Path.GetFileNameWithoutExtension(file).Split('.');
                if (parts.Length < 4 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
                { result.Warnings.Add("Unrecognized block name: " + relative); continue; }
                IReadOnlyList<GeometryCost>? layers = null; string? unavailable = null;
                try
                {
                    if (extension == ".btt") unavailable = "Legacy tree instance file; geometry counts unavailable.";
                    else layers = NifCostReader.Read(File.ReadAllBytes(file), extension == ".btr" ? "terrain" : "objects");
                }
                catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or OverflowException or ArgumentException)
                { unavailable = e.Message; }
                result.Blocks.Add(new(relative, parts[0], level, parts.Length > 4 ? parts[4].ToUpperInvariant() : "base", bytes, layers, unavailable));
                if (unavailable is not null) result.Warnings.Add(relative + ": " + unavailable);
                if (result.Blocks.Count % 100 == 0) progress?.Report($"Measuring finished output: {result.Blocks.Count:N0} blocks...");
            }
            else if (extension == ".dds")
            {
                try
                {
                    using var stream = File.OpenRead(file); byte[] header = new byte[Math.Min(148, bytes)]; stream.ReadExactly(header);
                    var info = DdsFile.ReadInfo(header);
                    result.Textures.Add(new(relative, bytes, info.Width, info.Height, info.MipCount,
                        info.Uncompressed ? "Uncompressed" : info.Format.ToString()));
                }
                catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or ArgumentException)
                { result.Textures.Add(new(relative, bytes, null, null, null, null, e.Message)); }
            }
        }
        return result;
    }

    public string Summary() => $"{Blocks.Count:N0} blocks · {Blocks.Sum(b => b.Bytes) / 1048576.0:N1} MiB on disk\n"
        + $"{Groups.Sum(g => g.Triangles):N0} triangles · {Groups.Sum(g => g.MeshGroups):N0} drawable mesh groups in measured blocks\n"
        + $"{Textures.Count:N0} local textures · {Textures.Sum(t => t.Bytes) / 1048576.0:N1} MiB on disk\n"
        + $"{Blocks.Count(b => b.Layers is null):N0} blocks and {Textures.Count(t => t.Width is null):N0} texture headers could not be measured.\n"
        + "These are output costs, not FPS, draw-call or resident-memory measurements. Textures outside this folder are not counted.";

    public void Write(string path) => ReportFiles.Write(path, this);
    public static OutputCostReport Read(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("Schema", out _)
            || !json.RootElement.TryGetProperty("Blocks", out _) || !json.RootElement.TryGetProperty("Textures", out _))
            throw new InvalidDataException("This file has no output cost measurements. Analyze the saved output folder first.");
        var report = json.RootElement.Deserialize<OutputCostReport>(PersistentPipeline.Json)
            ?? throw new InvalidDataException("Empty cost report.");
        if (report.Schema != 1 || report.Blocks is null || report.Textures is null || report.Settings is null)
            throw new InvalidDataException("Unsupported cost report. Analyze the saved output folder to create a current report.");
        if (report.Blocks.Any(b => b is null || string.IsNullOrWhiteSpace(b.File) || b.Bytes < 0
                || b.Layers?.Any(l => l is null || l.Triangles < 0 || l.Vertices < 0 || l.MeshGroups < 0) == true)
            || report.Textures.Any(t => t is null || string.IsNullOrWhiteSpace(t.File) || t.Bytes < 0))
            throw new InvalidDataException("Invalid output cost measurements.");
        if (report.Blocks.GroupBy(b => b.File, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Duplicate blocks in cost report.");
        if (report.Textures.GroupBy(t => t.File, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Duplicate textures in cost report.");
        return report;
    }

    public static CostComparison Compare(OutputCostReport before, OutputCostReport after)
    {
        var a = before.Blocks.ToDictionary(b => b.File, StringComparer.OrdinalIgnoreCase);
        var b = after.Blocks.ToDictionary(b => b.File, StringComparer.OrdinalIgnoreCase);
        var common = a.Keys.Intersect(b.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var added = b.Keys.Except(a.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var removed = a.Keys.Except(b.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var warnings = new List<string>();
        if (added.Length > 0 || removed.Length > 0) warnings.Add("Different output scopes: totals are compared only for matching block paths, including season and level.");
        if (common.Any(k => a[k].Layers is null || b[k].Layers is null)) warnings.Add("Some common blocks have unavailable geometry; their geometry deltas are omitted.");
        if (common.Length == 0) warnings.Add("No common blocks; no geometry comparison is possible.");
        if (before.Settings.Count == 0 || after.Settings.Count == 0) warnings.Add("Saved generation settings are unavailable for one or both reports.");
        var ta = before.Textures.ToDictionary(t => t.File, StringComparer.OrdinalIgnoreCase);
        var tb = after.Textures.ToDictionary(t => t.File, StringComparer.OrdinalIgnoreCase);
        string Size(TextureCost t) => t.Width is null ? "Unavailable" : $"{t.Width}×{t.Height}, {t.Mips} mips, {t.Format}";
        var groupDeltas = common.Where(k => a[k].Layers is not null && b[k].Layers is not null)
            .SelectMany(k => a[k].Layers!.Select(l => new CostGroup(b[k].World, b[k].Level, b[k].Season, l.Layer, -l.Triangles, -l.Vertices, -l.MeshGroups))
                .Concat(b[k].Layers!.Select(l => new CostGroup(b[k].World, b[k].Level, b[k].Season, l.Layer, l.Triangles, l.Vertices, l.MeshGroups))))
            .GroupBy(g => (g.World, g.Level, g.Season, g.Layer))
            .Select(g => new CostGroup(g.Key.World, g.Key.Level, g.Key.Season, g.Key.Layer, g.Sum(x => x.Triangles), g.Sum(x => x.Vertices), g.Sum(x => x.MeshGroups))).ToArray();
        return new(common.Select(k => new CostDelta(k, b[k].Bytes - a[k].Bytes,
                a[k].Layers is null || b[k].Layers is null ? null : b[k].Layers!.Sum(l => l.Triangles) - a[k].Layers!.Sum(l => l.Triangles),
                a[k].Layers is null || b[k].Layers is null ? null : b[k].Layers!.Sum(l => (long)l.MeshGroups) - a[k].Layers!.Sum(l => (long)l.MeshGroups))).ToArray(),
            added, removed, before.Settings.Keys.Union(after.Settings.Keys).Order(StringComparer.Ordinal)
                .Where(k => before.Settings.GetValueOrDefault(k) != after.Settings.GetValueOrDefault(k))
                .Select(k => new SettingChange(k, before.Settings.GetValueOrDefault(k), after.Settings.GetValueOrDefault(k))).ToArray(), warnings,
            ta.Keys.Intersect(tb.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
                .Select(k => new TextureDelta(k, tb[k].Bytes - ta[k].Bytes, Size(ta[k]), Size(tb[k]))).ToArray(),
            tb.Keys.Except(ta.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray(),
            ta.Keys.Except(tb.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray(), groupDeltas);
    }

    public static SortedDictionary<string, string> GenerationSettings(ScanRequest request)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Add("LOD", request); Add("Scan", request.Scan);
        if (request.Scan.Tree3D is { } tree) Add("Tree", tree);
        if (request.AutomaticObjects is { } objects) Add("AutomaticObjects", objects);
        return result;
        void Add(string prefix, object value)
        {
            foreach (var p in value.GetType().GetProperties())
            {
                if (p.Name is nameof(ScanRequest.Generate) or nameof(ScanRequest.CleanOutput)) continue;
                object? v = p.GetValue(value);
                if (v is bool or int or float or double or decimal or Enum)
                    result[prefix + "." + p.Name] = Convert.ToString(v, CultureInfo.InvariantCulture)!;
            }
        }
    }
}

internal static class ReportFiles
{
    public static void Write<T>(string path, T value)
    {
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, PersistentPipeline.Json)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal static class OutputFiles
{
    /// <summary>
    /// Rejects an output folder that is itself a symbolic link: publication renames that folder, which would move the
    /// link rather than its target. Links in parent folders (for example ~/.steam/steam) are allowed, and links inside
    /// the folder are rejected while its files are enumerated.
    /// </summary>
    public static void RequireRealPath(string root)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Path.GetPathRoot(full) == full) return;
        if (new DirectoryInfo(full).LinkTarget is not null || new FileInfo(full).LinkTarget is not null)
            throw new IOException("The output folder must not be a symbolic link; choose the folder it points to instead: " + full);
    }
    public static IReadOnlyList<string> Enumerate(string root, CancellationToken ct = default)
    {
        root = Path.GetFullPath(root);
        RequireRealPath(root);
        var files = new List<string>(); var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out string? dir))
            foreach (string path in Directory.EnumerateFileSystemEntries(dir))
            {
                ct.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                OutputSnapshot.Resolve(root, relative);
                if (Directory.Exists(path)) pending.Push(path); else files.Add(relative);
            }
        files.Sort(StringComparer.Ordinal);
        if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new InvalidDataException("Output contains paths that differ only in case; resolve the conflict first.");
        return files;
    }
}
