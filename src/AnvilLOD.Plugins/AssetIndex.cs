using System.Collections.Concurrent;
using System.Diagnostics;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;

namespace AnvilLOD.Plugins;

/// <summary>
/// Loose files + BSAs resolved once, with load-order priority (loose beats any archive,
/// later archives beat earlier ones).
/// <para>
/// Mutagen's built-in <c>ArchiveAssetProvider</c> re-opens every BSA on every lookup, which is
/// far too slow for LOD work (tens of thousands of lookups). This builds the archive index
/// once up front and checks loose files lazily, so it also works through MO2's VFS without
/// enumerating the whole Data folder.
/// </para>
/// </summary>
public sealed class AssetIndex : IAssetSource
{
    private readonly Func<string, FileInfo?> _looseResolver;
    private readonly Func<string, IEnumerable<string>> _looseEnumerator;
    private readonly Dictionary<string, ArchiveEntry> _archived;
    private readonly ConcurrentDictionary<string, FileInfo?> _looseCache = new(StringComparer.Ordinal);
    private Dictionary<string, FileInfo> _overrides = new(StringComparer.Ordinal);
    private readonly List<string> _replacedPrefixes = [];
    private bool Replaced(string key) => _replacedPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));

    /// <summary>Attach a verified generated-asset manifest before starting readers.</summary>
    public void SetOverrides(string root, IEnumerable<string> paths, string? replacePrefix = null, CancellationToken ct = default)
    {
        var files = new Dictionary<string, FileInfo>(StringComparer.Ordinal);
        var lookup = new GamePathLookup(root);
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested(); RunLimits.Check();
            var key = GamePath.Normalize(path);
            var file = new FileInfo(lookup.ResolveExisting(key, ct) ?? GamePath.ToFileSystemPath(root, key));
            if (!file.Exists) throw new FileNotFoundException("Generated asset is missing; regenerate its output.", file.FullName);
            files.Add(key, file);
        }
        if (replacePrefix is not null)
        {
            replacePrefix = GamePath.Normalize(replacePrefix);
            foreach (var key in _overrides.Keys.Where(k => k.StartsWith(replacePrefix, StringComparison.Ordinal)).ToArray()) _overrides.Remove(key);
            _replacedPrefixes.Add(replacePrefix);
        }
        foreach (var (key, file) in files) _overrides[key] = file;
    }

    /// <summary>Archives that were read.</summary>
    public int ArchiveCount { get; }
    public int ArchivedFileCount => _archived.Count;

    /// <summary>Archives that could not be read (a corrupt file, or names Mutagen cannot decode), each as "name: reason". They are left out.</summary>
    public IReadOnlyList<string> SkippedArchives { get; }
    public TimeSpan IndexTime { get; }

    private sealed record ArchiveEntry(string ArchiveName, IArchiveFile File);

    private AssetIndex(Func<string, FileInfo?> looseResolver, Func<string, IEnumerable<string>> looseEnumerator,
        Dictionary<string, ArchiveEntry> archived, int archiveCount, TimeSpan time, IReadOnlyList<string> skipped)
    {
        SkippedArchives = skipped;
        _looseResolver = looseResolver;
        _looseEnumerator = looseEnumerator;
        _archived = archived;
        ArchiveCount = archiveCount;
        IndexTime = time;
    }

    /// <summary>Plain Data folder (or MO2 VFS): explicit INI and enabled-plugin archive ordering; loose files are checked on demand.</summary>
    /// <param name="pathFilter">Optional filter on normalized paths (e.g. "meshes\\") to keep the index small.</param>
    /// <param name="pluginOrder">Plugin file names in load order; only enabled plugins should be supplied.</param>
    public static AssetIndex Build(GameRelease release, string dataFolder, Func<string, bool>? pathFilter = null,
        IReadOnlyList<string>? pluginOrder = null, IReadOnlyList<string>? iniPaths = null)
    {
        var archives = SkyrimArchives.Select(release, Directory.EnumerateFiles(dataFolder), pluginOrder ?? [],
            SkyrimArchives.ReadIniLists(iniPaths ?? []));
        IEnumerable<string> EnumerateLoose(string prefix)
        {
            var dir = GamePath.ResolveExisting(dataFolder, prefix.TrimEnd('\\'));
            if (dir is null || !Directory.Exists(dir)) yield break;
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(dir, "*", opts))
                yield return GamePath.Normalize(Path.GetRelativePath(dataFolder, f));
        }

        return Create(release, archives, EnumerateLoose, key =>
        {
            var full = GamePath.ResolveExisting(dataFolder, key);
            if (full is null) return null;
            var fi = new FileInfo(full);
            return fi.Exists ? fi : null;
        }, pathFilter);
    }

    /// <summary>
    /// Explicit archive list (lowest priority first) and loose-file resolver — used for MO2 instances
    /// read straight from disk.
    /// </summary>
    public static AssetIndex Create(GameRelease release, IReadOnlyList<string> archivesLowToHigh,
        Func<string, IEnumerable<string>> looseEnumerator,
        Func<string, FileInfo?> looseResolver, Func<string, bool>? pathFilter = null)
    {
        var sw = Stopwatch.StartNew();
        var map = new Dictionary<string, ArchiveEntry>(StringComparer.Ordinal);
        var skipped = new List<string>();

        foreach (var archivePath in archivesLowToHigh)
        {
            RunLimits.Check();
            var name = Path.GetFileName(archivePath);
            // The name table is read lazily, so a bad archive (e.g. "Strings section was not able to be read: did not end all
            // of its strings in null bytes", seen with a mod whose BSA holds non-ASCII file names) throws while the files are
            // listed. Collect them first so a half-read archive adds nothing, and skip the archive rather than the whole run.
            List<(string Key, IArchiveFile File)> entries = [];
            try
            {
                var reader = Archive.CreateReader(release, archivePath);
                foreach (var f in reader.Files)
                {
                    RunLimits.Check();
                    var key = GamePath.Normalize(f.Path);
                    if (pathFilter is not null && !pathFilter(key)) continue;
                    entries.Add((key, f));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped.Add($"{name}: {ex.GetBaseException().Message}");
                continue;
            }
            foreach (var (key, f) in entries) map[key] = new ArchiveEntry(name, f); // later archive wins
        }

        return new AssetIndex(looseResolver, looseEnumerator, map, archivesLowToHigh.Count - skipped.Count, sw.Elapsed, skipped);
    }

    /// <summary>All data-relative paths (normalized) under a prefix such as "meshes\\", from loose files and BSAs.</summary>
    public IEnumerable<string> EnumeratePaths(string prefix)
    {
        prefix = GamePath.Normalize(prefix);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in _overrides.Keys)
            if (k.StartsWith(prefix, StringComparison.Ordinal) && seen.Add(k)) yield return k;
        foreach (var k in _looseEnumerator(prefix))
            if (!Replaced(k) && k.StartsWith(prefix, StringComparison.Ordinal) && seen.Add(k)) yield return k;
        foreach (var k in _archived.Keys)
            if (!Replaced(k) && k.StartsWith(prefix, StringComparison.Ordinal) && seen.Add(k)) yield return k;
    }

    private FileInfo? Loose(string key) => _looseCache.GetOrAdd(key, _looseResolver);

    /// <summary>
    /// A folder read as the lowest-priority layer (below every mod and archive): files AnvilLOD made itself, such as tree billboards rendered
    /// without TexGen. Not cached, because those files appear while a run is going on.
    /// </summary>
    public string? FallbackRoot { get; set; }

    private FileInfo? Fallback(string key)
    {
        if (FallbackRoot is not { } root) return null;
        var full = GamePath.ResolveExisting(root, key);
        if (full is null) return null;
        var fi = new FileInfo(full);
        return fi.Exists ? fi : null;
    }

    public bool Exists(string dataRelativePath)
    {
        var key = GamePath.Normalize(dataRelativePath);
        return _overrides.ContainsKey(key) || !Replaced(key) && (Loose(key) is not null || _archived.ContainsKey(key) || Fallback(key) is not null);
    }

    public bool TryOpen(string dataRelativePath, out Stream stream)
    {
        var key = GamePath.Normalize(dataRelativePath);
        if (Replaced(key) && !_overrides.ContainsKey(key)) { stream = Stream.Null; return false; }
        if ((_overrides.GetValueOrDefault(key) ?? Loose(key)) is { } fi)
        {
            stream = fi.OpenRead();
            return true;
        }
        if (_archived.TryGetValue(key, out var e))
        {
            stream = new MemoryStream(e.File.GetBytes(), writable: false);
            return true;
        }
        if (Fallback(key) is { } fb)
        {
            stream = fb.OpenRead();
            return true;
        }
        stream = Stream.Null;
        return false;
    }

    public string? Fingerprint(string dataRelativePath)
    {
        var key = GamePath.Normalize(dataRelativePath);
        if (_overrides.TryGetValue(key, out var generated)) return $"G:{generated.Length}:{generated.LastWriteTimeUtc.Ticks}";
        if (Replaced(key)) return null;
        if (Loose(key) is { } fi) return $"L:{fi.Length}:{fi.LastWriteTimeUtc.Ticks}";
        if (_archived.TryGetValue(key, out var e)) return $"A:{e.ArchiveName}:{e.File.Size}";
        if (Fallback(key) is { } fb) return $"F:{fb.Length}:{fb.LastWriteTimeUtc.Ticks}";
        return null;
    }

    /// <summary>Diagnostic origin without disclosing an absolute filesystem path.</summary>
    public string? DescribeOrigin(string dataRelativePath)
    {
        var key = GamePath.Normalize(dataRelativePath);
        if (_overrides.ContainsKey(key)) return "Verified generated input";
        if (Replaced(key)) return null;
        if (Loose(key) is { } loose)
        {
            int depth = key.Count(c => c == '\\');
            var root = loose.Directory;
            for (int i = 0; i < depth && root?.Parent is not null; i++) root = root.Parent;
            return "Loose layer: " + (root?.Name ?? "Data");
        }
        return _archived.TryGetValue(key, out var archive) ? "Archive: " + Path.GetFileName(archive.ArchiveName) : Fallback(key) is not null ? "Generated fallback cache" : null;
    }

    /// <summary>Where the winning copy comes from — handy for reports ("loose" or the BSA name).</summary>
    public string? SourceOf(string dataRelativePath)
    {
        var key = GamePath.Normalize(dataRelativePath);
        if (_overrides.ContainsKey(key)) return key.StartsWith("grass\\", StringComparison.Ordinal) ? "AnvilLOD grass caching" : "AnvilLOD texture generation";
        if (Replaced(key)) return null;
        if (Loose(key) is not null) return "loose";
        if (_archived.TryGetValue(key, out var e)) return e.ArchiveName;
        return Fallback(key) is not null ? "AnvilLOD cache" : null;
    }
}
