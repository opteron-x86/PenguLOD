using System.Text.Json;
using PenguLOD.Desktop;
using PenguLOD.Desktop.Models;
using PenguLOD.Desktop.Services;
using PenguLOD.Plugins;
using PenguLOD.Testing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

[assembly: AvaloniaTestApplication(typeof(PenguLOD.Desktop.Tests.TestApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace PenguLOD.Desktop.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class DesktopTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PenguLOD-desktop-tests-" + Guid.NewGuid().ToString("N"));
    public DesktopTests()
    {
        Directory.CreateDirectory(_root);
        // Each screenshot-producing test must work on a fresh checkout, in any order.
        if (Environment.GetEnvironmentVariable("PENGULOD_SCREENSHOTS") is { } screenshots)
            Directory.CreateDirectory(screenshots);
    }
    public void Dispose() => Directory.Delete(_root, true);
    private MainWindow Window() => new(new SettingsStore(Path.Combine(_root, "settings.json")));
    private static Button FindButton(MainWindow w, string text) => w.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text);
    private static void Click(MainWindow w, string text)
    { w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); FindButton(w, text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static async Task WaitFor(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), "Timed out waiting for the UI job.");
    }
    private static T LabeledInput<T>(MainWindow window, string label) where T : Control =>
        window.GetVisualDescendants().OfType<StackPanel>()
            .Single(p => p.Children.OfType<TextBlock>().Any(t => t.Text == label) && p.Children.OfType<T>().Any())
            .Children.OfType<T>().Single();

    [AvaloniaFact]
    public void WorkflowStageOutputsStayInSyncAndOnlySelectedStagesRun()
    {
        using var fixture = new TextureFixture(); var window = Window();
        window.ReplaceSettings(new DesktopSettings { SourceMode = "Data folder / Vortex", GameFolder = fixture.Data,
            PluginsFile = fixture.Plugins, Worldspaces = "Tamriel", GrassWorldspaces = "DLC2SolstheimWorld" });
        var before = JsonSerializer.Serialize(window.Settings);
        window.Show();
        try
        {
            foreach (string page in new[] { "Game & resources", "Grass cache", "Textures", "Terrain", "Object LOD", "Overview" }) Click(window, page);
            Assert.Equal(before, JsonSerializer.Serialize(window.Settings));
            foreach (bool rebuild in new[] { false, true })
            {
                if (rebuild) window.ReplaceSettings(window.Settings.Snapshot());
                foreach (var (flag, output, page) in new[] {
                    ("PipelineGrass", "GrassOutput", "Grass cache"), ("PipelineTextures", "TextureGenOutput", "Textures"),
                    ("PipelineTerrain", "TerrainOutput", "Terrain"), ("PipelineLod", "OutputFolder", "Object LOD") })
                {
                    Click(window, "Overview");
                    string path = Path.Combine(fixture.Root, output);
                    window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == output).Text = path;
                    var configure = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Configure" + flag);
                    configure.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    var field = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == output);
                    Assert.Equal(path, field.Text);
                    field.Text = path + "-updated";
                    Click(window, "Overview");
                    Assert.Equal(path + "-updated", window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == output).Text);
                }
                foreach (var check in window.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Name?.StartsWith("Pipeline") == true))
                    check.SetCurrentValue(CheckBox.IsCheckedProperty, false);
                Assert.False(FindButton(window, "Run selected stages").IsEffectivelyEnabled);
                window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "PipelineTerrain").SetCurrentValue(CheckBox.IsCheckedProperty, true);
                Assert.True(FindButton(window, "Run selected stages").IsEffectivelyEnabled);
                var request = RequestFactory.Pipeline(window.Settings.Snapshot());
                Assert.NotNull(request.Terrain); Assert.Null(request.Grass); Assert.Null(request.Textures); Assert.Null(request.Lod);
                Click(window, "Object LOD");
                window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "Worldspaces").Text = "FixtureWorld";
                Click(window, "Terrain");
                Assert.Equal("FixtureWorld", window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "Worldspaces").Text);
                Assert.Equal("DLC2SolstheimWorld", window.Settings.GrassWorldspaces);
                Click(window, "Overview");
            }
            window.Width = 960; window.Height = 680; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            foreach (string text in new[] { "Run selected stages", "Check setup", "Cancel job" })
            {
                var button = FindButton(window, text);
                var point = button.TranslatePoint(new Point(), window)!.Value;
                Assert.InRange(point.X, 0, window.Bounds.Width - button.Bounds.Width);
                Assert.InRange(point.Y, 0, window.Bounds.Height - button.Bounds.Height);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task OutputScopeReviewInspectsLegacyCacheWithoutAConfiguredGameOrModifyingOutput()
    {
        string grass = Path.Combine(_root, "grass-output"); Directory.CreateDirectory(Path.Combine(grass, "grass"));
        string marker = Path.Combine(grass, "AnvilLOD.grass.json"); File.WriteAllText(marker, "legacy manifest");
        File.WriteAllBytes(Path.Combine(grass, "grass", "Tamrielx0000y0000.cgid"), [1, 2, 3]);
        var old = PenguLOD.Core.Pipeline.OutputSnapshot.Capture(grass);
        var window = Window();
        window.ReplaceSettings(new DesktopSettings { Instance = "", CacheFolder = Path.Combine(_root, "cache"),
            PipelineGrass = true, PipelineTextures = true, PipelineTerrain = true, PipelineLod = true,
            GrassOutput = grass, TextureGenOutput = Path.Combine(_root, "new-textures"),
            TerrainOutput = Path.Combine(_root, "new-terrain"), OutputFolder = Path.Combine(_root, "new-lod"),
            GrassWorldspaces = "all", Worldspaces = "*", GrassNgioConfig = Path.Combine(_root, "missing.ini") });
        string settings = JsonSerializer.Serialize(window.Settings);
        window.Show();
        try
        {
            window.GetVisualDescendants().OfType<Expander>().Single(e => e.Header as string == "Run options").IsExpanded = true;
            Click(window, "Review output scope"); await WaitFor(() => window.Status == "Review output scope completed");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Grass cache:") == true
                && t.Text.Contains(grass) && t.Text.Contains("AnvilLOD.grass.json"));
            Assert.Equal(settings, JsonSerializer.Serialize(window.Settings));
            Assert.Equal(old.Digest, PenguLOD.Core.Pipeline.OutputSnapshot.Capture(grass).Digest);
            Assert.False(Directory.Exists(window.Settings.TextureGenOutput));
            Assert.False(Directory.Exists(window.Settings.TerrainOutput));
            Assert.False(Directory.Exists(window.Settings.OutputFolder));
            Assert.DoesNotContain("System.InvalidOperationException", File.ReadAllText(Assert.Single(Directory.GetFiles(window.Settings.CacheFolder, "run.log", SearchOption.AllDirectories))));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DiagnosticsShowRuntimeRequirementsAndExportMetadataOnly()
    {
        using var fixture = new TextureFixture(); var window = Window();
        window.ReplaceSettings(new DesktopSettings { SourceMode = "Data folder / Vortex", GameFolder = fixture.Data, PluginsFile = fixture.Plugins,
            OutputFolder = Path.Combine(fixture.Root, "lod"), CacheFolder = Path.Combine(fixture.Root, "cache"),
            PipelineGrass = false, PipelineTextures = false, PipelineLod = true });
        window.Show();
        try
        {
            Click(window, "Check setup"); await WaitFor(() => window.Status == "Diagnostics complete");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Diagnostics" && t.FontSize == 28);
            var report = Assert.Single(Directory.GetFiles(window.Settings.CacheFolder, "diagnostics.json", SearchOption.AllDirectories));
            var json = File.ReadAllText(report); Assert.Contains("runtime-plugin-missing", json); Assert.DoesNotContain(fixture.Root, json);
            Assert.False(Directory.Exists(window.Settings.OutputFolder));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TerrainPipelineGeneratesAndResumesFromDesktop()
    {
        using var fixture = new TerrainFixture();
        var window = Window(); window.ReplaceSettings(new DesktopSettings
        {
            SourceMode = "Data folder / Vortex", GameFolder = fixture.Textures.Data, PluginsFile = fixture.Textures.Plugins,
            Worldspaces = "FixtureWorld", TerrainOutput = fixture.Output, TerrainTextureSize = 64,
            CacheFolder = Path.Combine(fixture.Root, "cache"), PipelineGrass = false, PipelineTextures = false,
            PipelineLod = false, PipelineTerrain = true
        });
        window.Show();
        try
        {
            Click(window, "Overview"); await window.RunSelectedPipeline();
            Assert.Equal("Completed", window.Status);
            Assert.True(window.Settings.UseGeneratedTerrain);
            Assert.True(File.Exists(Path.Combine(fixture.Output, TerrainGeneration.ManifestName)));
            await window.RunSelectedPipeline();
            Assert.Equal("Completed", window.Status);
            Click(window, "Terrain");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("1 terrain tiles") == true);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TerrainMaterialControlsRemainBoundAfterPresetReplacement()
    {
        var window=Window();window.Show();
        try
        {
            foreach(bool replace in new[]{false,true})
            {
                if(replace)window.ReplaceSettings(window.Settings.Snapshot());
                Click(window,"Terrain");
                foreach(var (label,value) in new[]{("Source normal strength",.6m),("PBR scale",.8m),("PBR gamma",1.3m),("PBR ambient occlusion",.4m)})
                {
                    var input=LabeledInput<NumericUpDown>(window,label);input.SetCurrentValue(NumericUpDown.ValueProperty,value);
                    Assert.False(DataValidationErrors.GetHasErrors(input));
                }
                Assert.Equal(.6m,window.Settings.TerrainNormalStrength);Assert.Equal(.8m,window.Settings.TerrainPbrScale);
                Assert.Equal(1.3m,window.Settings.TerrainPbrGamma);Assert.Equal(.4m,window.Settings.TerrainAo);
            }
            LabeledInput<NumericUpDown>(window,"PBR ambient occlusion").BringIntoView();
            Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            if(Environment.GetEnvironmentVariable("PENGULOD_SCREENSHOTS") is { } folder)
            {
                Directory.CreateDirectory(folder);using var image=window.CaptureRenderedFrame();image!.Save(Path.Combine(folder,"terrain-materials.png"));
            }
        }
        finally {window.Close();}
    }

    [AvaloniaFact]
    public async Task GenerationSettingsRemainEditableAndPersistAfterGarbageCollection()
    {
        var window = Window(); window.Show();
        try
        {
            // Exercise initial creation, preset page rebuilding and imported settings.
            foreach (string state in new[] { "Initial", "Preset", "Imported" })
            {
                Click(window, "Object LOD");
                if (state == "Preset") Click(window, "Balanced preset");
                if (state == "Imported") window.ReplaceSettings(new DesktopSettings());
                Click(window, "Overview");
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                Click(window, "Object LOD");

                foreach (var group in window.GetVisualDescendants().OfType<Expander>().ToArray()) group.IsExpanded = true;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var tip = LabeledInput<NumericUpDown>(window, "Grass tip brightness");
                var root = LabeledInput<NumericUpDown>(window, "Grass root brightness");
                var tree = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == "Tree billboards");
                var size = LabeledInput<ComboBox>(window, "PBR texture size");
                tip.SetCurrentValue(NumericUpDown.ValueProperty, .70m);
                root.SetCurrentValue(NumericUpDown.ValueProperty, .40m);
                tree.SetCurrentValue(CheckBox.IsCheckedProperty, false);
                size.SetCurrentValue(ComboBox.SelectedItemProperty, "1024");
                Dispatcher.UIThread.RunJobs();

                foreach (var input in new Control[] { tip, root, tree, size })
                    Assert.False(DataValidationErrors.GetHasErrors(input), $"{state}: {DataValidationErrors.GetErrors(input)?.FirstOrDefault()}");
                Assert.Equal(.70m, GenerationOptions.Get<decimal>(window.Settings, "grassTop"));
                Assert.Equal(.40m, GenerationOptions.Get<decimal>(window.Settings, "grassBottom"));
                Assert.False(GenerationOptions.Get<bool>(window.Settings, "trees"));
                Assert.Equal("1024", GenerationOptions.Get<string>(window.Settings, "pbrSize"));
            }

            // Verify the delayed autosave, not just the save performed when closing.
            var saved = new SettingsStore(Path.Combine(_root, "settings.json"));
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.True(File.Exists(saved.FilePath));
            var reloaded = saved.Load();
            Assert.Equal(.70m, GenerationOptions.Get<decimal>(reloaded, "grassTop"));
            Assert.Equal(.40m, GenerationOptions.Get<decimal>(reloaded, "grassBottom"));
            Assert.False(GenerationOptions.Get<bool>(reloaded, "trees"));
            Assert.Equal("1024", GenerationOptions.Get<string>(reloaded, "pbrSize"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PagesRenderAndBindingsPersist()
    {
        var window = Window(); window.Show();
        Click(window, "Game & resources");
        var input = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "Instance");
        input.Text = "/mnt/mods/My MO2";
        Assert.Equal(input.Text, window.Settings.Instance);
        string? screenshots = Environment.GetEnvironmentVariable("PENGULOD_SCREENSHOTS");
        foreach (var (title, file) in new[] { ("Overview", "overview"), ("Game & resources", "source"), ("Grass cache", "grass-cache"), ("Textures", "texture-generation"), ("Terrain", "terrain"), ("Object LOD", "settings"), ("Scan results", "scan"), ("Reports & outputs", "outputs"), ("Mesh maker", "meshes"), ("Mod author", "author"), ("Texture tools", "textures"), ("Custom rules", "rules"), ("Diagnostics", "diagnostics"), ("Preferences", "preferences") })
        {
            Click(window, title);
            Assert.Contains(window.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Bounds.Width > 0);
            if (screenshots is not null)
            { Directory.CreateDirectory(screenshots); using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap); bitmap.Save(Path.Combine(screenshots, file + ".png")); }
        }
        Click(window, "Object LOD"); Click(window, "Objects only");
        Assert.False(GenerationOptions.Get<bool>(window.Settings, "trees"));
        Assert.False(GenerationOptions.Get<bool>(window.Settings, "dynamic"));
        Assert.Equal("/mnt/mods/My MO2", window.Settings.Instance);
        window.ReplaceSettings(window.Settings.Snapshot());
        window.Width = 960; window.Height = 680; Click(window, "Overview");
        if (screenshots is not null) { using var bitmap = window.CaptureRenderedFrame(); bitmap!.Save(Path.Combine(screenshots, "compact.png")); }
        window.Close();
        var reloaded = new SettingsStore(Path.Combine(_root, "settings.json")).Load();
        Assert.Equal("/mnt/mods/My MO2", reloaded.Instance);
        Assert.False(GenerationOptions.Get<bool>(reloaded, "trees"));
    }

    [AvaloniaFact]
    public async Task InvalidSourceShowsAnActionableErrorAndRestoresTheUi()
    {
        var window = Window(); window.Settings.CacheFolder = Path.Combine(_root, "Cache");
        window.Show(); Click(window, "Game & resources"); Click(window, "Scan load order");
        await WaitFor(() => !window.Busy);
        Assert.False(window.Busy);
        Assert.True(FindButton(window, "Scan load order").IsEffectivelyEnabled);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Choose the Mod Organizer 2 instance") == true);
        window.Close();
    }

    [AvaloniaFact]
    public async Task GrassCachingRunsThroughUiAndPersistsOutputForLod()
    {
        using var fixture = new GrassFixture();
        var window = Window();
        window.ReplaceSettings(new DesktopSettings {
            SourceMode = "Data folder / Vortex", GameFolder = fixture.Textures.Data, PluginsFile = fixture.Textures.Plugins,
            GrassOutput = fixture.Output, GrassExecutable = fixture.Executable, MaxThreads = 2,
            CacheFolder = Path.Combine(fixture.Root, "Cache") });
        window.Show(); Click(window, "Grass cache");
        var output = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "GrassOutput");
        Assert.Equal(fixture.Output, output.Text);
        await window.RunGrassGeneration(false);
        Assert.Equal("Grass cache complete", window.Status);
        Assert.True(window.Settings.UseGeneratedGrass);
        GrassGenerationManifest.Read(fixture.Output, true)!.Verify(fixture.Output);
        window.ReplaceSettings(window.Settings.Snapshot()); Click(window, "Grass cache");
        if (Environment.GetEnvironmentVariable("PENGULOD_SCREENSHOTS") is { } screenshots)
        { using var bitmap = window.CaptureRenderedFrame(); bitmap!.Save(Path.Combine(screenshots, "grass-results.png")); }
        window.Close();
    }

    [AvaloniaFact]
    public async Task FailedGrassDoesNotStartTexturesOrClearLodOutput()
    {
        using var fixture = new GrassFixture("fail");
        string lod = Path.Combine(fixture.Root, "LOD"); Directory.CreateDirectory(lod);
        File.WriteAllText(Path.Combine(lod, "PenguLOD.log"), "previous result");
        var window = Window();
        window.ReplaceSettings(new DesktopSettings {
            SourceMode = "Data folder / Vortex", GameFolder = fixture.Textures.Data, PluginsFile = fixture.Textures.Plugins,
            GrassOutput = fixture.Output, GrassExecutable = fixture.Executable, MaxThreads = 2,
            TextureGenOutput = fixture.Textures.Output, DynDolodFolder = fixture.Textures.Resources, TextureGenTrees = false,
            OutputFolder = lod, CleanOutput = true, CacheFolder = Path.Combine(fixture.Root, "Cache") });
        window.Show(); await window.RunGrassGeneration(true);
        Assert.Equal("Grass, textures and LOD failed", window.Status);
        Assert.Equal("previous result", File.ReadAllText(Path.Combine(lod, "PenguLOD.log")));
        Assert.False(File.Exists(Path.Combine(fixture.Textures.Output, TextureGenerationManifest.FileName)));
        Assert.False(window.Settings.UseGeneratedGrass); window.Close();
    }

    [AvaloniaFact]
    public async Task ExistingCacheValidationRunsInsideTheJobAndPreservesPriorOutputs()
    {
        using var fixture = new GrassFixture();
        await Task.Run(() => GrassGeneration.Run(fixture.Request), TestContext.Current.CancellationToken);
        var manifestPath = Path.Combine(fixture.Output, GrassGenerationManifest.FileName);
        string manifest = File.ReadAllText(manifestPath);
        File.AppendAllText(Path.Combine(fixture.Output, "grass", "Tamrielx0000y0000.cgid"), "changed");
        string lod = Path.Combine(fixture.Root, "LOD"); Directory.CreateDirectory(lod);
        File.WriteAllText(Path.Combine(lod, "PenguLOD.log"), "previous LOD");
        var window = Window();
        window.ReplaceSettings(new DesktopSettings {
            SourceMode = "Data folder / Vortex", GameFolder = fixture.Textures.Data, PluginsFile = fixture.Textures.Plugins,
            GrassOutput = fixture.Output, GrassExecutable = fixture.Executable, MaxThreads = 2,
            TextureGenOutput = fixture.Textures.Output, DynDolodFolder = fixture.Textures.Resources, TextureGenTrees = false,
            OutputFolder = lod, CleanOutput = true, CacheFolder = Path.Combine(fixture.Root, "Cache") });
        window.Show();
        try
        {
            foreach (bool combined in new[] { true, false })
            {
                Task? job = null;
                // A corrupt existing cache must be reported by the worker. Previously the button
                // performed all file reads on the UI thread and threw before it could start a job.
                Assert.Null(Record.Exception(() => { job = window.RunGrassGeneration(combined); }));
                await job!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(combined ? "Grass, textures and LOD failed" : "Grass caching failed", window.Status);
                Assert.False(window.Busy);
            }
            Assert.Equal(manifest, File.ReadAllText(manifestPath));
            Assert.Equal("previous LOD", File.ReadAllText(Path.Combine(lod, "PenguLOD.log")));
            Assert.False(File.Exists(Path.Combine(fixture.Textures.Output, TextureGenerationManifest.FileName)));
            var logs = Directory.GetFiles(Path.Combine(fixture.Root, "Cache"), "run.log", SearchOption.AllDirectories);
            Assert.Equal(2, logs.Length);
            Assert.All(logs, path => Assert.Contains("Verifying generated grass cache:", File.ReadAllText(path)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CombinedGrassRegenerationKeepsTheUiResponsiveAndCanBeCancelled()
    {
        using var fixture = new GrassFixture();
        await Task.Run(() => GrassGeneration.Run(fixture.Request), TestContext.Current.CancellationToken);
        string manifest = File.ReadAllText(Path.Combine(fixture.Output, GrassGenerationManifest.FileName));
        fixture.SetBehavior("wait");
        var window = Window();
        window.ReplaceSettings(new DesktopSettings {
            SourceMode = "Data folder / Vortex", GameFolder = fixture.Textures.Data, PluginsFile = fixture.Textures.Plugins,
            GrassOutput = fixture.Output, GrassExecutable = fixture.Executable, MaxThreads = 2,
            TextureGenOutput = fixture.Textures.Output, DynDolodFolder = fixture.Textures.Resources, TextureGenTrees = false,
            OutputFolder = Path.Combine(fixture.Root, "LOD"), CacheFolder = Path.Combine(fixture.Root, "Cache") });
        window.Settings.PipelineGrass = true; window.Settings.PipelineTextures = true;
        window.Settings.PipelineTerrain = false; window.Settings.PipelineLod = true;
        window.Show(); Click(window, "Run selected stages");
        try
        {
            await WaitFor(() => window.Status == "FAKE_READY");
            Assert.True(window.Busy);
            Assert.True(FindButton(window, "Cancel job").IsEffectivelyEnabled);
            bool heartbeat = false;
            await Dispatcher.UIThread.InvokeAsync(() => heartbeat = true);
            Assert.True(heartbeat);
            Click(window, "Object LOD");
            Assert.False(FindButton(window, "Generate object LOD").IsEffectivelyEnabled);
            Click(window, "Textures");
            Assert.False(FindButton(window, "Generate textures").IsEffectivelyEnabled);
            Click(window, "Overview");
            Assert.False(FindButton(window, "Run selected stages").IsEffectivelyEnabled);
            Click(window, "Cancel job");
            await WaitFor(() => !window.Busy);
            Assert.Equal("Cancelled", window.Status);
            Assert.True(FindButton(window, "Run selected stages").IsEffectivelyEnabled);
            Assert.Equal(manifest, File.ReadAllText(Path.Combine(fixture.Output, GrassGenerationManifest.FileName)));
            Assert.False(File.Exists(Path.Combine(fixture.Textures.Output, TextureGenerationManifest.FileName)));
            Assert.False(Directory.Exists(window.Settings.OutputFolder));
            Assert.Empty(Directory.GetDirectories(fixture.Root, ".pengulod-grass-build-*"));
        }
        finally
        {
            if (window.Busy) { Click(window, "Cancel job"); await WaitFor(() => !window.Busy); }
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CombinedGrassJobKeepsTextureAndGrassOverridesTogether()
    {
        using var fixture = new GrassFixture();
        var window = Window();
        window.ReplaceSettings(new DesktopSettings {
            SourceMode = "Data folder / Vortex", GameFolder = fixture.Textures.Data, PluginsFile = fixture.Textures.Plugins,
            GrassOutput = fixture.Output, GrassExecutable = fixture.Executable, MaxThreads = 2,
            TextureGenOutput = fixture.Textures.Output, DynDolodFolder = fixture.Textures.Resources, TextureGenTrees = false,
            TextureGenBaseSize = 64, OutputFolder = Path.Combine(fixture.Root, "LOD"), CacheFolder = Path.Combine(fixture.Root, "Cache") });
        window.Show(); await window.RunGrassGeneration(true);
        Assert.True(window.Settings.UseGeneratedGrass); Assert.True(window.Settings.UseGeneratedTextures);
        Assert.Equal("No matching worldspaces; previous LOD preserved", window.Status); // Fixture has no LOD grids.
        GrassGenerationManifest.Read(fixture.Output, true)!.Verify(fixture.Output);
        TextureGenerationManifest.Read(fixture.Textures.Output, true)!.Verify(fixture.Textures.Output);
        window.Close();
    }

    [AvaloniaFact]
    public async Task FailedCombinedTexturesSaveReportsAndResumeWithoutRegeneratingGrass()
    {
        using var fixture = new GrassFixture();
        string rule = File.ReadAllText(fixture.Textures.RuleFile);
        File.WriteAllText(fixture.Textures.RuleFile, TextureFixture.Tile(@"textures\missing.dds", @"textures\lod\atlas.dds", 0));
        string lod = Path.Combine(fixture.Root, "LOD"), cache = Path.Combine(fixture.Root, "Cache");
        Directory.CreateDirectory(lod); File.WriteAllText(Path.Combine(lod, "PenguLOD.log"), "prior LOD");
        var window = Window();
        window.ReplaceSettings(new DesktopSettings {
            SourceMode = "Data folder / Vortex", GameFolder = fixture.Textures.Data, PluginsFile = fixture.Textures.Plugins,
            GrassOutput = fixture.Output, GrassExecutable = fixture.Executable, MaxThreads = 2,
            TextureGenOutput = fixture.Textures.Output, DynDolodFolder = fixture.Textures.Resources, TextureGenTrees = false,
            TextureGenBaseSize = 64, OutputFolder = lod, CacheFolder = cache });
        window.Show();
        try
        {
            await window.RunGrassGeneration(true);
            Assert.Equal("Grass complete; later stage failed", window.Status);
            Assert.Equal("prior LOD", File.ReadAllText(Path.Combine(lod, "PenguLOD.log")));
            Assert.Single(Directory.GetFiles(cache, "grass-report.json", SearchOption.AllDirectories));
            var report = Assert.Single(Directory.GetFiles(cache, "texture-report.json", SearchOption.AllDirectories));
            Assert.False(JsonSerializer.Deserialize<TextureGenerationResult>(File.ReadAllText(report))!.Published);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("resume without rebuilding verified stages") == true);
            var manifest = File.ReadAllText(Path.Combine(fixture.Output, GrassGenerationManifest.FileName));
            File.WriteAllText(fixture.Textures.RuleFile, rule);
            await window.RunGrassGeneration(true);
            Assert.Contains("no matching worldspaces", window.Status, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(manifest, File.ReadAllText(Path.Combine(fixture.Output, GrassGenerationManifest.FileName)));
            Assert.True(TextureGenerationManifest.Read(fixture.Textures.Output, true)!.Complete);
            Assert.NotEmpty(Directory.GetFiles(cache, "scan-report.json", SearchOption.AllDirectories));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TextureGenerationRunsThroughUiAndCombinedJobUsesItsOutput()
    {
        using var fixture = new TextureFixture();
        var window = Window();
        var settings = new DesktopSettings
        {
            SourceMode = "Data folder / Vortex", GameFolder = fixture.Data, PluginsFile = fixture.Plugins,
            ResourceFolders = fixture.Resources, TextureGenOutput = fixture.Output, TextureGenTrees = false,
            TextureGenGrass = false, TextureGenBaseSize = 64, MaxThreads = 2,
            OutputFolder = Path.Combine(fixture.Root, "LOD"), CacheFolder = Path.Combine(fixture.Root, "Cache")
        };
        window.ReplaceSettings(settings); window.Show(); Click(window, "Game & resources");
        Assert.Equal(fixture.Resources, window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ResourceFolders").Text);
        Click(window, "Textures");
        var output = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "TextureGenOutput");
        Assert.Equal(fixture.Output, output.Text);
        var size = window.GetVisualDescendants().OfType<ComboBox>().Single(t => t.Name == "TextureGenBaseSize");
        Assert.Equal(64m, size.SelectedItem);
        size.SelectedItem = 128m; Assert.Equal(128m, window.Settings.TextureGenBaseSize);
        size.SelectedItem = 64m;
        var trunks = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == "Hybrid tree trunk textures");
        Assert.True(trunks.IsChecked); // also enabled when older settings omit this property
        trunks.SetCurrentValue(CheckBox.IsCheckedProperty, false);
        Assert.False(RequestFactory.Textures(window.Settings.Snapshot()).TreeTrunks);
        trunks.SetCurrentValue(CheckBox.IsCheckedProperty, true);
        Assert.True(RequestFactory.Textures(window.Settings.Snapshot()).TreeTrunks);
        var hd = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == "HD front and side billboards");
        Assert.True(hd.IsChecked);
        hd.SetCurrentValue(CheckBox.IsCheckedProperty, false);
        var layout = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "TextureGenGrassTextures");
        layout.SelectedItem = "Basic";
        var scale = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "TextureGenUnplacedTreeScale");
        scale.Value = 2;
        var billboardSettings = RequestFactory.Textures(window.Settings.Snapshot()).Billboards!;
        Assert.False(billboardSettings.HdViews); Assert.Equal(2f, billboardSettings.UnplacedTreeScale);
        Assert.Equal(PenguLOD.Plugins.Authoring.GrassTextureMode.Basic, billboardSettings.GrassTextures);
        string preset = Path.Combine(fixture.Root, "billboard-settings.json"); SettingsStore.Write(preset, window.Settings);
        Assert.Equal(billboardSettings, RequestFactory.Textures(SettingsStore.Read(preset)).Billboards);
        Assert.Equal(new[] { fixture.Resources }, RequestFactory.Textures(SettingsStore.Read(preset)).ResourcePacks);
        Assert.Equal(new[] { fixture.Resources }, RequestFactory.Scan(SettingsStore.Read(preset), false).ResourcePacks);
        await window.RunTextureGeneration(false);
        Assert.True(window.Settings.UseGeneratedTextures);
        Assert.True(TextureGenerationManifest.Read(fixture.Output, true)!.Complete);
        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.SelectedItem = grid.ItemsSource.Cast<TextureGenerationItem>().First(i => i.Status == "Written");
        Click(window, "Preview selected texture");
        var preview = window.OwnedWindows.Single();
        Assert.IsType<Avalonia.Controls.Image>(preview.Content);
        if (Environment.GetEnvironmentVariable("PENGULOD_SCREENSHOTS") is { } screenshots)
        {
            using var frame = preview.CaptureRenderedFrame(); frame!.Save(Path.Combine(screenshots, "texture-preview.png"));
            window.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Content is StackPanel).ScrollToEnd();
            Dispatcher.UIThread.RunJobs(); using var result = window.CaptureRenderedFrame(); result!.Save(Path.Combine(screenshots, "texture-results.png"));
        }
        preview.Close();
        await window.RunTextureGeneration(true);
        // The tiny fixture has no worldspaces; reaching the scan and its report proves the second stage ran.
        Assert.Contains("no matching worldspaces", window.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(settings.OutputFolder)); // Failed/empty LOD is never published.
        Assert.Contains("Using generated textures", File.ReadAllText(Assert.Single(Directory.GetFiles(settings.CacheFolder, "lod.log", SearchOption.AllDirectories))));
        Assert.NotEmpty(Directory.GetFiles(settings.CacheFolder, "scan-report.json", SearchOption.AllDirectories));
        window.Close();
        Assert.True(new SettingsStore(Path.Combine(_root, "settings.json")).Load().TextureGenTrunks);
    }

    [AvaloniaFact]
    public async Task FailedTexturesDoNotStartLodOrClearItsOutput()
    {
        using var fixture = new TextureFixture();
        string lod = Path.Combine(fixture.Root, "LOD"); Directory.CreateDirectory(lod);
        File.WriteAllText(Path.Combine(lod, "PenguLOD.log"), "previous LOD");
        File.WriteAllText(fixture.RuleFile, TextureFixture.Tile(@"textures\missing.dds", @"textures\lod\atlas.dds", 0));
        var window = Window();
        window.ReplaceSettings(new DesktopSettings
        {
            SourceMode = "Data folder / Vortex", GameFolder = fixture.Data, PluginsFile = fixture.Plugins,
            ResourceFolders = fixture.Resources, TextureGenOutput = fixture.Output, TextureGenTrees = false,
            TextureGenGrass = false, TextureGenBaseSize = 64, MaxThreads = 2, CleanOutput = true,
            OutputFolder = lod, CacheFolder = Path.Combine(fixture.Root, "Cache")
        });
        window.Show(); await window.RunTextureGeneration(true);
        Assert.Equal("Textures not published", window.Status);
        Assert.Equal("previous LOD", File.ReadAllText(Path.Combine(lod, "PenguLOD.log")));
        Assert.False(window.Settings.UseGeneratedTextures); window.Close();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("*")]
    [InlineData(" Tamriel, * ")]
    public void TerrainAndObjectWildcardRequestsAgree(string worlds)
    {
        using var f = new TextureFixture();
        var settings = new DesktopSettings { SourceMode = "Data folder / Vortex", GameFolder = f.Data,
            PluginsFile = f.Plugins, TerrainOutput = Path.Combine(f.Root, "terrain"), Worldspaces = worlds };
        Assert.Null(RequestFactory.Scan(settings, false).Scan.Worldspaces);
        Assert.Null(RequestFactory.Terrain(settings).Worldspaces);
        settings.Worldspaces = "Tamriel, tamriel; OtherWorld";
        Assert.Equal(new[] { "Tamriel", "OtherWorld" }, RequestFactory.Terrain(settings).Worldspaces);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WaterfallAndObjectPbrControlsRemainIndependent(bool objects, bool waterfalls)
    {
        using var f = new TextureFixture();
        var settings = new DesktopSettings { SourceMode = "Data folder / Vortex", GameFolder = f.Data, PluginsFile = f.Plugins };
        settings.Options["pbr"] = JsonSerializer.SerializeToElement(objects);
        settings.Options["pbrWaterfalls"] = JsonSerializer.SerializeToElement(waterfalls);
        var request = RequestFactory.Scan(settings, false);
        Assert.Equal(objects, request.PbrLod); Assert.Equal(waterfalls, request.Scan.PbrWaterfalls);
    }

    [AvaloniaFact]
    public void ImportedRuleActionsAreReadOnlyAndClearlyExplained()
    {
        var window = Window(); window.Show();
        try
        {
            Click(window, "Custom rules");
            var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
            Assert.True(grid.Columns.Single(c => c.Header as string == "Reference").IsReadOnly);
            Assert.True(grid.Columns.Single(c => c.Header as string == "Flags").IsReadOnly);
            Assert.False(grid.Columns.Single(c => c.Header as string == "LOD4").IsReadOnly);
            var imported = PenguLOD.Core.Lod.RuleEntry.Parse("tree,Level0,Level1,Level2,,Near LOD,Disable,19,imported", "fixture")!;
            imported.Lod4 = "Level1";
            var restored = PenguLOD.Core.Lod.RuleEntry.Parse(imported.Format(), "fixture")!;
            Assert.Equal("Disable", restored.Reference); Assert.Equal(19, restored.Flags);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("not applied during generation") == true);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReportsMeasureSavedOutputWithoutRewritingItAndPlansRequirePreview()
    {
        var window = Window();
        string output = Path.Combine(_root, "output"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "PenguLOD.esm"), "fixture");
        PenguLOD.Plugins.OutputSetReceipt.Record(output, "lod", "fixture", PenguLOD.Core.Pipeline.OutputSnapshot.Capture(output));
        var before = PenguLOD.Core.Pipeline.OutputSnapshot.Capture(output);
        window.ReplaceSettings(new DesktopSettings { CacheFolder = Path.Combine(_root, "cache") }); window.Show();
        try
        {
            Click(window, "Reports & outputs");
            Assert.False(FindButton(window, "Publish reviewed package").IsEnabled);
            window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CostFolder").Text = output;
            await window.MeasureOutput(); before.Verify(output);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("not FPS") == true);
            window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "OutputFolders").Text = output;
            window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PackageFolder").Text = Path.Combine(_root, "package");
            await window.PreviewOutputUpdate(); Assert.True(FindButton(window, "Publish reviewed package").IsEnabled);
            Assert.False(Directory.Exists(Path.Combine(_root, "package")));
            window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PackageFolder").Text += "-changed";
            Assert.False(FindButton(window, "Publish reviewed package").IsEnabled);
            if (Environment.GetEnvironmentVariable("PENGULOD_SCREENSHOTS") is { } screenshots)
                window.CaptureRenderedFrame()?.Save(Path.Combine(screenshots, "reports-outputs.png"));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void PresetRoundTripAndValidation()
    {
        var file = Path.Combine(_root, "portable.json");
        var settings = new DesktopSettings { Instance = "/path/with spaces/MO2", MaxThreads = 3, Worldspaces = "Tamriel, DLC2SolstheimWorld" };
        settings.Options["grassDensity"] = JsonSerializer.SerializeToElement(.125m);
        SettingsStore.Write(file, settings);
        var restored = SettingsStore.Read(file);
        Assert.Equal(settings.Instance, restored.Instance); Assert.Equal(.125m, GenerationOptions.Get<decimal>(restored, "grassDensity"));
        restored.Options["grassDensity"] = JsonSerializer.SerializeToElement(20);
        Assert.Throws<ArgumentException>(() => GenerationOptions.Validate(restored));
        File.WriteAllText(file, "{\"SchemaVersion\":99}"); Assert.Throws<InvalidDataException>(() => SettingsStore.Read(file));
        File.WriteAllText(file, "{broken"); Assert.Throws<JsonException>(() => SettingsStore.Read(file));
    }

    [Fact]
    public void RequestMappingValidatesLevelsAndKeepsAllWorldspaces()
    {
        var settings = new DesktopSettings { SourceMode = "Data folder / Vortex", GameFolder = _root, PluginsFile = Path.Combine(_root, "plugins.txt"), Worldspaces = "*" };
        var request = RequestFactory.Scan(settings, false);
        Assert.False(request.OptimizeObjectTextures);
        settings.Options["objectTextures"] = JsonSerializer.SerializeToElement(true);
        settings.Options["objectTextureSize"] = JsonSerializer.SerializeToElement("2048");
        var optimized = RequestFactory.Scan(settings, false);
        Assert.True(optimized.OptimizeObjectTextures); Assert.Equal(2048, optimized.ObjectTextureSize);
        settings.Options["objectTextureSize"] = JsonSerializer.SerializeToElement("300");
        Assert.Throws<ArgumentException>(() => RequestFactory.Scan(settings, false));
        settings.Options["objectTextureSize"] = JsonSerializer.SerializeToElement("1024");
        settings.Options["occlusion"] = JsonSerializer.SerializeToElement(true);
        Assert.Null(RequestFactory.Scan(settings, false).Occlusion);
        Assert.Null(request.Scan.Worldspaces); Assert.False(request.CleanOutput); Assert.Equal(4, request.Levels!.Count);
        settings.Options["snowMode"] = JsonSerializer.SerializeToElement(2.5m);
        settings.Options["occlusion"] = JsonSerializer.SerializeToElement(true);
        Assert.Throws<ArgumentException>(() => RequestFactory.Scan(settings, false));
        settings.Options.Clear();
        foreach (var level in new[] { 4, 8, 16, 32 }) settings.Options["lod" + level] = JsonSerializer.SerializeToElement(false);
        Assert.Throws<ArgumentException>(() => RequestFactory.Scan(settings, false));
    }

    [Fact]
    public async Task CancellationWaitsForWorkerAndRunnerCanBeReused()
    {
        var runner = new JobRunner(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new List<string>();
        var running = runner.Run("cancel-test", _root, 2, (p, ct) =>
        { started.SetResult(); ct.WaitHandle.WaitOne(); ct.ThrowIfCancellationRequested(); return 1; }, messages.Add);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); Assert.True(runner.IsRunning);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.Run("second", _root, 1, (_, _) => 2, _ => { }));
        runner.Cancel(); var cancelled = await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(cancelled.Cancelled); Assert.False(runner.IsRunning);
        var next = await runner.Run("next", _root, 1, (p, _) => { p.Report("done"); return 42; }, messages.Add);
        Assert.True(next.Success); Assert.Equal(42, next.Value);
        Assert.Contains("done", File.ReadAllText(Path.Combine(next.Folder, "run.log")));
    }

    public static bool HasSmokeSettings => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PENGULOD_SMOKE_SETTINGS"));
    public static bool HasGrassSmokeSettings => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PENGULOD_GRASS_SMOKE_SETTINGS"));
    public static bool HasExistingGrassSmokeSettings => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PENGULOD_EXISTING_GRASS_SMOKE_SETTINGS"));

    [AvaloniaFact(Skip = "Set PENGULOD_EXISTING_GRASS_SMOKE_SETTINGS to an isolated preset with a large existing grass cache.", SkipUnless = nameof(HasExistingGrassSmokeSettings))]
    public async Task RealCombinedStartupReturnsImmediatelyAndCancelsExistingCacheVerification()
    {
        var settings = SettingsStore.Read(Environment.GetEnvironmentVariable("PENGULOD_EXISTING_GRASS_SMOKE_SETTINGS")!);
        string manifest = File.ReadAllText(Path.Combine(settings.GrassOutput, GrassGenerationManifest.FileName));
        var window = Window(); window.ReplaceSettings(settings); window.Show(); Click(window, "Grass cache");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var job = window.RunGrassGeneration(true);
        try
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"Button blocked for {watch.Elapsed}.");
            Console.WriteLine($"Combined-job handler returned in {watch.Elapsed.TotalMilliseconds:F1} ms.");
            await WaitFor(() => window.Status.StartsWith("Verifying generated grass cache:") || window.Status.StartsWith("Locating generated grass cache:"));
            Assert.True(window.Busy); Assert.True(FindButton(window, "Cancel job").IsEffectivelyEnabled);
            await Dispatcher.UIThread.InvokeAsync(() => Console.WriteLine("UI responded during existing-cache verification."));
            watch.Restart(); Click(window, "Cancel job");
            await job.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Console.WriteLine($"Cancellation completed in {watch.Elapsed.TotalMilliseconds:F1} ms.");
            Assert.Equal("Cancelled", window.Status);
            Assert.Equal(manifest, File.ReadAllText(Path.Combine(settings.GrassOutput, GrassGenerationManifest.FileName)));
        }
        finally
        {
            if (window.Busy) { Click(window, "Cancel job"); await job.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
            window.Close();
        }
    }

    [AvaloniaFact(Skip = "Set PENGULOD_GRASS_SMOKE_SETTINGS to an isolated grass-generation preset.", SkipUnless = nameof(HasGrassSmokeSettings))]
    public async Task RealGrassGenerationThroughDesktopWithBundledBackend()
    {
        var settings = SettingsStore.Read(Environment.GetEnvironmentVariable("PENGULOD_GRASS_SMOKE_SETTINGS")!);
        var window = Window(); window.ReplaceSettings(settings); window.Show(); Click(window, "Grass cache");
        await window.RunGrassGeneration(false);
        Assert.DoesNotContain("failed", window.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("complete", window.Status, StringComparison.OrdinalIgnoreCase);
        var manifest = GrassGenerationManifest.Read(settings.GrassOutput)!;
        manifest.Verify(settings.GrassOutput); Assert.True(manifest.Instances > 0);
        if (Environment.GetEnvironmentVariable("PENGULOD_SCREENSHOTS") is { } screenshots)
        {
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Content is StackPanel);
            scroll.ScrollToEnd(); Dispatcher.UIThread.RunJobs();
            using var bitmap = window.CaptureRenderedFrame(); bitmap!.Save(Path.Combine(screenshots, "grass-real-results.png"));
        }
        window.Close();
    }

    [AvaloniaFact(Skip = "Provide --smoke-settings to tools/test.sh to run against a local game.", SkipUnless = nameof(HasSmokeSettings))]
    public async Task RealGameGenerationAndIncrementalBuildThroughDesktop()
    {
        var settings = SettingsStore.Read(Environment.GetEnvironmentVariable("PENGULOD_SMOKE_SETTINGS")!);
        var store = new SettingsStore(Path.Combine(_root, "settings.json")); store.Save(settings);
        var window = new MainWindow(store); window.Show();
        if (settings.TextureGenOutput.Length > 0) await window.RunTextureGeneration(true);
        else await window.RunScan(true);
        Assert.DoesNotContain("failed", window.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("completed", window.Status, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(Directory.GetFiles(settings.OutputFolder, "*.bto", SearchOption.AllDirectories));
        foreach (var path in Directory.GetFiles(settings.OutputFolder, "*.bto", SearchOption.AllDirectories))
        {
            var nif = PenguLOD.Meshes.Nif.NifFile.Read(File.ReadAllBytes(path));
            Assert.NotNull(nif);
        }
        await window.RunScan(true);
        Assert.Contains("completed", window.Status, StringComparison.OrdinalIgnoreCase);
        string[] reports = Directory.GetFiles(Path.Combine(settings.CacheFolder, "runs"), "scan-report.json", SearchOption.AllDirectories);
        var newest = reports.OrderByDescending(File.GetLastWriteTimeUtc).First();
        using var report = JsonDocument.Parse(File.ReadAllText(newest));
        Assert.Equal(0, report.RootElement.GetProperty("plan").GetProperty("rebuild").GetInt32());
        Assert.True(report.RootElement.GetProperty("plan").GetProperty("unchanged").GetInt32() > 0);
        Assert.Equal(0, report.RootElement.GetProperty("generation").GetProperty("BlocksFailed").GetInt32());
        if (Environment.GetEnvironmentVariable("PENGULOD_SCREENSHOTS") is { } screenshots)
        {
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Content is StackPanel);
            scroll.ScrollToEnd(); Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(screenshots, "completed-build.png"));
        }
        window.Close();
    }

    [AvaloniaFact]
    public void CorruptSettingsArePreservedBeforeDefaultsAreSaved()
    {
        var path = Path.Combine(_root, "settings.json"); File.WriteAllText(path, "invalid original");
        var window = Window(); window.Show(); window.Close();
        Assert.Equal("invalid original", File.ReadAllText(Directory.GetFiles(_root, "settings.json.invalid-*").Single()));
        Assert.NotNull(SettingsStore.Read(path));
    }
}
