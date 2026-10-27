using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Plugins.Mo2;

namespace AnvilLOD.Plugins;

/// <summary>Validate the source and destination before the first output mutation.</summary>
public static class GenerationDestination
{
    public static void Validate(GameContextOptions game, string output)
    {
        RunLimits.Check();
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Choose an output folder.");
        var full = HostPath.Canonical(output);
        var roots = new List<string>();
        string gameFolder;
        if (game.Mode == GameSourceMode.Mo2Instance)
        {
            var locations = game.Mo2Locations ?? new Mo2Locations();
            if (game.WinePrefix is not null) locations = locations with { WinePrefix = game.WinePrefix };
            var instance = Mo2Instance.Open(game.Mo2InstanceFolder ?? throw new ArgumentException("Choose an MO2 instance."), locations);
            var profile = instance.OpenProfile(game.Mo2Profile);
            gameFolder = instance.GamePath;
            if (HostPath.IsWithin(full, instance.ProfilesFolder) || HostPath.IsWithin(full, instance.OverwriteFolder))
                throw new InvalidOperationException("Choose a dedicated output mod outside MO2's profiles and overwrite folders.");
            roots.AddRange([instance.InstanceFolder, instance.ModsFolder, instance.ProfilesFolder, instance.OverwriteFolder]);
            foreach (var mod in profile.EnabledModFolders())
            {
                RunLimits.Check();
                if (HostPath.IsWithin(full, mod) && HostPath.Canonical(mod) != full)
                    throw new InvalidOperationException("The output must not be inside an existing input mod: " + mod);
            }
        }
        else
        {
            string data;
            if (game.DataFolder is { } selected) data = GameContext.ResolveDataFolder(selected, []);
            else if (OperatingSystem.IsWindows()) { using var context = GameContext.Open(game); data = context.DataFolder; }
            else throw new ArgumentException("Choose the game Data folder.");
            gameFolder = Path.GetDirectoryName(data)!;
            if (game.PluginsTxt is { Length: > 0 } plugins && !File.Exists(plugins)) throw new FileNotFoundException("plugins.txt not found: " + plugins);
        }
        roots.Add(gameFolder);
        if (HostPath.IsWithin(full, gameFolder) || roots.Any(root => HostPath.IsWithin(root, full)))
            throw new InvalidOperationException("Choose a separate output mod folder, outside the game and away from mod-manager roots.");
        if (OutputFolder.WhyNotToClear(full) is { } reason)
            throw new InvalidOperationException("This folder is not an empty or recognized AnvilLOD output: " + reason);
    }
}
