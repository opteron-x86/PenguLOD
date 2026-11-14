using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PenguLOD.Core.Paths;
using PenguLOD.Core.Pipeline;
using PenguLOD.Core.World;
using PenguLOD.Plugins.Mo2;

namespace PenguLOD.Plugins;

public sealed record PipelineInputSnapshot(string Fingerprint, int Files, long Bytes, double Seconds, bool FullContentHashes);

/// <summary>
/// Conservative input inventory. Plugins/configs are content-hashed; large assets can use metadata or strict hashes.
/// Files no stage reads are left out (<see cref="Unread"/>), so playing the game or running an animation tool between
/// runs does not discard finished work.
/// </summary>
public static class PipelineInputs
{
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly HashSet<string> AssetRoots = new(StringComparer.OrdinalIgnoreCase)
        { "meshes", "textures", "lodsettings", "dyndolod", "grass", "seasons", "skse" };
    private static readonly HashSet<string> ContentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".esp", ".esm", ".esl", ".ini", ".json", ".txt", ".toml", ".lod", ".ccc" };
    private static readonly HashSet<string> ProfileFiles = new(StringComparer.OrdinalIgnoreCase)
        { "plugins.txt", "loadorder.txt", "modlist.txt", "archives.txt", "lockedorder.txt" };
    // Havok animation and behaviour files, face morphs, and what tools leave behind.
    private static readonly HashSet<string> UnreadExtensions = new(StringComparer.OrdinalIgnoreCase) { ".hkx", ".tri", ".log", ".bak", ".tmp" };
    // The settings under SKSE\Plugins that a stage reads: grass (NGIO), seasons, and whether editor IDs are loaded.
    // A stage that starts reading another one must add it here, or a change to it will not rebuild that stage.
    private static readonly HashSet<string> SkseSettings = new(StringComparer.OrdinalIgnoreCase) { "GrassControl.ini", "po3_SeasonsOfSkyrim.ini", "po3_Tweaks.ini" };
    private static readonly HashSet<string> AnimationData = new(StringComparer.OrdinalIgnoreCase) { "animationdatasinglefile.txt", "animationsetdatasinglefile.txt" };

    /// <summary>
    /// True for a file in a game or mod folder that no stage reads: the mod manager's own <c>meta.ini</c>, animation data,
    /// logs, and everything under <c>SKSE</c> except the DLLs directly in <c>SKSE\Plugins</c> (their presence changes
    /// what is generated) and the grass and season settings there. SKSE plugins write logs, caches, menu state and
    /// their own settings below that folder while the game runs.
    /// </summary>
    /// <param name="relative">The path below the Data or mod folder.</param>
    internal static bool Unread(string relative)
    {
        string[] parts = relative.Split('/', '\\');
        string name = parts[^1], extension = Path.GetExtension(name);
        if (UnreadExtensions.Contains(extension)) return true;
        if (parts.Length == 1) return name.Equals("meta.ini", StringComparison.OrdinalIgnoreCase);
        if (parts[0].Equals("skse", StringComparison.OrdinalIgnoreCase))
            return parts.Length != 3 || !parts[1].Equals("plugins", StringComparison.OrdinalIgnoreCase)
                || !(extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) || SkseSettings.Contains(name));
        return parts[0].Equals("meshes", StringComparison.OrdinalIgnoreCase) && AnimationData.Contains(name);
    }

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
        string data, gameFolder; string? mo2 = null;
        if (game.Mode == GameSourceMode.Mo2Instance)
        {
            var locations = (game.Mo2Locations ?? new()) with { WinePrefix = game.WinePrefix ?? game.Mo2Locations?.WinePrefix };
            var instance = Mo2Instance.Open(game.Mo2InstanceFolder!, locations);
            var profile = instance.OpenProfile(game.Mo2Profile);
            data = instance.GameDataFolder; gameFolder = instance.GamePath;
            roots.Add((data, 1)); roots.AddRange(profile.EnabledModFolders().Select(p => (p, 1)));
            roots.Add((profile.Folder, 0));
            // Mod Organizer rewrites ModOrganizer.ini whenever it runs. Only the folders it resolves to matter here.
            mo2 = string.Join('|', new[] { instance.GamePath, instance.ModsFolder, instance.ProfilesFolder, instance.OverwriteFolder, profile.Folder }.Select(Path.GetFullPath));
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
        Add("pipeline-inputs-v2"); Add(JsonSerializer.Serialize(game));
        if (mo2 is not null) Add("mo2:" + mo2);
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
                    else if (!path.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase) && mode switch
                    {
                        // The game rewrites its preferences file on exit, and no stage reads it.
                        0 => ProfileFiles.Contains(Path.GetFileName(path)) || Path.GetExtension(path).ToLowerInvariant() is ".ini" or ".toml"
                            && !Path.GetFileName(path).Equals("skyrimprefs.ini", StringComparison.OrdinalIgnoreCase),
                        1 => !Unread(Path.GetRelativePath(root, path)),
                        _ => true,
                    }) FileEntry(path);
                }
            }
        }
        foreach (var file in singles.Distinct(PathComparer).Order(StringComparer.Ordinal)) FileEntry(file);
        var result = new PipelineInputSnapshot(Convert.ToHexString(hash.GetHashAndReset()), files, bytes, timer.Elapsed.TotalSeconds, strict);
        progress?.Report($"Pipeline input inventory: {files:N0} files in {result.Seconds:F1}s ({(strict ? "full content verification" : "plugin/config hashes and asset metadata")}).");
        return result;
    }
}
