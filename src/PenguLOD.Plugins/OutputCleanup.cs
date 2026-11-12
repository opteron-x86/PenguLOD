using PenguLOD.Core.Paths;
using PenguLOD.Core.Pipeline;

namespace PenguLOD.Plugins;

public sealed record CleanupItem(string Path, string Reason, long Bytes);

/// <summary>Finds and removes previous copies and unfinished build folders that publication leaves beside an output.</summary>
public static class OutputCleanup
{
    /// <summary>Unattributed build folders of older versions are only removed once they are this old.</summary>
    public static readonly TimeSpan LegacyMinimumAge = TimeSpan.FromHours(24);

    public static IReadOnlyList<CleanupItem> Find(string output, int keepPrevious, CancellationToken ct = default)
    {
        if (keepPrevious is < 0 or > OutputRetention.Maximum) throw new ArgumentOutOfRangeException(nameof(keepPrevious));
        string full = HostPath.Canonical(output), parent = Path.GetDirectoryName(full)!;
        var items = new List<CleanupItem>();
        void Add(string dir, string reason) { if (new DirectoryInfo(dir).LinkTarget is null) items.Add(new(dir, reason, Size(dir, ct))); }
        foreach (string old in OutputRetention.PreviousCopies(full).Skip(keepPrevious)) Add(old, "previous output");
        if (Directory.Exists(parent))
        {
            foreach (string dir in Directory.EnumerateDirectories(parent, StagedOutput.WorkspacePrefix(full) + "*")) Add(dir, "unfinished build");
            // Earlier versions named workspaces without their output; they cannot be attributed, so only stale ones are listed.
            foreach (string dir in Directory.EnumerateDirectories(parent))
            {
                string name = Path.GetFileName(dir);
                bool legacy = (name.StartsWith(".pengulod-build-", StringComparison.Ordinal) && name.Length == ".pengulod-build-".Length + 32)
                    || (name.StartsWith(".pengulod-grass-build-", StringComparison.Ordinal) && name.Length == ".pengulod-grass-build-".Length + 32);
                if (legacy && DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) >= LegacyMinimumAge) Add(dir, "unfinished build of an earlier version");
            }
        }
        if (Directory.Exists(full))
            foreach (string dir in Directory.EnumerateDirectories(full, ".texture-build-*")) Add(dir, "unfinished texture build of an earlier version");
        return items;
    }

    /// <summary>Removes <paramref name="items"/> found for <paramref name="output"/> while holding its output lock.</summary>
    public static int Apply(string output, IReadOnlyList<CleanupItem> items, IProgress<string>? progress = null)
    {
        using var guard = OutputLock.Acquire(output);
        int removed = 0;
        foreach (var item in items)
        {
            try { Directory.Delete(item.Path, recursive: true); removed++; progress?.Report("Removed " + item.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { progress?.Report("Could not remove " + item.Path + ": " + e.Message); }
        }
        return removed;
    }

    private static long Size(string dir, CancellationToken ct)
    {
        long total = 0;
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        { ct.ThrowIfCancellationRequested(); if (file.LinkTarget is null) total += file.Length; }
        return total;
    }
}
