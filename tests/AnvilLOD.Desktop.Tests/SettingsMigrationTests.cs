using System.Text.Json;
using AnvilLOD.Desktop.Models;
using AnvilLOD.Desktop.Services;

namespace AnvilLOD.Desktop.Tests;

public sealed class SettingsMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PenguLOD-migration-" + Guid.NewGuid().ToString("N"));
    private string Current => Path.Combine(_root, "PenguLOD", "settings.json");
    private string Legacy => Path.Combine(_root, "AnvilLOD", "settings.json");
    private string LegacyCache => Path.Combine(_root, "old-cache");
    private SettingsStore Store() => new(Current, Legacy, LegacyCache);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Theory]
    [InlineData("")]
    [InlineData("/custom/cache")]
    public void ImportsOnceAndPreservesOriginalPathsAndPreferences(string cache)
    {
        var original = new DesktopSettings {
            Instance = "/mods/Fluorine", Profile = "My profile", WinePrefix = "/wine/pfx",
            OutputFolder = "/output/AnvilLOD", GrassOutput = "/output/GrassCache",
            TextureGenOutput = "/output/AnvilTexGen", CacheFolder = cache,
            UseGeneratedGrass = true, UseGeneratedTextures = true };
        original.Options["grassTop"] = JsonSerializer.SerializeToElement(.7m);
        original.Options["occEyes"] = JsonSerializer.SerializeToElement(2m);
        SettingsStore.Write(Legacy, original);
        byte[] before = File.ReadAllBytes(Legacy);
        var store = Store();
        var migrated = store.Load();
        Assert.Equal(Legacy, store.MigratedFrom);
        Assert.Equal(before, File.ReadAllBytes(Legacy));
        original.CacheFolder = cache.Length == 0 ? LegacyCache : cache;
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(migrated));
        Assert.Equal(JsonSerializer.Serialize(migrated), JsonSerializer.Serialize(SettingsStore.Read(Current)));

        // Changing the old app's settings never overwrites the new app's settings.
        SettingsStore.Write(Legacy, new DesktopSettings { Profile = "Changed later" });
        var second = Store();
        Assert.Equal("My profile", second.Load().Profile);
        Assert.Null(second.MigratedFrom);
    }

    [Fact]
    public void ExistingPenguLodSettingsTakePrecedenceEvenIfLegacyIsCorrupt()
    {
        SettingsStore.Write(Current, new DesktopSettings { Profile = "PenguLOD" });
        Directory.CreateDirectory(Path.GetDirectoryName(Legacy)!);
        File.WriteAllText(Legacy, "broken legacy settings");
        Assert.Equal("PenguLOD", Store().Load().Profile);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"SchemaVersion\":99}")]
    [InlineData("{\"Options\":null}")]
    public void InvalidLegacySettingsAreNotSilentlyReplaced(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Legacy)!);
        File.WriteAllText(Legacy, content);
        Assert.Throws<InvalidDataException>(() => Store().Load());
        Assert.False(File.Exists(Current));
        Assert.Equal(content, File.ReadAllText(Legacy));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void ExplicitSettingsFileDoesNotImportOrCreateDefaultSettings()
    {
        SettingsStore.Write(Legacy, new DesktopSettings { Profile = "Old" });
        var store = new SettingsStore(Current);
        Assert.Equal("", store.Load().Profile);
        Assert.Null(store.MigratedFrom);
        Assert.False(File.Exists(Current));
    }

    [Fact]
    public void NewSettingsUseUpstreamOcclusionDefaults()
    {
        var settings = Store().Load();
        var upstream = new AnvilLOD.Core.Occlusion.OcclusionSettings();
        Assert.Equal((decimal)upstream.ParentTargetPoints, GenerationOptions.Get<decimal>(settings, "occParentTargets"));
        Assert.Equal((decimal)upstream.BlockTargetPoints, GenerationOptions.Get<decimal>(settings, "occBlockTargets"));
        Assert.Equal((decimal)upstream.EyePoints, GenerationOptions.Get<decimal>(settings, "occEyes"));
        Assert.Null(Store().MigratedFrom);
    }
}
