using AnvilLOD.Core.Pipeline;

namespace AnvilLOD.Core.World;

/// <summary>
/// Case-insensitive lookups for a read-only batch. Each directory needed for a case fallback is
/// enumerated once. Create a new lookup after changing files; do not share it between workers.
/// </summary>
public sealed class GamePathLookup
{
    private readonly string _root;
    private readonly Func<string, IEnumerable<string>> _enumerate;
    private readonly Dictionary<string, Dictionary<string, string?>> _directories = new(StringComparer.Ordinal);

    public GamePathLookup(string root) : this(root, Directory.EnumerateFileSystemEntries) { }

    internal GamePathLookup(string root, Func<string, IEnumerable<string>> enumerate)
    { _root = Path.GetFullPath(root); _enumerate = enumerate; }

    public string? ResolveExisting(string relative, CancellationToken ct = default)
    {
        GamePath.ValidateRelative(relative);
        string current = _root;
        foreach (string part in GamePath.ToHostPath(relative).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested(); RunLimits.Check();
            string exact = Path.Combine(current, part);
            if (Path.Exists(exact)) { current = exact; continue; }
            if (!_directories.TryGetValue(current, out var entries))
            {
                if (!Directory.Exists(current)) return null;
                entries = new(StringComparer.OrdinalIgnoreCase);
                foreach (string path in _enumerate(current))
                {
                    ct.ThrowIfCancellationRequested(); RunLimits.Check();
                    string name = Path.GetFileName(path);
                    if (!entries.TryAdd(name, path)) entries[name] = null;
                }
                _directories.Add(current, entries);
            }
            if (!entries.TryGetValue(part, out var match)) return null;
            if (match is null) throw new IOException($"Ambiguous filename case for '{part}' in '{current}'.");
            current = match;
        }
        return Path.Exists(current) ? current : null;
    }
}
