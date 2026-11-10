using PenguLOD.Core.World;

namespace PenguLOD.Core.Paths;

/// <summary>Read older product files without writing new output under the old identity.</summary>
public static class ProductPaths
{
    public const string LegacyName = "AnvilLOD";
    public static string LegacyFileName(string current) => current.Replace("PenguLOD", LegacyName, StringComparison.Ordinal);
    public static string? FindFile(string folder, string current) =>
        GamePath.ResolveExisting(folder, current) ?? GamePath.ResolveExisting(folder, LegacyFileName(current));

    public static bool HasLegacyOutput(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        var names = Directory.EnumerateFiles(folder).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Any(n => n is not null && n.StartsWith(LegacyName, StringComparison.OrdinalIgnoreCase)
                && (n.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)))
            || new[] { "manifest.json", "grass.json", "textures.json", "log", "esp", "esm" }
            .Any(suffix => names.Contains(LegacyName + "." + suffix));
    }

    public static void RequireCurrentOutput(string folder)
    {
        if (HasLegacyOutput(folder))
            throw new InvalidOperationException("This folder contains AnvilLOD-named output. Keep it as your working baseline and generate the PenguLOD replacement into a new folder. Do not rename generated plugins or mix the two output sets.");
    }
}
