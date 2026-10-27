using AnvilLOD.Core.World;
using AnvilLOD.Plugins.Mo2;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Plugins;

namespace AnvilLOD.Plugins;

/// <summary>Explicit archive discovery, independent of Windows registry and the host's Documents folder.</summary>
public static class SkyrimArchives
{
    private static readonly string[] Defaults1 = ["Skyrim - Misc.bsa", "Skyrim - Shaders.bsa", "Skyrim - Interface.bsa",
        "Skyrim - Animations.bsa", "Skyrim - Meshes0.bsa", "Skyrim - Meshes1.bsa", "Skyrim - Sounds.bsa"];
    private static readonly string[] Defaults2 = ["Skyrim - Voices_en0.bsa", "Skyrim - Textures0.bsa", "Skyrim - Textures1.bsa",
        "Skyrim - Textures2.bsa", "Skyrim - Textures3.bsa", "Skyrim - Textures4.bsa", "Skyrim - Textures5.bsa",
        "Skyrim - Textures6.bsa", "Skyrim - Textures7.bsa", "Skyrim - Textures8.bsa", "Skyrim - Patch.bsa"];

    /// <param name="iniPaths">Highest priority first. Each archive-list key is overridden independently.</param>
    public static IReadOnlyList<string> ReadIniLists(IEnumerable<string> iniPaths)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ini in iniPaths.Distinct(StringComparer.Ordinal))
        {
            var folder = Path.GetDirectoryName(ini);
            var file = folder is null ? null : GamePath.ResolveExisting(folder, Path.GetFileName(ini));
            if (file is null || !File.Exists(file)) continue;
            if (Mo2Instance.ReadIni(file).TryGetValue("Archive", out var section))
                foreach (var pair in section) values.TryAdd(pair.Key, pair.Value);
        }
        string[] List(string key, string[] fallback) => values.TryGetValue(key, out var v)
            ? v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : fallback;
        return List("sResourceArchiveList", Defaults1).Concat(List("sResourceArchiveList2", Defaults2))
            .Concat(List("sArchiveToLoadInMemoryList", [])).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> Select(GameRelease release, IEnumerable<string> archivePaths,
        IReadOnlyList<string> plugins, IReadOnlyList<string> iniArchives)
    {
        var available = archivePaths.Where(p => p.EndsWith(".bsa", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(GamePath.GetFileName, p => p, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string name) { if (available.TryGetValue(name, out var full) && seen.Add(name)) result.Add(full); }
        foreach (var name in iniArchives) Add(name);
        foreach (var plugin in plugins)
        {
            var key = ModKey.FromFileName(plugin);
            foreach (var name in available.Keys.Where(n => Archive.IsApplicable(release, key, n)).Order(StringComparer.OrdinalIgnoreCase)) Add(name);
        }
        return result;
    }
}
