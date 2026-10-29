using AnvilLOD.Core.World;

namespace AnvilLOD.Core.Paths;

/// <summary>Host paths and Wine drive mappings. Game-relative paths remain in GamePath.</summary>
public static class HostPath
{
    public static string? PrefixRoot(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return null;
        var root = Path.GetFullPath(Expand(prefix.Trim().Trim('"')));
        return Directory.Exists(Path.Combine(root, "pfx", "dosdevices")) ? Path.Combine(root, "pfx") : root;
    }

    private static string Expand(string path)
    {
        if (path == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return Environment.ExpandEnvironmentVariables(path);
    }

    public static string Resolve(string value, string? relativeTo = null, string? winePrefix = null)
    {
        var path = Expand(value.Trim().Trim('"'));
        if (path.Length == 0) throw new ArgumentException("A folder or file path is required.");
        if (!OperatingSystem.IsWindows() && path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            if (path.Length > 2 && path[2] is not ('/' or '\\'))
                throw new ArgumentException("Use an absolute Windows drive path, such as C:\\Games, or a Linux path.");
            var drive = char.ToLowerInvariant(path[0]);
            var prefix = PrefixRoot(winePrefix);
            string? mapped = null;
            if (prefix is not null)
            {
                var link = Path.Combine(prefix, "dosdevices", drive + ":");
                if (Directory.Exists(link)) mapped = new DirectoryInfo(link).ResolveLinkTarget(true)?.FullName ?? link;
                else if (drive == 'c' && Directory.Exists(Path.Combine(prefix, "drive_c"))) mapped = Path.Combine(prefix, "drive_c");
            }
            if (mapped is null && drive == 'z' && prefix is null) mapped = "/";
            if (mapped is null)
                throw new ArgumentException($"Cannot map Windows drive {char.ToUpperInvariant(drive)}:. Select its Wine/Proton prefix or enter a native Linux path.");
            path = Path.Combine(mapped, GamePath.ToHostPath(path[2..].TrimStart('\\', '/')));
        }
        else path = GamePath.ToHostPath(path);
        var full = Path.GetFullPath(path, relativeTo is null ? Environment.CurrentDirectory : Path.GetFullPath(relativeTo));
        var root = Path.GetPathRoot(full)!;
        if (full == root) return full;
        return GamePath.ResolveExisting(root, Path.GetRelativePath(root, full)) ?? full;
    }

    /// <summary>Resolves existing symlink components, including when the final output directory does not exist.</summary>
    public static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in Path.GetRelativePath(current, full).Split(Path.DirectorySeparatorChar))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            if (Directory.Exists(current)) current = new DirectoryInfo(current).ResolveLinkTarget(true)?.FullName ?? current;
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    /// <summary>Stable identity for hashing host locations; Windows aliases differ only in casing.</summary>
    public static string ComparisonKey(string path)
    {
        string canonical = Canonical(path);
        return OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical;
    }

    public static bool IsWithin(string path, string parent)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        path = Canonical(path); parent = Canonical(parent);
        return path.Equals(parent, comparison) || path.StartsWith(parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar, comparison);
    }

    public static IEnumerable<string> GameDocuments(string? prefix, bool vr = false)
    {
        if (PrefixRoot(prefix) is { } root)
        {
            var users = Path.Combine(root, "drive_c", "users");
            if (Directory.Exists(users))
                foreach (var user in Directory.EnumerateDirectories(users).OrderBy(p => Path.GetFileName(p) == "steamuser" ? 0 : 1))
                    foreach (var name in new[] { "Documents", "My Documents" })
                        yield return Path.Combine(user, name, "My Games", vr ? "Skyrim VR" : "Skyrim Special Edition");
        }
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(docs)) yield return Path.Combine(docs, "My Games", vr ? "Skyrim VR" : "Skyrim Special Edition");
    }
}
