using PenguLOD.Core.World;

namespace PenguLOD.Core.Paths;

/// <summary>Read older product files without writing new output under the old identity.</summary>
public static class ProductPaths
{
    public const string LegacyName = "AnvilLOD";
    public static string LegacyFileName(string current) => current.Replace("PenguLOD", LegacyName, StringComparison.Ordinal);
    public static string? FindFile(string folder, string current) =>
        GamePath.ResolveExisting(folder, current) ?? GamePath.ResolveExisting(folder, LegacyFileName(current));

    public static string[] LegacyOutputFiles(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var markers = new[] { "manifest.json", "grass.json", "textures.json", "log", "esp", "esm" }
            .Select(suffix => LegacyName + "." + suffix).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()
            .Where(n => markers.Contains(n) || n.StartsWith(LegacyName, StringComparison.OrdinalIgnoreCase)
                && (n.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".esm", StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool HasLegacyOutput(string folder) => LegacyOutputFiles(folder).Length > 0;

    public static string LegacyOutputMessage(string folder, IReadOnlyList<string> files) =>
        $"Older AnvilLOD output in '{folder}': {string.Join(", ", files.Take(5))}"
        + (files.Count > 5 ? $" (and {files.Count - 5} more)" : "")
        + ". Choose an empty output folder or clear the old generated output before generating. Do not rename individual generated files or mix old and new output.";

    public static void RequireCurrentOutput(string folder)
    {
        var files = LegacyOutputFiles(folder);
        if (files.Length > 0)
            throw new InvalidOperationException(LegacyOutputMessage(folder, files));
    }
}
