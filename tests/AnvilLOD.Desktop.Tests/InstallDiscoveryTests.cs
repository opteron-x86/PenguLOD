using AnvilLOD.Desktop.Services;
using AnvilLOD.Testing;

namespace AnvilLOD.Desktop.Tests;

public sealed class InstallDiscoveryTests
{
    [Fact]
    public void NativeWindowsDiscoveryUsesLibrariesAndLocalPluginListWithoutProton()
    {
        using var fixture = new TextureFixture();
        string steam = Path.Combine(fixture.Root, "Steam"), library = Path.Combine(fixture.Root, "Games forêt");
        string local = Path.Combine(fixture.Root, "LocalAppData");
        string game = Path.Combine(library, "steamapps", "common", "Skyrim Special Edition");
        string plugins = Path.Combine(local, "Skyrim Special Edition", "plugins.txt");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(game, "Data"));
        Directory.CreateDirectory(Path.GetDirectoryName(plugins)!);
        File.WriteAllText(plugins, "*Skyrim.esm\n");
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" } }");
        var found = Assert.Single(InstallDiscovery.Find([steam], local));
        Assert.Equal(game, found.GameFolder);
        Assert.Equal(plugins, found.PluginsFile);
        Assert.Empty(found.Prefix);
    }
}
