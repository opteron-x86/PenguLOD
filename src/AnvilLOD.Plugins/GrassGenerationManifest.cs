using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;

namespace AnvilLOD.Plugins;

public sealed class GrassGenerationManifest
{
    public const string FileName = "AnvilLOD.grass.json";
    public int Version { get; set; } = 1;
    public bool Complete { get; set; }
    public bool Preview { get; set; }
    public string Source { get; set; } = "";
    public string LoadOrderFingerprint { get; set; } = "";
    public string Worldspaces { get; set; } = "";
    public string Placement { get; set; } = "";
    public GrassGenerationRequest? Settings { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public long Instances { get; set; }
    public int EmptyFiles { get; set; }
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
        Files.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value)))));

    internal static GrassGenerationManifest Create(string folder, string source, string loadOrder,
        GrassGenerationRequest req, CancellationToken ct)
    {
        var result = new GrassGenerationManifest { Source = source, LoadOrderFingerprint = loadOrder, Preview = req.Preview,
            Worldspaces = req.Worldspaces, Placement = req.Placement, Settings = req, GeneratedAt = DateTimeOffset.UtcNow };
        foreach (var file in Directory.EnumerateFiles(Path.Combine(folder, "grass"), "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = GamePath.Normalize(Path.GetRelativePath(folder, file));
            ValidatePath(relative); RejectLink(file, folder);
            using var input = File.OpenRead(file);
            using var sha = SHA256.Create();
            using var hashing = new CryptoStream(input, sha, CryptoStreamMode.Read);
            GrassCacheInfo info;
            try { info = GrassCacheValidation.Inspect(hashing, ct); }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException)
            { throw new InvalidDataException("Invalid generated grass cache: " + file + ": " + ex.Message, ex); }
            result.Instances += info.Instances;
            if (info.Instances == 0) result.EmptyFiles++;
            result.Files.Add(relative, Convert.ToHexString(sha.Hash!));
        }
        result.Complete = true;
        return result;
    }

    public static GrassGenerationManifest? Read(string folder, bool requireFull = false)
    {
        var file = Path.Combine(folder, FileName);
        if (!File.Exists(file))
        {
            if (requireFull) throw new InvalidOperationException("Generate a complete grass cache first: " + folder);
            return null;
        }
        RejectLink(file, folder);
        var result = JsonSerializer.Deserialize<GrassGenerationManifest>(File.ReadAllText(file)) ?? throw new InvalidDataException("Invalid grass cache manifest.");
        if (result.Version != 1 || !result.Complete || result.Files is null || result.Files.Count == 0)
            throw new InvalidDataException("The grass cache output is incomplete. Generate into a fresh folder.");
        foreach (var path in result.Files.Keys) ValidatePath(path);
        if (requireFull && result.Preview) throw new InvalidOperationException("A small-area grass preview cannot be used for full LOD generation. Generate a complete cache first.");
        return result;
    }

    public void Verify(string folder, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); RunLimits.Check();
        var watch = Stopwatch.StartNew();
        var lookup = new GamePathLookup(folder);
        int checkedFiles = 0;
        double lastReport = 0;
        progress?.Report($"Verifying generated grass cache: 0/{Files.Count:N0} files...");
        var files = new List<(string Path, string Hash, DateTime WrittenAt)>(Files.Count);
        foreach (var (relative, hash) in Files)
        {
            ct.ThrowIfCancellationRequested(); RunLimits.Check();
            var path = lookup.ResolveExisting(relative, ct) ?? GamePath.ToFileSystemPath(folder, relative); RejectLink(path, folder);
            files.Add((path, hash, File.GetLastWriteTimeUtc(path)));
            if (watch.Elapsed.TotalSeconds - lastReport >= 1)
            {
                lastReport = watch.Elapsed.TotalSeconds;
                progress?.Report($"Locating generated grass cache: {files.Count:N0}/{Files.Count:N0} files...");
            }
        }
        // Directory enumeration is hash-ordered on ext4. Reading in approximate write order avoids
        // seeking across the disk for every tiny cell file on a rotational output drive.
        foreach (var (path, hash, _) in files.OrderBy(f => f.WrittenAt))
        {
            ct.ThrowIfCancellationRequested(); RunLimits.Check();
            using var input = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Generated grass cache was changed; regenerate it: " + path);
            checkedFiles++;
            if (watch.Elapsed.TotalSeconds - lastReport >= 1)
            {
                lastReport = watch.Elapsed.TotalSeconds;
                progress?.Report($"Verifying generated grass cache: {checkedFiles:N0}/{Files.Count:N0} files ({100.0 * checkedFiles / Files.Count:F0}%)...");
            }
        }
        ct.ThrowIfCancellationRequested(); RunLimits.Check();
        progress?.Report($"Verified generated grass cache: {checkedFiles:N0} files in {watch.Elapsed.TotalSeconds:F1}s.");
    }

    public void Attach(AssetIndex assets, string folder, GameContext game, CancellationToken ct = default)
    {
        if (!Complete || Preview) throw new InvalidOperationException("A complete grass cache is required for LOD.");
        ValidateLoadOrder(game);
        assets.SetOverrides(folder, Files.Keys, replacePrefix: "grass\\", ct: ct);
    }

    internal void ValidateLoadOrder(GameContext game)
    {
        if (GrassGeneration.LoadOrderFingerprint(game) != LoadOrderFingerprint)
            throw new InvalidOperationException("The plugin load order has changed since grass caching. Regenerate the grass cache, or disable 'Use generated grass cache for LOD'.");
    }

    internal void Save(string folder) => File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    internal static void CheckDestination(string folder, bool preview, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); RunLimits.Check();
        progress?.Report("Checking grass cache output...");
        if (new DirectoryInfo(folder).LinkTarget is not null) throw new IOException("Choose a real output folder, not a symbolic link.");
        if (!Directory.Exists(folder)) return;
        var old = Read(folder);
        if (old is null)
        {
            if (Directory.EnumerateFileSystemEntries(folder).Any(p => Path.GetFileName(p) != "meta.ini"))
                throw new IOException("Choose an empty grass output folder. Existing caches and other AnvilLOD output cannot be replaced by this job.");
            return;
        }
        if (preview && !old.Preview) throw new ArgumentException("Choose a separate output folder for the small-area preview; it cannot replace a complete cache.");
        var watch = Stopwatch.StartNew();
        int checkedEntries = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested(); RunLimits.Check();
            checkedEntries++;
            if (watch.Elapsed.TotalSeconds >= 1)
            {
                progress?.Report($"Checking grass cache output: {checkedEntries:N0} entries...");
                watch.Restart();
            }
            RejectLink(path, folder);
            var relative = GamePath.Normalize(Path.GetRelativePath(folder, path));
            if (Directory.Exists(path))
            {
                if (relative != "grass") throw new IOException("Unexpected directory in grass output: " + path);
                continue;
            }
            if (relative is "meta.ini" or "fasterngio.log" or "readme-grass.txt" or "anvillod.grass.json" || old.Files.ContainsKey(relative)) continue;
            throw new IOException("Unexpected file in grass output; choose a fresh output folder: " + path);
        }
        old.Verify(folder, progress, ct);
    }

    private static void ValidatePath(string path)
    {
        GamePath.ValidateRelative(path);
        if (!path.StartsWith("grass\\", StringComparison.Ordinal) || path[6..].Contains('\\')
            || !GrassCache.TryParseFileName(path[6..], out _, out _, out _, out _))
            throw new InvalidDataException("Invalid generated grass path: " + path);
    }

    private static void RejectLink(string path, string folder)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.LinkTarget is not null || !HostPath.IsWithin(path, folder)) throw new IOException("Grass output contains a symbolic link or escaping path: " + path);
    }
}
