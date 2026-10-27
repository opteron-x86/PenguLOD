using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnvilLOD.Core.World;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Paths;

namespace AnvilLOD.Plugins;

public sealed class TextureGenerationManifest
{
    public const string FileName = "AnvilLOD.textures.json";
    public int Version { get; set; } = 1;
    public bool Complete { get; set; }
    public string Source { get; set; } = "";
    public string ModelFilter { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
        Files.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value)))));

    public static TextureGenerationManifest? Read(string folder, bool requireComplete = false)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            if (requireComplete) throw new InvalidOperationException("Generate textures successfully first: " + folder);
            return null;
        }
        var result = JsonSerializer.Deserialize<TextureGenerationManifest>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid texture manifest: " + path);
        if (result.Version != 1 || result.Files is null) throw new InvalidDataException("Unsupported texture manifest: " + path);
        result.Files = new(result.Files, StringComparer.OrdinalIgnoreCase);
        result.ModelFilter ??= "";
        foreach (var file in result.Files.Keys)
        {
            GamePath.ValidateRelative(file);
            if (!file.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid generated texture path: " + file);
        }
        if (requireComplete && (!result.Complete || result.Files.Count == 0))
            throw new InvalidOperationException("The texture output is incomplete. Run texture generation again before generating LOD.");
        if (requireComplete && result.ModelFilter.Length > 0)
            throw new InvalidOperationException("This is filtered preview output. Clear the model filter and generate a full texture set before generating LOD.");
        return result;
    }

    public void Verify(string folder, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); RunLimits.Check();
        var watch = Stopwatch.StartNew();
        int checkedFiles = 0;
        double lastReport = 0;
        progress?.Report($"Verifying generated textures: 0/{Files.Count:N0} files...");
        foreach (var (relative, hash) in Files)
        {
            ct.ThrowIfCancellationRequested(); RunLimits.Check();
            var path = GamePath.ToFileSystemPath(folder, relative);
            if (!HostPath.IsWithin(path, folder) || !File.Exists(path) || new FileInfo(path).LinkTarget is not null)
                throw new IOException("Generated texture is missing or linked outside its output; regenerate textures: " + path);
            using var input = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Generated texture was changed; regenerate textures: " + path);
            checkedFiles++;
            if (watch.Elapsed.TotalSeconds - lastReport >= 1)
            {
                lastReport = watch.Elapsed.TotalSeconds;
                progress?.Report($"Verifying generated textures: {checkedFiles:N0}/{Files.Count:N0} files ({100.0 * checkedFiles / Files.Count:F0}%)...");
            }
        }
        ct.ThrowIfCancellationRequested(); RunLimits.Check();
        progress?.Report($"Verified generated textures: {checkedFiles:N0} files in {watch.Elapsed.TotalSeconds:F1}s.");
    }

    public void Save(string folder)
    {
        var file = Path.Combine(folder, FileName);
        if (new FileInfo(file).LinkTarget is not null || new FileInfo(file + ".tmp").LinkTarget is not null)
            throw new IOException("A texture manifest cannot be a symbolic link: " + file);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }

    public void Attach(AssetIndex assets, string folder, CancellationToken ct = default)
    {
        if (!Complete) throw new InvalidOperationException("The texture output is incomplete.");
        assets.SetOverrides(folder, Files.Keys, ct: ct);
    }
}
