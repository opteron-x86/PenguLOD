using PenguLOD.Plugins;
using PenguLOD.Testing;

namespace PenguLOD.Core.Tests;

/// <summary>What counts as a change to the load order when deciding whether finished stages can be reused.</summary>
public sealed class PipelineInputTests
{
    private sealed class Instance : IDisposable
    {
        private readonly TextureFixture _fixture = new();
        public string Folder => Path.Combine(_fixture.Root, "ModOrganizer");
        public string Profile => Path.Combine(Folder, "profiles", "Test");
        public string Ini => Path.Combine(Folder, "ModOrganizer.ini");
        public string Mod(string name, string relative) => Path.Combine(Folder, "mods", name, relative.Replace('\\', Path.DirectorySeparatorChar));
        public string Overwrite(string relative) => Path.Combine(Folder, "overwrite", relative.Replace('\\', Path.DirectorySeparatorChar));
        public GameContextOptions Game => new(Mode: GameSourceMode.Mo2Instance, Mo2InstanceFolder: Folder);

        public Instance()
        {
            Directory.CreateDirectory(Profile);
            File.WriteAllText(Ini, $"[General]\ngamePath={Path.GetDirectoryName(_fixture.Data)}\nselected_profile=Test\n");
            File.WriteAllText(Path.Combine(Profile, "modlist.txt"), "+Animations\n+Landscape\n-Unused\n");
            File.WriteAllText(Path.Combine(Profile, "plugins.txt"), "");
            File.WriteAllText(Path.Combine(Profile, "skyrim.ini"), "[Grass]\niMinGrassSize=72\n");
            File.WriteAllText(Path.Combine(Profile, "skyrimprefs.ini"), "[Display]\niSize W=1920\n");
            Write(Mod("Landscape", @"meshes\landscape\rock.nif"), "rock");
            Write(Mod("Landscape", @"SKSE\Plugins\GrassControl.ini"), "[Grass]\n");
            Write(Mod("Landscape", "Landscape_SWAP.ini"), "[Forms]\n");
            Write(Mod("Landscape", "meta.ini"), "[General]\nversion=1\n");
            Write(Mod("Animations", @"meshes\actors\character\behaviors\sprintbehavior.hkx"), "behaviour");
            Write(Mod("Animations", @"meshes\animationsetdatasinglefile.txt"), "sets");
            Write(Mod("Unused", @"meshes\unused.nif"), "unused");
            Write(Overwrite(@"SKSE\Plugins\Example.log"), "started");
        }

        public string Fingerprint() => PipelineInputs.Capture(Game, [Path.Combine(_fixture.Root, "Output")], [], false).Fingerprint;
        public static void Write(string file, string text) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }
        public void Dispose() => _fixture.Dispose();
    }

    [Fact]
    public void PlayingTheGameOrRunningAnAnimationToolKeepsFinishedWorkReusable()
    {
        using var instance = new Instance();
        string before = instance.Fingerprint();
        // Mod Organizer: its window state, and the notes it keeps beside each mod.
        File.AppendAllText(instance.Ini, "[Geometry]\nMainWindow_geometry=@ByteArray(1)\n");
        File.WriteAllText(instance.Mod("Landscape", "meta.ini"), "[General]\nversion=2\nlastNexusQuery=today\n");
        // The game: preferences, and what SKSE plugins write below their folder.
        File.WriteAllText(Path.Combine(instance.Profile, "skyrimprefs.ini"), "[Display]\niSize W=2560\n");
        Instance.Write(instance.Overwrite(@"SKSE\Plugins\Example.log"), "started\nstopped");
        Instance.Write(instance.Overwrite(@"SKSE\Plugins\Example.settings.json"), "{}");
        Instance.Write(instance.Overwrite(@"SKSE\Plugins\Example.ini"), "[Window]\nx=10\n");
        Instance.Write(instance.Overwrite(@"SKSE\Plugins\Menu\Settings\imgui.ini"), "[Window]\n");
        Instance.Write(instance.Overwrite(@"SKSE\Plugins\Cache\textures\wall_m.dds.key"), "1");
        // An animation tool: new behaviour files and the animation data lists.
        Instance.Write(instance.Mod("Animations", @"meshes\actors\character\behaviors\sprintbehavior.hkx"), "new behaviour");
        Instance.Write(instance.Mod("Animations", @"meshes\actors\character\behaviors\bashbehavior.hkx"), "added");
        Instance.Write(instance.Mod("Animations", @"meshes\animationsetdatasinglefile.txt"), "more sets");
        Instance.Write(instance.Mod("Animations", @"meshes\actors\character\facegen\head.tri"), "morphs");
        // A mod that is not enabled.
        Instance.Write(instance.Mod("Unused", @"meshes\unused.nif"), "changed and longer");
        Assert.Equal(before, instance.Fingerprint());
    }

    [Fact]
    public void EveryChangeAStageCanSeeStillDiscardsFinishedWork()
    {
        using var instance = new Instance();
        var seen = new HashSet<string> { instance.Fingerprint() };
        void Changed(string what) => Assert.True(seen.Add(instance.Fingerprint()), what + " did not change the fingerprint.");
        Instance.Write(instance.Mod("Landscape", @"meshes\landscape\rock.nif"), "a longer rock"); Changed("A model");
        Instance.Write(instance.Mod("Landscape", @"meshes\landscape\cliff.nif"), "cliff"); Changed("A new model");
        Instance.Write(instance.Mod("Landscape", @"SKSE\Plugins\GrassControl.ini"), "[Grass]\nUse-Grass-Cache=true\n"); Changed("The grass settings");
        Instance.Write(instance.Mod("Landscape", @"SKSE\Plugins\po3_SeasonsOfSkyrim.dll"), "dll"); Changed("An SKSE plugin whose presence is checked");
        Instance.Write(instance.Overwrite(@"SKSE\Plugins\po3_SeasonsOfSkyrim.ini"), "[Settings]\niSeasonType = 1\n"); Changed("The season settings");
        Instance.Write(instance.Mod("Landscape", "Landscape_SWAP.ini"), "[Forms]\n0x1~Skyrim.esm|0x2~Skyrim.esm\n"); Changed("A swap file");
        Instance.Write(instance.Mod("Landscape", "Landscape.esp"), "TES4"); Changed("A plugin");
        Instance.Write(instance.Overwrite(@"textures\landscape\dirt02.dds"), "dds"); Changed("A texture in Overwrite");
        File.WriteAllText(Path.Combine(instance.Profile, "skyrim.ini"), "[Grass]\niMinGrassSize=40\n"); Changed("The profile's Skyrim.ini");
        File.WriteAllText(Path.Combine(instance.Profile, "modlist.txt"), "+Animations\n+Landscape\n+Unused\n"); Changed("Enabling a mod");
        string moved = Path.Combine(instance.Folder, "elsewhere"); Directory.CreateDirectory(moved);
        File.AppendAllText(instance.Ini, $"[Settings]\nmod_directory={moved}\n"); Changed("Another mods folder");
    }

    [Theory]
    [InlineData("meta.ini", true)]
    [InlineData("Mod.esp", false)]
    [InlineData("Mod - Textures.bsa", false)]
    [InlineData("Mod_SWAP.ini", false)]
    [InlineData("install.log", true)]
    [InlineData(@"textures\meta.ini", false)]
    [InlineData(@"meshes\lod\rock_lod.nif", false)]
    [InlineData(@"meshes\actors\character\behaviors\0_master.HKX", true)]
    [InlineData(@"Meshes\AnimationDataSingleFile.txt", true)]
    [InlineData(@"textures\terrain\lodgen\animationdatasinglefile.txt", false)]
    [InlineData(@"textures\terrain\lodgen\skyrim.esm\tree_0001.txt", false)]
    [InlineData(@"SKSE\Plugins\GrassControl.ini", false)]
    [InlineData(@"skse\plugins\PO3_SeasonsOfSkyrim.ini", false)]
    [InlineData(@"SKSE\Plugins\po3_Tweaks.ini", false)]
    [InlineData(@"SKSE\Plugins\CommunityShaders_ImGui.ini", true)]
    [InlineData(@"skse/plugins/po3_BaseObjectSwapper.dll", false)]
    [InlineData(@"SKSE\Plugins\Quickslot.settings.json", true)]
    [InlineData(@"SKSE\Plugins\Menu\Settings\imgui.ini", true)]
    [InlineData(@"SKSE\Plugins\ScrambledBugs.log", true)]
    [InlineData(@"SKSE\skse.ini", true)]
    [InlineData(@"Seasons\MainFormSwap_WIN.ini", false)]
    [InlineData(@"DynDOLOD\DynDOLOD_Tamriel.json", false)]
    public void OnlyFilesNoStageReadsAreLeftOut(string relative, bool unread) => Assert.Equal(unread, PipelineInputs.Unread(relative));
}
