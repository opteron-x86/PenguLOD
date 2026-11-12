using System.Collections.Immutable;
using PenguLOD.Core.Paths;

namespace PenguLOD.Core.Pipeline;

/// <summary>Stage identifiers used in journals, receipts and plans.</summary>
public static class StageIds
{
    public const string Grass = "grass";
    public const string Textures = "textures";
    public const string Terrain = "terrain";
    public const string Lod = "lod";
    public static readonly IReadOnlyList<string> All = [Grass, Textures, Terrain, Lod];
    public static bool IsKnown(string? id) => id is Grass or Textures or Terrain or Lod;
}

/// <summary>
/// Files in an output folder that belong to the user or the mod manager, not to a generation: they are never hashed,
/// verified, replaced or deleted by a stage. A new generation keeps the installed copy.
/// </summary>
public static class UserOwnedFiles
{
    /// <summary>Paths relative to the output folder, '/'-separated.</summary>
    public static readonly IReadOnlyList<string> Paths = ["meta.ini", "SKSE/Plugins/PenguLOD.ini"];

    public static bool Contains(string relative)
    {
        string normalized = relative.Replace('\\', '/').TrimStart('/');
        foreach (string path in Paths)
            if (path.Equals(normalized, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The user-owned files present in <paramref name="folder"/>, with their on-disk relative spelling.</summary>
    public static IEnumerable<string> Existing(string folder)
    {
        if (!Directory.Exists(folder)) yield break;
        foreach (string path in Paths)
            if (PenguLOD.Core.World.GamePath.ResolveAllExisting(folder, path).LastOrDefault(File.Exists) is { } found
                && new FileInfo(found).LinkTarget is null)
                yield return Path.GetRelativePath(folder, found).Replace('\\', '/');
    }
}

/// <summary>
/// Exclusive lock on an output location, shared by every PenguLOD process. Code running inside a job that already holds
/// the lock (a stage within its pipeline) joins it; a separate job, even in the same process, is refused.
/// </summary>
public static class OutputLock
{
    private static readonly AsyncLocal<ImmutableHashSet<string>?> Flow = new();

    public static string PathFor(string output)
    {
        string canonical = HostPath.Canonical(output);
        return Path.Combine(Path.GetDirectoryName(canonical)!,
            ".pengulod-output-" + OutputSnapshot.HashText(HostPath.ComparisonKey(canonical))[..24] + ".lock");
    }

    /// <summary>True when this execution flow already holds the lock for <paramref name="output"/>.</summary>
    public static bool HeldHere(string output) => Flow.Value?.Contains(PathFor(output)) == true;

    public static IDisposable Acquire(string output)
    {
        string file = PathFor(output);
        if (Flow.Value?.Contains(file) == true) return new Release(null, null);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (new FileInfo(file).LinkTarget is not null) throw new IOException("Output lock must not be a symbolic link: " + file);
        FileStream stream;
        try { stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new IOException("Another generation job is using this output or checkpoint: " + file, e); }
        Flow.Value = (Flow.Value ?? ImmutableHashSet<string>.Empty).Add(file);
        return new Release(file, stream);
    }

    private sealed class Release(string? file, FileStream? stream) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0 || file is null) return;
            if (Flow.Value is { } held) Flow.Value = held.Remove(file);
            stream!.Dispose();
        }
    }
}

/// <summary>How many earlier copies of an output a publication keeps beside it (".previous-*").</summary>
public static class OutputRetention
{
    public const int Default = 1;
    public const int Maximum = 100;
    private static readonly AsyncLocal<int?> Current = new();
    public static int KeepPrevious => Current.Value ?? Default;

    public static IDisposable Enter(int keepPrevious)
    {
        if (keepPrevious is < 0 or > Maximum) throw new ArgumentOutOfRangeException(nameof(keepPrevious), $"Keep between 0 and {Maximum} previous outputs.");
        var previous = Current.Value; Current.Value = keepPrevious;
        return new Scope(() => Current.Value = previous);
    }

    /// <summary>Earlier copies of <paramref name="output"/>, newest first. Their names sort by publication time.</summary>
    public static IReadOnlyList<string> PreviousCopies(string output)
    {
        string full = Path.TrimEndingDirectorySeparator(HostPath.Canonical(output));
        string parent = Path.GetDirectoryName(full)!, prefix = Path.GetFileName(full) + ".previous-";
        if (!Directory.Exists(parent)) return [];
        return Directory.EnumerateDirectories(parent)
            .Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.Ordinal) && new DirectoryInfo(d).LinkTarget is null)
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).ToArray();
    }

    /// <summary>Deletes earlier copies beyond the newest <paramref name="keep"/>. Returns the folders removed.</summary>
    public static IReadOnlyList<string> Prune(string output, int keep, IProgress<string>? progress = null)
    {
        var removed = new List<string>();
        foreach (string old in PreviousCopies(output).Skip(Math.Max(0, keep)))
        {
            try { Directory.Delete(old, recursive: true); removed.Add(old); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { progress?.Report("Could not remove previous output " + old + ": " + e.Message); }
        }
        if (removed.Count > 0) progress?.Report($"Removed {removed.Count} older previous output(s); keeping {keep}.");
        return removed;
    }

    private sealed class Scope(Action end) : IDisposable
    { private Action? _end = end; public void Dispose() => Interlocked.Exchange(ref _end, null)?.Invoke(); }
}

/// <summary>Keeps the newest entries of a folder of timestamped run folders.</summary>
public static class FolderRetention
{
    /// <summary>Deletes all but the <paramref name="keep"/> most recently written subfolders of <paramref name="parent"/>.</summary>
    public static int Prune(string parent, int keep)
    {
        if (!Directory.Exists(parent)) return 0;
        int removed = 0;
        foreach (var dir in new DirectoryInfo(parent).EnumerateDirectories().Where(d => d.LinkTarget is null)
            .OrderByDescending(d => d.LastWriteTimeUtc).ThenByDescending(d => d.Name, StringComparer.Ordinal).Skip(Math.Max(0, keep)))
        {
            try { dir.Delete(recursive: true); removed++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return removed;
    }
}
