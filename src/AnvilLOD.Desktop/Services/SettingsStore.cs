using System.Text.Json;
using AnvilLOD.Desktop.Models;

namespace AnvilLOD.Desktop.Services;

public sealed class SettingsStore(string? file = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string FilePath { get; } = file ?? Path.Combine(ConfigRoot, "settings.json");
    public static string ConfigRoot => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnvilLOD");
    public static string CacheRoot => Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "AnvilLOD");

    public DesktopSettings Load()
    {
        if (!File.Exists(FilePath)) return new DesktopSettings();
        return Read(FilePath);
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
    public static void Write(string file, DesktopSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, file, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
