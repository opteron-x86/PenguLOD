using System.Text.Json;
using PenguLOD.Desktop.Models;
using PenguLOD.Desktop.Services;
using PenguLOD.Testing;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PenguLOD.Desktop.Tests;

public sealed class SettingsBehaviorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PenguLOD-settings-tests-" + Guid.NewGuid().ToString("N"));
    public SettingsBehaviorTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private MainWindow Window() => new(new SettingsStore(Path.Combine(_root, "settings.json")));
    private static void Show(MainWindow w, string page)
    {
        w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        w.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == page).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
        // Object LOD groups start collapsed; their controls are only in the visual tree once expanded.
        foreach (var group in w.GetVisualDescendants().OfType<Expander>().ToArray()) group.IsExpanded = true;
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
    }
    private static CheckBox Check(MainWindow w, string label) =>
        w.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == label);

    [Fact]
    public void EveryStageRequestCarriesTheRunSettings()
    {
        using var f = new TextureFixture();
        var s = new DesktopSettings { SourceMode = "Data folder / Vortex", GameFolder = f.Data, PluginsFile = f.Plugins,
            TextureGenOutput = Path.Combine(f.Root, "textures"), TerrainOutput = Path.Combine(f.Root, "terrain"),
            PipelinePreflight = false, TextureCompute = "Auto", ExpandOutputScope = true, ResumeCompletedStages = false, VerifySourceContents = true };
        foreach (var request in new[] { RequestFactory.Stages(s, textures: true), RequestFactory.Stages(s, terrain: true) })
        {
            Assert.False(request.Preflight); Assert.Equal("Auto", request.TextureCompute); Assert.True(request.ExpandOutputScope);
            Assert.False(request.Resume); Assert.True(request.VerifySourceContents);
        }
        s.PipelineGrass = false; s.PipelineTextures = true; s.PipelineTerrain = false; s.PipelineLod = false;
        Assert.Equal(JsonSerializer.Serialize(RequestFactory.Stages(s, textures: true)), JsonSerializer.Serialize(RequestFactory.Pipeline(s)));
    }

    [Fact]
    public void WholeNumberOptionsRoundAndValidate()
    {
        var s = new DesktopSettings();
        var value = new OptionValue(GenerationOptions.All.Single(o => o.Key == "snowMode"), s, () => { });
        Assert.True(value.Spec.Whole);
        value.Number = 0.05m; Assert.Equal(0m, value.Number);
        value.Number = 1.6m; Assert.Equal(2m, value.Number);
        Assert.False(GenerationOptions.All.Single(o => o.Key == "grassDensity").Whole);
        foreach (string key in GenerationOptions.WholeNumbers) Assert.Contains(GenerationOptions.All, o => o.Key == key && o.Default is decimal);

        s.Options["mapLevel"] = JsonSerializer.SerializeToElement(-1.95m);
        Assert.Throws<ArgumentException>(() => GenerationOptions.Validate(s));
        string file = Path.Combine(_root, "fractional.json");
        SettingsStore.Write(file, s);
        // A preset saved by a build that stepped these options by 0.05 still loads.
        Assert.Equal(-2, GenerationOptions.Get<decimal>(SettingsStore.Read(file), "mapLevel"));

        s.Options["mapLevel"] = JsonSerializer.SerializeToElement(-2m);
        s.Options["undersideStep"] = JsonSerializer.SerializeToElement("4");
        SettingsStore.Write(file, s);
        // Underside step 4 is no longer offered; such presets load with the finest valid step.
        Assert.Equal("8", GenerationOptions.Get<string>(SettingsStore.Read(file), "undersideStep"));
    }

    [Fact]
    public void PresetResetAndSharedOptionsNotifyEveryControl()
    {
        var s = new DesktopSettings();
        var spec = GenerationOptions.All.Single(o => o.Key == "seasons");
        var a = new OptionValue(spec, s, () => { }); var b = new OptionValue(spec, s, () => { });
        var notified = new List<string>();
        b.PropertyChanged += (_, e) => notified.Add("b"); s.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);
        a.Flag = true;
        Assert.True(b.Flag); Assert.True(s.Seasons); Assert.Contains("b", notified); Assert.Contains(nameof(DesktopSettings.Seasons), notified);
        notified.Clear(); s.ResetOptions();
        Assert.False(b.Flag); Assert.Contains("b", notified); Assert.Contains(nameof(DesktopSettings.Seasons), notified);
        notified.Clear(); s.Seasons = true;
        Assert.True(a.Flag); Assert.Contains("b", notified);
    }

    [AvaloniaFact]
    public void SeasonsCheckboxesOnTerrainAndObjectLodStayInSync()
    {
        var window = Window(); window.Show();
        try
        {
            Show(window, "Terrain");
            var terrain = Check(window, "Generate seasonal terrain from Seasons of Skyrim swaps");
            terrain.IsChecked = true; Dispatcher.UIThread.RunJobs();
            Show(window, "Object LOD");
            var objects = Check(window, "Seasonal LOD");
            Assert.True(objects.IsChecked);
            objects.IsChecked = false; Dispatcher.UIThread.RunJobs();
            Show(window, "Terrain");
            Assert.False(Check(window, "Generate seasonal terrain from Seasons of Skyrim swaps").IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void IntegerOptionsSpinByOne()
    {
        var window = Window(); window.Show();
        try
        {
            Show(window, "Object LOD");
            var spinners = window.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            var snow = spinners.Single(n => n.Parent is StackPanel p && p.Children.OfType<TextBlock>().Any(t => t.Text == "Snow shader"));
            Assert.Equal(1m, snow.Increment);
            var density = spinners.Single(n => n.Parent is StackPanel p && p.Children.OfType<TextBlock>().Any(t => t.Text == "Grass density"));
            Assert.Equal(.05m, density.Increment);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void Mo2GameFolderIsSeparateFromTheDataFolder()
    {
        using var f = new TextureFixture();
        var s = new DesktopSettings { SourceMode = "Data folder / Vortex", GameFolder = f.Data, PluginsFile = f.Plugins, Instance = f.Root };
        s.SourceMode = "Mod Organizer 2";
        var game = RequestFactory.Game(s);
        Assert.Null(game.Mo2Locations!.GamePath); Assert.Null(game.DataFolder);
        s.Mo2GameFolder = Path.Combine(f.Root, "Stock Game");
        Assert.Equal(s.Mo2GameFolder, RequestFactory.Game(s).Mo2Locations!.GamePath);
        s.SourceMode = "Data folder / Vortex";
        Assert.Equal(f.Data, RequestFactory.Game(s).DataFolder);
    }

    [Theory]
    [InlineData("Mod Organizer 2", true)]
    [InlineData("Data folder / Vortex", false)]
    public void OlderSettingsMoveTheMo2OverrideOutOfGameFolder(string mode, bool moved)
    {
        string file = Path.Combine(_root, "old.json");
        var old = JsonSerializer.SerializeToNode(new DesktopSettings { SourceMode = mode, GameFolder = "/games/Stock Game" })!.AsObject();
        old.Remove(nameof(DesktopSettings.Mo2GameFolder));
        File.WriteAllText(file, old.ToJsonString());
        var read = SettingsStore.Read(file);
        Assert.Equal(moved ? "" : "/games/Stock Game", read.GameFolder);
        Assert.Equal(moved ? "/games/Stock Game" : "", read.Mo2GameFolder);
        // Once saved with the new field, a Data-folder path kept in MO2 mode is not moved again.
        read.GameFolder = "/games/Skyrim"; read.SourceMode = "Mod Organizer 2"; SettingsStore.Write(file, read);
        Assert.Equal("/games/Skyrim", SettingsStore.Read(file).GameFolder);
    }
}
