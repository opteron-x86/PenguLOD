using System.Security.Cryptography;
using System.Text;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins.Mo2;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Plugins;

/// <summary>A disposable, case-normalized Data view. Only file links are made; source folders are never modified.</summary>
internal sealed record GrassGenerationInput(string Data, string Plugins, string Inis, string Ngio,
    string Source, string WorldArgument, string LoadOrderFingerprint);

public static partial class GrassGeneration
{
    private static readonly string[] InputRoots = ["meshes", "skse\\plugins", "seasons"];

    internal static GrassGenerationInput PrepareInput(GrassGenerationRequest req, string work,
        IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Preparing the selected profile for FasterNGIO...");
        var options = req.Game with { ExcludeFolder = req.OutputFolder, ExtraLooseRoots = InputRoots };
        using var game = GameContext.Open(options, progress);
        if (game.MissingPlugins.Count > 0) throw new InvalidOperationException("Enabled plugins are missing: " + string.Join(", ", game.MissingPlugins.Take(10)));
        var runtime = RuntimeFormIds.FromLoadOrder(game.LoadOrder);
        var worlds = game.LoadOrder.PriorityOrder.Worldspace().WinningOverrides()
            .Where(w => !w.IsDeleted && !string.IsNullOrEmpty(w.EditorID)).ToArray();
        string selection;
        var names = SplitWorlds(req.Worldspaces);
        if (names.Length == 1 && names[0].Equals("all", StringComparison.OrdinalIgnoreCase)) selection = "all";
        else
        {
            var ids = new List<string>();
            foreach (var name in names)
            {
                var world = worlds.FirstOrDefault(w => w.EditorID!.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || ("0x" + runtime.Resolve(w.FormKey).ToString("X")).Equals(name, StringComparison.OrdinalIgnoreCase));
                if (world is null) throw new ArgumentException("Unknown grass worldspace: " + name + ". Use its editor ID (such as Tamriel) or all.");
                ids.Add("0x" + runtime.Resolve(world.FormKey).ToString("X8"));
            }
            selection = string.Join(',', ids);
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        Mo2Profile? profile = null;
        var inis = new List<string>();
        string? ccc;
        if (req.Game.Mode == GameSourceMode.Mo2Instance)
        {
            var locations = (req.Game.Mo2Locations ?? new Mo2Locations()) with { WinePrefix = req.Game.WinePrefix ?? req.Game.Mo2Locations?.WinePrefix };
            var instance = Mo2Instance.Open(req.Game.Mo2InstanceFolder!, locations);
            profile = instance.OpenProfile(req.Game.Mo2Profile);
            var index = VirtualDataIndex.Build(profile, InputRoots, req.OutputFolder, req.Game.ExcludeFolders);
            if (index.MissingModFolders > 0) throw new InvalidOperationException($"{index.MissingModFolders} enabled mod folders are missing; fix the selected profile before caching grass.");
            foreach (var name in index.RootFileNames) if (RootInput(name)) files[name] = index.ResolveRootFile(name)!;
            foreach (var root in InputRoots)
                foreach (var path in index.EnumerateLoose(root + "\\"))
                    if (LooseInput(path)) files[path] = index.ResolveLoose(path)!;
            if (profile.UsesLocalInis) inis.Add(profile.Folder);
            ccc = GamePath.ResolveExisting(instance.GamePath, "Skyrim.ccc");
        }
        else
        {
            foreach (var file in Directory.EnumerateFiles(game.DataFolder))
                if (RootInput(Path.GetFileName(file))) files[Path.GetFileName(file).ToLowerInvariant()] = file;
            foreach (var root in InputRoots)
            {
                var dir = GamePath.ResolveExisting(game.DataFolder, root);
                if (dir is null) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var path = GamePath.Normalize(Path.GetRelativePath(game.DataFolder, file));
                    if (LooseInput(path)) files[path] = file;
                }
            }
            if (req.Game.PluginsTxt is { } plugins) inis.Add(Path.GetDirectoryName(plugins)!);
            ccc = GamePath.ResolveExisting(Path.GetDirectoryName(game.DataFolder)!, "Skyrim.ccc");
        }
        ct.ThrowIfCancellationRequested();
        var data = Path.Combine(work, "Data");
        var iniDir = Path.Combine(work, "ini");
        Directory.CreateDirectory(data); Directory.CreateDirectory(iniDir);
        int linked = 0;
        foreach (var (relative, source) in files)
        {
            ct.ThrowIfCancellationRequested();
            var destination = GamePath.ToFileSystemPath(data, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.CreateSymbolicLink(destination, Path.GetFullPath(source));
            if (++linked % 25000 == 0) progress?.Report($"Prepared {linked:N0} of {files.Count:N0} input files...");
        }
        if (ccc is not null) File.Copy(ccc, Path.Combine(work, "Skyrim.ccc"));

        // FasterNGIO prepends base masters/CC itself. Preserve the profile's resolved order, but
        // do not turn implicit masters into explicit entries (that would activate their plugin INIs).
        HashSet<string> explicitPlugins;
        if (profile is not null) explicitPlugins = new(profile.ExplicitlyEnabled, StringComparer.OrdinalIgnoreCase);
        else explicitPlugins = File.ReadLines(req.Game.PluginsTxt!).Select(l => l.Trim()).Where(l => l.StartsWith('*'))
            .Select(l => l[1..].Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pluginFile = Path.Combine(work, "plugins.txt");
        File.WriteAllLines(pluginFile, game.LoadOrder.ListedOrder.Where(l => l.Enabled && explicitPlugins.Contains(l.ModKey.FileName.String))
            .Select(l => "*" + l.ModKey.FileName.String), new UTF8Encoding(false));
        if (req.Game.GameIniPath is { } ini) inis.Insert(0, Path.GetDirectoryName(ini)!);
        inis.AddRange(HostPath.GameDocuments(req.Game.WinePrefix, req.Game.Release == Mutagen.Bethesda.GameRelease.SkyrimVR));
        foreach (var name in new[] { "Skyrim.ini", "SkyrimCustom.ini" })
        {
            var found = name == "Skyrim.ini" && req.Game.GameIniPath is { } chosen ? chosen
                : inis.Select(dir => GamePath.ResolveExisting(dir, name)).FirstOrDefault(File.Exists);
            if (found is null && name == "Skyrim.ini") found = GamePath.ResolveExisting(Path.GetDirectoryName(game.DataFolder)!, "Skyrim_Default.ini");
            if (found is null) continue;
            File.Copy(found, Path.Combine(iniDir, name));
            progress?.Report($"Grass settings: {found}");
        }
        var ngio = req.NgioConfig ?? (files.TryGetValue("skse\\plugins\\grasscontrol.ini", out var config) ? config : "none");
        if (ngio != "none")
        {
            File.Copy(ngio, Path.Combine(work, "GrassControl.ini"));
            progress?.Report("NGIO settings: " + ngio);
            ngio = Path.Combine(work, "GrassControl.ini");
        }
        else progress?.Report("No GrassControl.ini found; FasterNGIO will use its built-in defaults.");
        progress?.Report($"Input ready: {files.Count:N0} winning files; source files remain read-only inputs.");
        return new(data, pluginFile, iniDir, ngio, game.SourceDescription, selection, LoadOrderFingerprint(game));
    }

    internal static string LoadOrderFingerprint(GameContext game) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', game.LoadOrder.ListedOrder.Where(l => l.Enabled && l.Mod is not null).Select(l => l.ModKey.FileName.String.ToLowerInvariant())))));

    private static bool RootInput(string path) => !path.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)
        && new[] { ".esm", ".esp", ".esl", ".bsa", ".ini" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    private static bool LooseInput(string path) => !path.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)
        && (path.StartsWith("meshes\\", StringComparison.Ordinal) && path.EndsWith(".nif", StringComparison.Ordinal)
            || path.StartsWith("seasons\\", StringComparison.Ordinal) && path.EndsWith(".ini", StringComparison.Ordinal)
            || path.StartsWith("skse\\plugins\\", StringComparison.Ordinal) && (path.EndsWith(".ini", StringComparison.Ordinal)
                || path is "skse\\plugins\\po3_seasonsofskyrim.dll" or "skse\\plugins\\po3_tweaks.dll"));
}
