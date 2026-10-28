using AnvilLOD.Core.Paths;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins.Mo2;
using Mutagen.Bethesda;

namespace AnvilLOD.Plugins;

internal static class TerrainDefaultTexture
{
    // Skyrim's unpainted LAND uses the Landscape INI setting, not Oblivion's Default.dds.
    internal const string SkyrimDefault = @"textures\landscape\dirt02.dds";

    internal static string Resolve(GameContextOptions options, string dataFolder)
    {
        string? ini = options.GameIniPath is { } path ? HostPath.Resolve(path, winePrefix: options.WinePrefix) : null;
        IReadOnlyList<string> candidates;
        if (options.Mode == GameSourceMode.Mo2Instance)
        {
            var locations = (options.Mo2Locations ?? new()) with { WinePrefix = options.WinePrefix ?? options.Mo2Locations?.WinePrefix };
            var instance = Mo2Instance.Open(options.Mo2InstanceFolder!, locations);
            candidates = instance.OpenProfile(options.Mo2Profile).IniPaths(ini, options.Release == GameRelease.SkyrimVR);
        }
        else candidates = GameContext.IniPaths(options with { GameIniPath = ini }, dataFolder);
        return Read(candidates);
    }

    internal static string Read(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            string full = Path.GetFullPath(candidate);
            var file = GamePath.ResolveExisting(Path.GetDirectoryName(full)!, Path.GetFileName(full));
            if (file is null || !File.Exists(file)
                || !Mo2Instance.ReadIni(file).TryGetValue("Landscape", out var section)
                || !section.TryGetValue("sDefaultLandDiffuseTexture", out var value)
                || string.IsNullOrWhiteSpace(value)) continue;
            var path = GamePath.Normalize(value);
            GamePath.ValidateRelative(path);
            if (path.StartsWith("textures\\", StringComparison.Ordinal)) return path;
            return path.StartsWith("landscape\\", StringComparison.Ordinal) ? "textures\\" + path : @"textures\landscape\" + path;
        }
        return SkyrimDefault;
    }
}
