using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins.Mo2;

namespace AnvilLOD.Plugins;

public sealed record PipelineInputSnapshot(string Fingerprint, int Files, long Bytes, double Seconds, bool FullContentHashes);

/// <summary>Conservative input inventory. Plugins/configs are content-hashed; large assets can use metadata or strict hashes.</summary>
public static class PipelineInputs
{
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly HashSet<string> AssetRoots = new(StringComparer.OrdinalIgnoreCase)
        { "meshes", "textures", "lodsettings", "dyndolod", "grass", "seasons", "skse" };
    private static readonly HashSet<string> ContentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".esp", ".esm", ".esl", ".ini", ".json", ".txt", ".toml", ".lod", ".ccc" };

    public static string ExternalFingerprint(IEnumerable<string?> paths, bool strict, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) { hash.AppendData(Encoding.UTF8.GetBytes(value)); hash.AppendData([0]); }
        foreach (var path in paths.OfType<string>().Distinct(PathComparer).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested(); Add(Path.GetFullPath(path));
            if (File.Exists(path)) { Add(OutputSnapshot.HashFile(path, ct)); continue; }
            if (!Directory.Exists(path)) { Add("missing"); continue; }
            var pending = new Stack<string>(); pending.Push(path);
            var seen = new HashSet<string>(PathComparer);
            while (pending.TryPop(out var dir))
            {
                ct.ThrowIfCancellationRequested();
                if (!seen.Add(HostPath.Canonical(dir))) continue;
                foreach (var file in Directory.EnumerateFileSystemEntries(dir).Order(StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    if (Directory.Exists(file)) { pending.Push(file); continue; }
                    var info = new FileInfo(file); Add(Path.GetRelativePath(path, file));
                    Add(info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Add(strict || ContentExtensions.Contains(info.Extension) ? OutputSnapshot.HashFile(file, ct)
                        : info.LastWriteTimeUtc.Ticks + "|" + info.CreationTimeUtc.Ticks);
                }
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static PipelineInputSnapshot Capture(GameContextOptions game, IEnumerable<string> outputs,
        IEnumerable<string?> extraInputs, bool strict, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var timer = Stopwatch.StartNew(); var message = Stopwatch.StartNew();
        var exclusions = outputs.Select(HostPath.Canonical).ToArray();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool Excluded(string path) => exclusions.Any(e => path.Equals(e, comparison) || path.StartsWith(e + Path.DirectorySeparatorChar, comparison));
        var roots = new List<(string Path, int Mode)>();
        var singles = new List<string>();
        string data, gameFolder;
        if (game.Mode == GameSourceMode.Mo2Instance)
        {
            var locations = (game.Mo2Locations ?? new()) with { WinePrefix = game.WinePrefix ?? game.Mo2Locations?.WinePrefix };
            var instance = Mo2Instance.Open(game.Mo2InstanceFolder!, locations);
            var profile = instance.OpenProfile(game.Mo2Profile);
            data = instance.GameDataFolder; gameFolder = instance.GamePath;
            roots.Add((data, 1)); roots.AddRange(profile.EnabledModFolders().Select(p => (p, 1)));
            roots.Add((profile.Folder, 0)); singles.Add(Path.Combine(instance.InstanceFolder, "ModOrganizer.ini"));
        }
        else
        {
            data = GameContext.ResolveDataFolder(game.DataFolder ?? throw new ArgumentException("Choose the game Data folder."), []);
            gameFolder = Path.GetDirectoryName(data)!; roots.Add((data, 1));
            if (game.PluginsTxt is { } plugins) { singles.Add(plugins); roots.Add((Path.GetDirectoryName(Path.GetFullPath(plugins))!, 0)); }
        }
        singles.AddRange([Path.Combine(gameFolder, "Skyrim.ccc"), Path.Combine(gameFolder, "Skyrim_Default.ini")]);
        if (game.GameIniPath is { } ini) singles.Add(ini);
        foreach (var docs in HostPath.GameDocuments(game.WinePrefix, game.Release == Mutagen.Bethesda.GameRelease.SkyrimVR))
        { singles.Add(Path.Combine(docs, "Skyrim.ini")); singles.Add(Path.Combine(docs, "SkyrimCustom.ini")); }
        foreach (var input in extraInputs.OfType<string>().Where(p => !string.IsNullOrWhiteSpace(p)))
            if (Directory.Exists(input)) roots.Add((input, 2)); else singles.Add(input);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int files = 0; long bytes = 0;
        void Add(string value) { hash.AppendData(Encoding.UTF8.GetBytes(value)); hash.AppendData([0]); }
        void FileEntry(string file)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(file); info.Refresh();
            if (!info.Exists) { Add(file + "|missing"); return; }
            Add(Path.GetFullPath(file)); Add(info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            bool content = strict || ContentExtensions.Contains(info.Extension);
            Add(content ? OutputSnapshot.HashFile(file, ct) : info.LastWriteTimeUtc.Ticks + "|" + info.CreationTimeUtc.Ticks);
            files++; bytes += info.Length;
            if (message.Elapsed.TotalSeconds >= 1)
            { progress?.Report($"Inspecting pipeline inputs: {files:N0} files ({timer.Elapsed.TotalSeconds:F1}s)..."); message.Restart(); }
        }
        Add("pipeline-inputs-v1"); Add(JsonSerializer.Serialize(game));
        foreach (var (root, mode) in roots)
        {
            ct.ThrowIfCancellationRequested(); Add("layer:" + Path.GetFullPath(root));
            if (!Directory.Exists(root)) { Add("missing"); continue; }
            var pending = new Stack<(string Dir, bool Top)>(); pending.Push((root, true));
            var visited = new HashSet<string>(PathComparer);
            while (pending.TryPop(out var item))
            {
                ct.ThrowIfCancellationRequested();
                var canonical = HostPath.Canonical(item.Dir);
                if (Excluded(canonical) || !visited.Add(canonical)) continue;
                foreach (var path in Directory.EnumerateFileSystemEntries(item.Dir).Order(StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    if (Excluded(Path.GetFullPath(path))) continue;
                    if (Directory.Exists(path))
                    {
                        if (mode == 2 || mode == 1 && (!item.Top || AssetRoots.Contains(Path.GetFileName(path)))) pending.Push((path, false));
                    }
                    else if (!path.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)
                        && (mode != 0 || ContentExtensions.Contains(Path.GetExtension(path)))) FileEntry(path);
                }
            }
        }
        foreach (var file in singles.Distinct(PathComparer).Order(StringComparer.Ordinal)) FileEntry(file);
        var result = new PipelineInputSnapshot(Convert.ToHexString(hash.GetHashAndReset()), files, bytes, timer.Elapsed.TotalSeconds, strict);
        progress?.Report($"Pipeline input inventory: {files:N0} files in {result.Seconds:F1}s ({(strict ? "full content verification" : "plugin/config hashes and asset metadata")}).");
        return result;
    }
}
