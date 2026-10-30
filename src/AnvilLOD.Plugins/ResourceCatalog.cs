using System.Text;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.World;

namespace AnvilLOD.Plugins;

public sealed record ResourceFileInfo(string Path, string Origin, bool FromEnabledMods, IReadOnlyList<string> Overridden);
public sealed record ResourceDiscoveryReport(IReadOnlyList<ResourceFileInfo> Files)
{
    public int Rules => Files.Count(f => f.Path.StartsWith("rules\\", StringComparison.Ordinal));
    public int Configs => Files.Count(f => f.Path.StartsWith("configs\\", StringComparison.Ordinal));
    public int RenderModels => Files.Count(f => f.Path.StartsWith("render\\", StringComparison.Ordinal));
}

/// <summary>Compatible data resources from explicit folders and the winning enabled Data view.
/// No executables are inspected or launched. Logical paths are case-insensitive on every host.</summary>
public sealed class ResourceCatalog
{
    internal sealed record Entry(string Path, string Origin, bool FromMod, Func<byte[]> Read, IReadOnlyList<string> Overridden, int Priority)
    {
        public string Text()
        {
            using var data = new MemoryStream(Read());
            using var reader = new StreamReader(data, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }
    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    private int _priority;
    public ResourceDiscoveryReport Report => new(_files.Values.OrderBy(f => f.Path, StringComparer.Ordinal)
        .Select(f => new ResourceFileInfo(f.Path, f.Origin, f.FromMod, f.Overridden)).ToArray());
    internal IEnumerable<Entry> Under(string prefix) => _files.Values.Where(f => f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .OrderBy(f => f.FromMod).ThenBy(f => f.Path, StringComparer.Ordinal);
    internal Dictionary<string, Func<string>> TextFiles(string folder) => Under(folder + "\\")
        .Where(f => !f.Path[(folder.Length + 1)..].Contains('\\'))
        .ToDictionary(f => GamePath.GetFileName(f.Path), f => (Func<string>)f.Text, StringComparer.OrdinalIgnoreCase);
    internal string? Text(string path) => _files.TryGetValue(GamePath.Normalize(path), out var file) ? file.Text() : null;
    internal IEnumerable<(string Relative, Entry File)> RenderModels(string kind) =>
        new[] { $"render\\skyrim\\{kind}\\", $"render\\{kind}\\" }
            .SelectMany((prefix, index) => Under(prefix).Select(file => (Relative: file.Path[prefix.Length..], File: file, Layout: index)))
            .OrderBy(p => p.File.Priority).ThenBy(p => p.Layout).ThenBy(p => p.Relative, StringComparer.Ordinal)
            .Select(p => (p.Relative, p.File));

    public static IEnumerable<string> Folders(string? legacy, IReadOnlyList<string>? packs) =>
        new[] { legacy }.Concat(packs ?? []).OfType<string>().Where(p => !string.IsNullOrWhiteSpace(p)).Reverse().Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Reverse();

    public static void ValidateFolders(string? legacy, IReadOnlyList<string>? packs, string? output = null)
    {
        foreach (string folder in Folders(legacy, packs))
        {
            _ = FindRoot(folder);
            if (output is not null && (HostPath.IsWithin(output, folder) || HostPath.IsWithin(folder, output)))
                throw new ArgumentException("Generated output and resource folders must be separate, without nesting.");
        }
    }

    public static string FindRoot(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Resource folder not found: " + folder);
        foreach (string sub in new[] { @"Edit Scripts\DynDOLOD", @"Data\DynDOLOD", "DynDOLOD", "" })
        {
            string? root = sub.Length == 0 ? Path.GetFullPath(folder) : GamePath.ResolveExisting(folder, sub);
            if (root is null || !Directory.Exists(root)) continue;
            if (new[] { "Rules", "Configs", "Render" }.Any(n => GamePath.ResolveExisting(root, n) is { } p && Directory.Exists(p))
                || Directory.EnumerateFiles(root).Any(f => Category(Path.GetFileName(f)) is not null)) return root;
        }
        throw new DirectoryNotFoundException("Choose a resource pack containing Rules, Configs or Render, or compatible DynDOLOD data files: " + folder);
    }

    public static ResourceCatalog Load(GameContext game, AssetIndex assets, string? legacy = null, IReadOnlyList<string>? packs = null, CancellationToken ct = default)
    {
        var catalog = new ResourceCatalog();
        foreach (string folder in Folders(legacy, packs))
        {
            catalog._priority++;
            string root = FindRoot(folder);
            foreach (string file in PackFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                if (Category(Path.GetRelativePath(root, file)) is { } relative)
                    catalog.Add(relative, file, false, () => File.ReadAllBytes(file));
            }
        }
        // Legacy Data-root rules have lower precedence than the structured Data/DynDOLOD view.
        catalog._priority++;
        foreach (var (name, file) in game.RootFiles().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (Category(name) is { } relative) catalog.Add(relative, file, true, () => File.ReadAllBytes(file));
        }
        foreach (string path in assets.EnumeratePaths(@"dyndolod\").OrderBy(p => p.Count(c => c == '\\')).ThenBy(p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (Category(path[9..]) is not { } relative) continue;
            catalog.Add(relative, assets.DescribeOrigin(path) ?? path, true, () =>
            {
                if (!assets.TryOpen(path, out var stream)) throw new FileNotFoundException("Resource is no longer available: " + path);
                using (stream) { using var data = new MemoryStream(); stream.CopyTo(data); return data.ToArray(); }
            });
        }
        return catalog;
    }

    internal static IEnumerable<string> ExternalFiles(string? legacy, IReadOnlyList<string>? packs) =>
        Folders(legacy, packs).SelectMany(folder => PackFiles(FindRoot(folder)));

    private static IEnumerable<string> PackFiles(string root)
    {
        // A bundle's binaries and unrelated directories are never used as resource inputs.
        var files = Directory.EnumerateFiles(root).ToList();
        foreach (string category in new[] { "Rules", "Configs", "Render" })
            if (GamePath.ResolveExisting(root, category) is { } dir && Directory.Exists(dir))
                files.AddRange(Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }));
        return files.Where(p => Category(Path.GetRelativePath(root, p)) is not null)
            .OrderBy(p => Path.GetRelativePath(root, p).Count(c => c == Path.DirectorySeparatorChar)).ThenBy(p => p, StringComparer.Ordinal);
    }

    private void Add(string path, string origin, bool fromMod, Func<byte[]> read)
    {
        var previous = _files.GetValueOrDefault(path);
        _files[path] = new(path, origin, fromMod, read, previous is null ? [] : [.. previous.Overridden, previous.Origin], _priority);
    }

    private static string? Category(string path)
    {
        path = GamePath.Normalize(path);
        if (path.StartsWith("render\\") && path.EndsWith(".nif")) return path;
        if (path.StartsWith("rules\\") && !path[6..].Contains('\\') && path.EndsWith(".ini")) return path;
        if (path.StartsWith("configs\\") && !path[8..].Contains('\\') && (path.EndsWith(".ini") || path.EndsWith(".txt"))) return path;
        if (path.Contains('\\') || !path.StartsWith("dyndolod_", StringComparison.Ordinal)) return null;
        if (path.EndsWith(".txt") || path.Contains("_childworld_", StringComparison.Ordinal)) return "configs\\" + path;
        return path.EndsWith(".ini") ? "rules\\" + path : null;
    }
}
