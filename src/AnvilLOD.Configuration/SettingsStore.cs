using System.Text.Json;
using AnvilLOD.Desktop.Models;

namespace AnvilLOD.Desktop.Services;

public sealed class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string FilePath { get; }
    public string? MigratedFrom { get; private set; }
    private readonly string? _legacyFile;
    private readonly string _legacyCacheRoot;
    private static string ConfigBase => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } path
        ? path : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string CacheBase => Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } path
        ? path : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
    public static string ConfigRoot => Path.Combine(ConfigBase, "PenguLOD");
    public static string CacheRoot => Path.Combine(CacheBase, "PenguLOD");

    public SettingsStore(string? file = null) : this(file ?? Path.Combine(ConfigRoot, "settings.json"),
        file is null ? Path.Combine(ConfigBase, "AnvilLOD", "settings.json") : null,
        Path.Combine(CacheBase, "AnvilLOD")) { }

    internal SettingsStore(string file, string? legacyFile, string legacyCacheRoot)
    { FilePath = file; _legacyFile = legacyFile; _legacyCacheRoot = legacyCacheRoot; }

    public DesktopSettings Load()
    {
        if (File.Exists(FilePath)) return Read(FilePath);
        if (_legacyFile is null || !File.Exists(_legacyFile)) return new DesktopSettings();
        // Validate before writing anything. The original configuration remains untouched,
        // and existing run history/caches keep their old location after the rename.
        DesktopSettings settings;
        try { settings = Read(_legacyFile); }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        { throw new InvalidDataException($"Could not import AnvilLOD settings from {_legacyFile}: {e.Message}", e); }
        if (string.IsNullOrWhiteSpace(settings.CacheFolder)) settings.CacheFolder = _legacyCacheRoot;
        try { Write(FilePath, settings, overwrite: false); }
        catch (IOException) when (File.Exists(FilePath)) { return Read(FilePath); }
        MigratedFrom = _legacyFile;
        return settings;
    }
    public static DesktopSettings Read(string file)
    {
        var settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(file)) ?? throw new InvalidDataException("The settings file is empty.");
        if (settings.SchemaVersion != 1) throw new InvalidDataException($"Unsupported settings version: {settings.SchemaVersion}.");
        if (settings.Options is null) throw new InvalidDataException("The preset options are missing.");
        GenerationOptions.Validate(settings);
        return settings;
    }
    public void Save(DesktopSettings settings) => Write(FilePath, settings);
    public static void Write(string file, DesktopSettings settings) => Write(file, settings, overwrite: true);
    private static void Write(string file, DesktopSettings settings, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, file, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
