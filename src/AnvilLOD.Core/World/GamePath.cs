namespace AnvilLOD.Core.World;

/// <summary>Data-relative game paths always use backslashes, regardless of host OS.</summary>
public static class GamePath
{
    public static string Join(params string[] parts) => string.Join('\\', parts);

    /// <summary>Converts separators at the filesystem boundary, preserving filename case.</summary>
    public static string ToHostPath(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    public static string ToFileSystemPath(string root, string dataRelativePath)
    {
        ValidateRelative(dataRelativePath);
        return Path.Combine(root, ToHostPath(dataRelativePath));
    }

    /// <summary>Rejects paths that escape Data or contain names Windows cannot use, even on a Unix host.</summary>
    public static void ValidateRelative(string path)
    {
        var p = path.Replace('/', '\\');
        if (string.IsNullOrWhiteSpace(p) || p.StartsWith('\\')
            || p.Any(c => c < 32 || "<>:\"|?*".Contains(c))
            || p.Split('\\', StringSplitOptions.RemoveEmptyEntries).Any(s => s is "." or ".."))
            throw new ArgumentException($"Expected a safe Data-relative game path, got '{path}'.", nameof(path));
    }

    /// <summary>
    /// Texture references sometimes retain the author's absolute install path. Keep the part starting at textures,
    /// so both asset lookup and emitted NIFs use the game's path rather than creating a literal drive-name folder.
    /// </summary>
    public static string NormalizeTexture(string path)
    {
        var p = path.Trim().Replace('/', '\\');
        if (p.Length == 0) return ""; // unused shader slot
        if (p.StartsWith("data\\", StringComparison.OrdinalIgnoreCase)) p = p[5..];
        if (!p.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase) || p.Contains(':'))
        {
            int start = p.LastIndexOf("\\textures\\", StringComparison.OrdinalIgnoreCase);
            p = start >= 0 ? p[(start + 1)..] : "textures\\" + p.TrimStart('\\');
        }
        p = string.Join('\\', p.Split('\\', StringSplitOptions.RemoveEmptyEntries));
        ValidateRelative(p);
        return p;
    }

    /// <summary>Game paths can contain either separator, even on a Unix host.</summary>
    public static string GetFileName(string path) => path[(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1)..];

    public static string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(GetFileName(path));

    /// <summary>
    /// Resolves a case-insensitive game path to its actual spelling on disk. The root is a host path;
    /// never lowercase it. Exact matches win; ambiguous fallback matches are reported instead of picked at random.
    /// </summary>
    public static string? ResolveExisting(string root, string dataRelativePath)
    {
        var relative = ToHostPath(dataRelativePath);
        if (Path.IsPathRooted(relative)) throw new ArgumentException("Expected a Data-relative path.", nameof(dataRelativePath));
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p is "." or "..")) throw new ArgumentException("A game path must stay inside its root.", nameof(dataRelativePath));
        var current = root;
        foreach (var part in parts)
        {
            var exact = Path.Combine(current, part);
            if (Path.Exists(exact)) { current = exact; continue; }
            if (!Directory.Exists(current)) return null;
            var matches = Directory.EnumerateFileSystemEntries(current)
                .Where(p => string.Equals(Path.GetFileName(p), part, StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (matches.Length == 0) return null;
            if (matches.Length > 1) throw new IOException($"Ambiguous filename case for '{part}' in '{current}'.");
            current = matches[0];
        }
        return Path.Exists(current) ? current : null;
    }

    /// <summary>Lower-cased, backslash-separated, no leading "data\" — the form used as a cache/index key.</summary>
    public static string Normalize(string path)
    {
        var p = path.Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
        if (p.StartsWith("data\\", StringComparison.Ordinal)) p = p[5..];
        return p;
    }
}
