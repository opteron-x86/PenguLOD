using System.Text.RegularExpressions;
using AnvilLOD.Core.Paths;

namespace AnvilLOD.Desktop.Services;

public sealed record GameInstallation(string GameFolder, string Prefix, string? PluginsFile);

public static partial class InstallDiscovery
{
    public static IReadOnlyList<GameInstallation> Find()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        IEnumerable<string> steamRoots = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam")]
            : new[] { ".steam/steam", ".local/share/Steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam" }
                .Select(p => Path.Combine(home, p));
        return Find(steamRoots, OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : null);
    }

    internal static IReadOnlyList<GameInstallation> Find(IEnumerable<string> steamRoots, string? windowsLocalData)
    {
        var libraries = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var root in steamRoots.Where(Directory.Exists))
        {
            libraries.Add(HostPath.Canonical(root));
            string file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(file))
                foreach (Match match in LibraryPath().Matches(File.ReadAllText(file))) libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        }
        var found = new List<GameInstallation>();
        foreach (var lib in libraries)
        foreach (var (id, folder) in new[] { ("489830", "Skyrim Special Edition"), ("611670", "SkyrimVR") })
        {
            string game = Path.Combine(lib, "steamapps", "common", folder);
            if (!Directory.Exists(Path.Combine(game, "Data"))) continue;
            string prefix = windowsLocalData is null ? Path.Combine(lib, "steamapps", "compatdata", id, "pfx") : "";
            string plugins = Path.Combine(windowsLocalData ?? Path.Combine(prefix, "drive_c", "users", "steamuser", "AppData", "Local"),
                id == "611670" ? "Skyrim VR" : "Skyrim Special Edition", "plugins.txt");
            found.Add(new(game, Directory.Exists(prefix) ? prefix : "", File.Exists(plugins) ? plugins : null));
        }
        return found;
    }
    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPath();
}
