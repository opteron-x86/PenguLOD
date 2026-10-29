using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using AnvilLOD.Core;
using AnvilLOD.Core.Lod;
using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Textures;
using AnvilLOD.Desktop.Models;
using AnvilLOD.Desktop.Services;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Mo2;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AnvilLOD.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly SettingsStore _store;
    private DesktopSettings _settings;
    private readonly JobRunner _runner = new();
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly Queue<string> _logLines = new();
    private readonly DispatcherTimer _logTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private WrapPanel? _lodActions;
    private readonly ContentControl _page = new();
    private readonly TextBlock _status = new() { Text = "Ready", TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _elapsed = new() { Foreground = Muted };
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, Foreground = Accent };
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("monospace"), FontSize = 12, MinHeight = 130, MaxHeight = 270 };
    private readonly ProgressBar _progress = new() { Height = 3, IsVisible = false, IsIndeterminate = true };
    private readonly Button _cancel = new() { Content = "Cancel job", IsEnabled = false };
    private readonly ObservableCollection<RuleEntry> _rules = [];
    private readonly ComboBox _profiles = new() { MinWidth = 180, PlaceholderText = "Available profiles" };
    private readonly TextBlock _counts = new() { Text = "Scan your load order to inspect worldspaces and missing assets.", TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _worlds = Table(("Worldspace", "World"), ("LOD 4", "Lod4"), ("LOD 8", "Lod8"), ("LOD 16", "Lod16"), ("LOD 32", "Lod32"));
    private readonly DataGrid _treeCoverage = Table(("Model", "Model"), ("Level", "Level"), ("Instances", "Instances"), ("Triangles/model", "OutputTriangles"), ("Placed triangles", "PlacedTriangles"), ("Leaf triangles removed", "FoliageTrianglesRemoved"), ("Coverage loss (%)", "FoliageCoveragePercent"), ("Method", "Method"), ("Fallback", "Fallback"));
    private readonly TextBox _issues = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 220, PlaceholderText = "Scan warnings and missing assets appear here." };
    private readonly DataGrid _textures = Table(("Mod", "Mod"), ("Texture", "Relative"), ("Format", "Format"), ("Width", "Width"), ("Height", "Height"), ("Mips", "Mips"));
    private readonly Dictionary<string, Control> _pages = [];
    private readonly Dictionary<string, Button> _navigation = [];
    private IReadOnlyList<BadTexture> _badTextures = [];
    private string _textureSourceSignature = "";
    private string? _lastRunFolder;
    private bool _closing, _allowClose;
    private readonly Stopwatch _watch = new();
    private string _selectedPage = "Overview";
    private static readonly IBrush Accent = Brush.Parse("#E5B16C"), Muted = Brush.Parse("#A5ADBB"), Surface = Brush.Parse("#1C222D");
    internal DesktopSettings Settings => _settings;
    internal string Status => _status.Text ?? "";
    internal bool Busy => _runner.IsRunning;

    public MainWindow(SettingsStore? store = null)
    {
        _store = store ?? new SettingsStore();
        string? loadIssue = null;
        try { _settings = _store.Load(); }
        catch (Exception e)
        {
            // Keep the original bytes before allowing autosave of fresh defaults.
            string backup = _store.FilePath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try { File.Copy(_store.FilePath, backup, false); loadIssue = $"Could not load settings: {e.Message} Original saved as {backup}"; }
            catch { loadIssue = $"Could not load settings: {e.Message} Automatic saving is disabled until you import a valid preset."; _saveAllowed = false; }
            _settings = new();
        }
        Title = $"{ProductInfo.Name} · Linux";
        Width = 1240; Height = 900; MinWidth = 960; MinHeight = 680;
        Background = Brush.Parse("#121720");
        FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter");
        FontSize = 14;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PenguLOD.Desktop/Assets/pengulod.png"))); } catch (FileNotFoundException) { }
        _cancel.Click += (_, _) => { _runner.Cancel(); _status.Text = "Cancelling… waiting for active workers"; _cancel.IsEnabled = false; };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };
        _logTimer.Tick += (_, _) => DrainLog();
        _logTimer.Start();
        Closing += (_, e) =>
        {
            if (_runner.IsRunning && !_allowClose)
            {
                e.Cancel = true; _closing = true; _runner.Cancel(); _cancel.IsEnabled = false;
                _status.Text = "Cancelling the active job before closing…";
            }
            else Save();
        };
        Closed += (_, _) => { _saveTimer.Stop(); _logTimer.Stop(); };
        AttachSettings();
        BuildShell();
        if (loadIssue is not null) Notice(loadIssue);
        else if (_store.MigratedFrom is { } source) Notice($"Imported settings from {source}. The original settings and output paths are preserved.");
    }

    private bool _saveAllowed = true;
    private void AttachSettings() => _settings.PropertyChanged += (_, _) => ScheduleSave();
    private void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }
    private void Save()
    {
        if (!_saveAllowed) return;
        try { _store.Save(_settings); }
        catch (Exception e) { Notice("Settings could not be saved: " + e.Message); }
    }
    private void Notice(string text) { _notice.Text = text; _notice.IsVisible = text.Length > 0; }
    private void DrainLog()
    {
        bool changed = false;
        while (_messages.TryDequeue(out var line))
        {
            _logLines.Enqueue(line); changed = true;
            while (_logLines.Count > 2500) _logLines.Dequeue();
            if (_runner.IsRunning && !_closing && _cancel.IsEnabled) _status.Text = line;
        }
        if (changed) { _log.Text = string.Join('\n', _logLines); _log.CaretIndex = _log.Text.Length; }
        if (_watch.IsRunning) _elapsed.Text = _watch.Elapsed.ToString(@"hh\:mm\:ss");
    }
    private void BuildShell()
    {
        _page.Content = null;
        foreach (var control in new Control[] { _notice, _page, _status, _elapsed, _cancel, _progress, _log, _profiles, _counts, _worlds, _issues, _textures, _textureGenResults, _textureGenSummary, _grassSummary, _pipelineSummary, _pipelineStages, _terrainSummary, _terrainResults, _treeCoverage, _diagnosticSummary, _diagnosticIssues, _diagnosticEstimates, _seasonCoverage })
        {
            if (control.Parent is Panel panel) panel.Children.Remove(control);
            else if (control.Parent is ContentControl content) content.Content = null;
        }
        _pages.Clear(); _navigation.Clear();
        _pages["Overview"] = Overview();
        _pages["Pipeline"] = PipelinePage();
        _pages["Grass cache"] = GrassGenerationPage();
        _pages["Texture generation"] = TextureGenerationPage();
        _pages["Terrain"] = TerrainPage();
        _pages["Generation settings"] = GenerationSettings();
        _pages["Mesh maker"] = MeshMaker();
        _pages["Mod author"] = ModAuthor();
        _pages["Texture tools"] = TextureTools();
        _pages["Custom rules"] = RulesPage();
        _pages["Diagnostics"] = DiagnosticsPage();
        _pages["Preferences"] = Preferences();
        var nav = Stack(6);
        nav.Children.Add(new TextBlock { Text = "PENGULOD", FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Accent, Margin = new Thickness(10, 8, 0, 0) });
        nav.Children.Add(new TextBlock { Text = "NATIVE LINUX", FontSize = 10, LetterSpacing = 2, Foreground = Muted, Margin = new Thickness(12, 0, 0, 32) });
        foreach (var title in _pages.Keys)
        {
            var button = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            button.Click += (_, _) => SelectPage(title); _navigation[title] = button; nav.Children.Add(button);
        }
        var side = new DockPanel();
        var version = Text($"v{ProductInfo.Version}\nSkyrim SE / AE / VR", true); version.Margin = new Thickness(12, 0, 0, 10);
        DockPanel.SetDock(version, Dock.Bottom); side.Children.Add(version); side.Children.Add(nav);
        var sidebar = new Border { Background = Surface, Padding = new Thickness(12, 16), Child = side };
        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(28, 20, 28, 16), RowSpacing = 12 };
        _notice.IsVisible = !string.IsNullOrEmpty(_notice.Text);
        main.Children.Add(_notice);
        Grid.SetRow(_page, 1); main.Children.Add(_page);
        var logHeader = Row(Button("Open run folder", () => Open(_lastRunFolder)), Button("Clear visible log", () => { _logLines.Clear(); _log.Text = ""; }));
        var logPanel = Stack(8, logHeader, _log);
        var expander = new Expander { Header = "Activity log", Content = logPanel, HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = false };
        Grid.SetRow(expander, 2); main.Children.Add(expander);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 16, RowSpacing = 10 };
        footer.Children.Add(_status); Grid.SetColumn(_elapsed, 1); footer.Children.Add(_elapsed); Grid.SetColumn(_cancel, 3); footer.Children.Add(_cancel);
        _lodActions = Row(Button("Scan load order", () => RunScan(false)), Primary("Generate LOD", () => RunScan(true)));
        Grid.SetColumn(_lodActions, 2); footer.Children.Add(_lodActions);
        Grid.SetRow(_progress, 1); Grid.SetColumnSpan(_progress, 4); footer.Children.Add(_progress);
        Grid.SetRow(footer, 3); main.Children.Add(footer);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("205,*") };
        root.Children.Add(sidebar); Grid.SetColumn(main, 1); root.Children.Add(main); Content = root;
        SelectPage(_selectedPage);
    }
    private void SelectPage(string title)
    {
        _selectedPage = title; _page.Content = _pages[title];
        if (_lodActions is not null) _lodActions.IsVisible = title is "Overview" or "Generation settings";
        foreach (var (key, button) in _navigation) { button.Background = key == title ? Brush.Parse("#343B47") : Brushes.Transparent; button.Foreground = key == title ? Accent : Brushes.White; }
    }

    private Control Overview()
    {
        var source = Choice("Source", nameof(DesktopSettings.SourceMode), ["Mod Organizer 2", "Data folder / Vortex"]);
        var profileRow = Row(Button("Read profiles", LoadProfiles), _profiles);
        _profiles.SelectionChanged += (_, _) => { if (_profiles.SelectedItem is string p) _settings.Profile = p; };
        var advanced = new Expander { Header = "Advanced source paths", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Stack(10,
                PathField("Wine / Proton prefix", nameof(DesktopSettings.WinePrefix), hint: "Optional pfx folder; resolves C: and custom drive mappings from MO2's INI."),
                PathField("Mods folder override", nameof(DesktopSettings.ModsFolder)),
                PathField("Profiles folder override", nameof(DesktopSettings.ProfilesFolder)),
                PathField("Overwrite folder override", nameof(DesktopSettings.OverwriteFolder)),
                PathField("Skyrim.ini override", nameof(DesktopSettings.GameIni), file: true)) };
        var mo2 = Stack(12, PathField("MO2 instance", nameof(DesktopSettings.Instance), hint: "Folder containing ModOrganizer.ini. Read the instance directly; MO2 does not need to be running."),
            Field("Profile", nameof(DesktopSettings.Profile), "Blank uses the instance's selected profile."), profileRow);
        var data = Stack(12, PathField("Game / Data folder", nameof(DesktopSettings.GameFolder)),
            PathField("plugins.txt", nameof(DesktopSettings.PluginsFile), file: true, hint: "Choose an explicit plugin list; an empty file works for a vanilla game."));
        mo2.Bind(IsVisibleProperty, new Binding(nameof(DesktopSettings.SourceMode)) { Source = _settings, Converter = new FuncValueConverter<string, bool>(v => v == "Mod Organizer 2") });
        data.Bind(IsVisibleProperty, new Binding(nameof(DesktopSettings.SourceMode)) { Source = _settings, Converter = new FuncValueConverter<string, bool>(v => v != "Mod Organizer 2") });
        ((StackPanel)advanced.Content!).Children.Insert(1, PathField("Game folder override (MO2)", nameof(DesktopSettings.GameFolder), hint: "Use this if the gamePath saved in MO2 points to an old location."));
        var s = Stack(16, Heading("Build a distant world", "Generate object, tree and grass LOD directly from your Linux installation."),
            Card("01  SOURCE", Stack(12, Row(source, Choice("Game edition", nameof(DesktopSettings.Edition), ["SE / AE", "VR"])), mo2, data, advanced)),
            Card("02  BUILD", Stack(12,
                PathField("Output mod folder", nameof(DesktopSettings.OutputFolder), hint: "A separate, empty folder or an existing AnvilLOD output. Install the result with your mod manager."),
                Field("Worldspaces", nameof(DesktopSettings.Worldspaces), "Comma-separated editor IDs, such as Tamriel, DLC2SolstheimWorld. * scans all worlds with LOD settings."),
                Check("Clear previous AnvilLOD output before generation", nameof(DesktopSettings.CleanOutput)),
                Text("Incremental generation is the default. Dynamic LOD requires the matching AnvilLOD SKSE DLL installed in the Windows game.", true),
                Button("Open output", () => Open(_settings.OutputFolder)))),
            Card("SCAN RESULTS", Stack(12, _counts, _worlds, _issues)),
            Card("TREE COVERAGE & COST", Stack(12, Text("Measured mesh counts and estimated geometry size, before block merging. Full coverage is saved in scan-report.json.", true), _treeCoverage)));
        return Scroll(s);
    }

    private Control GenerationSettings()
    {
        var groups = Stack(12);
        foreach (var group in GenerationOptions.All.GroupBy(o => o.Group))
        {
            var rows = Stack(12);
            foreach (var spec in group)
            {
                var value = new OptionValue(spec, _settings, ScheduleSave);
                Control input;
                if (spec.Default is bool)
                {
                    var check = new CheckBox { Content = spec.Label }; Bind(check, ToggleButton.IsCheckedProperty, value, nameof(OptionValue.Flag)); input = check;
                }
                else if (spec.Default is decimal)
                {
                    var number = new NumericUpDown { Minimum = spec.Min, Maximum = spec.Max, Increment = spec.Max <= 10 ? .05m : 1, FormatString = "0.##", Width = 145 };
                    Bind(number, NumericUpDown.ValueProperty, value, nameof(OptionValue.Number)); input = Labeled(spec.Label, number);
                }
                else
                {
                    var choice = new ComboBox { ItemsSource = spec.Choices, MinWidth = 170 }; Bind(choice, SelectingItemsControl.SelectedItemProperty, value, nameof(OptionValue.Choice)); input = Labeled(spec.Label, choice);
                }
                var row = Stack(2, input, Text(spec.Help, true));
                // Bindings hold their sources weakly. Keep the per-option adapter alive with its row.
                row.DataContext = value;
                row.Tag = (spec.Label + " " + spec.Help).ToLowerInvariant(); rows.Children.Add(row);
            }
            var card = new Expander { Header = group.Key, Content = rows, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12), Background = Surface };
            groups.Children.Add(card);
        }
        var search = new TextBox { PlaceholderText = "Find a setting…" };
        search.TextChanged += (_, _) =>
        {
            string filter = (search.Text ?? "").Trim().ToLowerInvariant();
            foreach (Expander group in groups.Children)
            {
                var rows = (StackPanel)group.Content!;
                foreach (var row in rows.Children) row.IsVisible = row.Tag?.ToString()?.Contains(filter) == true;
                group.IsVisible = rows.Children.Any(c => c.IsVisible);
                if (filter.Length > 0) group.IsExpanded = true;
            }
        };
        return Scroll(Stack(16, Heading("Generation settings", "Tune quality, compatibility and runtime features. Settings are saved automatically."),
            Row(Button("Balanced preset", () => ApplyPreset("Balanced")), Button("High quality", () => ApplyPreset("High")), Button("Objects only", () => ApplyPreset("Objects"))),
            PathField("DynDOLOD folder", nameof(DesktopSettings.DynDolodFolder), hint: "Optional source of mesh rules and configuration files."),
            PathField("Custom rule file", nameof(DesktopSettings.CustomRulesFile), file: true), search, groups));
    }
    private Control MeshMaker() => Scroll(Stack(16,
        Heading("Mesh maker", "Simplify full NIF models into LOD assets. Review the generated meshes before distributing them."),
        Card("INPUT & OUTPUT", Stack(12, MultiField("Input NIF files or folders · one per line", nameof(DesktopSettings.MakerInputs)),
            Row(Button("Add folder", () => AddInput(nameof(DesktopSettings.MakerInputs), false)), Button("Add NIF files", () => AddInput(nameof(DesktopSettings.MakerInputs), true))),
            PathField("Mesh output folder", nameof(DesktopSettings.MakerOutput)), Field("Group name", nameof(DesktopSettings.MakerGroup)),
            Field("Owner plugin", nameof(DesktopSettings.MakerPlugin), "For example MyMod.esp; used to name the automatically loaded rule file."))),
        Card("SIMPLIFICATION", Stack(10, Field("Mesh levels", nameof(DesktopSettings.MakerLevels), "0,1,2 correspond to LOD4, LOD8, LOD16."),
            Number("Detail reduction", nameof(DesktopSettings.MakerDetail), .1m, 10, .1m), Number("Minimum model size", nameof(DesktopSettings.MakerMinSize), 0, 100000),
            Choice("Triangle budget", nameof(DesktopSettings.MakerBudget), ["Recommended", "Off", "Custom"]), Field("Custom budgets", nameof(DesktopSettings.MakerCustomBudget), "Three triangle limits, one per level. 0 means unlimited."),
            Check("Write mesh rules", nameof(DesktopSettings.MakerRules)), Check("Replace existing generated files", nameof(DesktopSettings.MakerOverwrite)), Primary("Make LOD meshes", RunMaker))),
        new Expander { Header = "Convert Oldrim meshes to SSE", HorizontalAlignment = HorizontalAlignment.Stretch, Content = Stack(10,
            PathField("Source mod folder", nameof(DesktopSettings.ConvertSource)), PathField("Converted output folder", nameof(DesktopSettings.ConvertOutput)),
            Check("Convert all meshes (otherwise LOD meshes only)", nameof(DesktopSettings.ConvertAllMeshes)), Check("Replace existing converted files", nameof(DesktopSettings.ConvertOverwrite)), Button("Convert meshes", RunConvert)) },
        ToolResults("Mesh maker")));
    private Control ModAuthor() => Scroll(Stack(16,
        Heading("Mod author", "Inspect a plugin's models, then create missing LOD meshes and tree billboards."),
        Card("AUTHORING", Stack(12, Text("Uses the game source and profile configured on Overview.", true),
            Field("Plugin", nameof(DesktopSettings.AuthorPlugin), "Use a plugin filename, or * for missing tree billboards across the load order."),
            PathField("Authoring output folder", nameof(DesktopSettings.AuthorOutput)),
            Number("Minimum object size", nameof(DesktopSettings.AuthorMinSize), 0, 100000), Number("Minimum tree height", nameof(DesktopSettings.AuthorTreeHeight), 0, 100000),
            Check("Create LOD meshes", nameof(DesktopSettings.AuthorMeshes)), Check("Render tree billboards", nameof(DesktopSettings.AuthorBillboards)), Check("Write mesh rules", nameof(DesktopSettings.AuthorRules)),
            Check("Create stand-ins for missing tree models", nameof(DesktopSettings.AuthorStandIns)), Check("Replace generated files", nameof(DesktopSettings.AuthorOverwrite)),
            Row(Button("Analyze only", () => RunAuthor(false)), Primary("Generate assets", () => RunAuthor(true))))), ToolResults("Mod author")));
    private Control TextureTools() => Scroll(Stack(16,
        Heading("Texture tools", "Find unsupported DDS files and convert BC6H textures into BC7 override files."),
        Card("SCAN & REPAIR", Stack(12, MultiField("Input mod folders · one per line", nameof(DesktopSettings.TextureInputs)),
            Button("Add folder", () => AddInput(nameof(DesktopSettings.TextureInputs), false)), Check("Also scan all enabled MO2 mod folders", nameof(DesktopSettings.TextureAllMods)),
            PathField("Repaired texture output", nameof(DesktopSettings.TextureOutput), hint: "A separate override mod folder; original textures remain in place."),
            Text("The native converter preserves dimensions and mip levels. HDR values are clamped to 0–1 for Skyrim's BC7 format. Loose DDS files are scanned; packed BSA textures are not repaired.", true),
            Row(Button("Scan textures", ScanTextures), Primary("Repair BC6H textures", RepairTextures)))), _textures));
    private Control RulesPage()
    {
        var grid = Table(("Mask / FormID", "Mask"), ("LOD4", "Lod4"), ("LOD8", "Lod8"), ("LOD16", "Lod16"), ("LOD32", "Lod32"), ("Grid", "Grid"), ("Reference", "Reference"), ("Flags", "Flags"), ("Description", "Description"));
        grid.IsReadOnly = false; grid.ItemsSource = _rules; grid.MinHeight = 320; grid.MaxHeight = 600;
        foreach (var col in grid.Columns.OfType<DataGridTextColumn>()) if (col.Binding is Binding binding) binding.Mode = BindingMode.TwoWay;
        return Scroll(Stack(16, Heading("Custom mesh rules", "First matching rule wins. Saving puts the catch-all \\ rule last."),
            Text("LOD choices: None, Level0–Level3, Full model, Billboard1–Billboard6. Grid: None, Near LOD, Far LOD, Far Full, Never Fade LOD. Flags: 1 VWD, 2 NoGlow, 4 NoMATO, 8 Dynamic, 16 TREE.", true),
            Row(Button("Load rule file", LoadRules), Button("Add rule", () => { _rules.Add(new RuleEntry { Mask = "new-mask", Lod4 = "Level0", Lod8 = "Level1", Lod16 = "Level2", IsCustom = true }); }),
                Button("Remove selected", () => { if (grid.SelectedItem is RuleEntry rule) _rules.Remove(rule); }),
                Primary("Save rules as…", async () => { grid.CommitEdit(); await SaveRules(); })), grid,
            Text("Save a custom file to activate these rules for generation. The editor writes LODGen entries only; retain your original file if it also contains other settings.", true)));
    }
    private Control Preferences() => Scroll(Stack(16, Heading("Preferences", "Control resource use and keep portable presets for different mod lists."),
        Card("PROCESSING", Stack(12, Choice("Texture compute", nameof(DesktopSettings.TextureCompute), ["Cpu", "Auto", "Vulkan"]),
            Text("Optional Vulkan BC7 encoding for large opaque textures. Auto requires a measured speedup and a passing quality check. Alpha foliage and small images use CPU. Grass collision caching is a separate backend.", true),
            Button("Check texture acceleration", CheckTextureCompute),
            Number("Worker threads", nameof(DesktopSettings.MaxThreads), 0, 1024), Text("0 uses available processors minus one. Cancellation waits for active engine operations to finish.", true),
            PathField("Run history and cache folder", nameof(DesktopSettings.CacheFolder), hint: "Blank uses " + SettingsStore.CacheRoot),
            Button("Detect Steam installation", DetectInstall))),
        Card("SETTINGS & PRESETS", Stack(12, Text("Settings file\n" + _store.FilePath, true),
            Row(Button("Import settings…", ImportSettings), Button("Export settings…", ExportSettings)),
            Text("Presets include source paths and generation choices. Importing replaces the current settings; your existing settings are backed up first.", true))),
        Card("ABOUT PENGULOD", Stack(10, Text(ProductInfo.Description),
            Text("Native Avalonia desktop • managed .NET engine • GPL-3.0-only"),
            Text("Built on CageTV’s AnvilLOD. Maintained by opteron-x86 and PenguLOD contributors.", true),
            Text("The generator runs on Linux without Wine. Skyrim and its SKSE DLL still run under Proton/Wine. Install the matching AnvilLOD game plugin separately. This alpha includes native terrain, tree reduction, seasonal output and object atlases; compare new output in game before replacing a known-good setup.", true),
            Button("Open project on GitHub", () => Open(ProductInfo.Repository))))));

    private Border ToolResults(string page)
    {
        // Each page gets a separate view; one shared Control cannot have multiple visual parents.
        var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, MinHeight = 180, MaxHeight = 400, Name = page == "Mesh maker" ? "MakerResults" : "AuthorResults" };
        _resultViews[page] = grid; return Card("RESULTS", grid);
    }
    private readonly Dictionary<string, DataGrid> _resultViews = [];
    private static StackPanel Stack(double spacing, params Control[] controls)
    { var p = new StackPanel { Spacing = spacing }; foreach (var c in controls) p.Children.Add(c); return p; }
    private static WrapPanel Row(params Control[] controls)
    { var p = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var c in controls) { c.Margin = new Thickness(0, 0, 10, 6); p.Children.Add(c); } return p; }
    private static TextBlock Text(string text, bool muted = false) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = muted ? Muted : Brushes.White, FontSize = muted ? 12 : 14, LineHeight = muted ? 19 : 22 };
    private static Control Heading(string title, string subtitle) => Stack(6, new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.SemiBold }, Text(subtitle, true));
    private static Border Card(string title, Control content) => new() { Background = Surface, CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Child = Stack(14, new TextBlock { Text = title, Foreground = Accent, FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.3 }, content) };
    private static ScrollViewer Scroll(Control content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
    private static Control Labeled(string label, Control input) => Stack(5, Text(label), input);
    private static void Bind(AvaloniaObject target, AvaloniaProperty property, object source, string path) => target.Bind(property, new Binding(path) { Source = source, Mode = BindingMode.TwoWay });
    private Control Field(string label, string property, string? hint = null)
    {
        var box = new TextBox { Name = property, HorizontalAlignment = HorizontalAlignment.Stretch }; Bind(box, TextBox.TextProperty, _settings, property);
        return hint is null ? Labeled(label, box) : Stack(5, Text(label), box, Text(hint, true));
    }
    private Control MultiField(string label, string property)
    {
        var box = new TextBox { Name = property, AcceptsReturn = true, MinHeight = 90, MaxHeight = 180, TextWrapping = TextWrapping.Wrap }; Bind(box, TextBox.TextProperty, _settings, property); return Labeled(label, box);
    }
    private Control PathField(string label, string property, bool file = false, string? hint = null)
    {
        var box = new TextBox { Name = property, PlaceholderText = "/path/to/" + (file ? "file" : "folder") }; Bind(box, TextBox.TextProperty, _settings, property);
        var browse = Button("Browse…", async () =>
        {
            var path = await Pick(file, label);
            if (path is not null) typeof(DesktopSettings).GetProperty(property)!.SetValue(_settings, path);
        });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 }; grid.Children.Add(box); Grid.SetColumn(browse, 1); grid.Children.Add(browse);
        return hint is null ? Labeled(label, grid) : Stack(5, Text(label), grid, Text(hint, true));
    }
    private Control Number(string label, string property, decimal min, decimal max, decimal step = 1)
    {
        var input = new NumericUpDown { Minimum = min, Maximum = max, Increment = step, FormatString = "0.##", Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        Bind(input, NumericUpDown.ValueProperty, _settings, property); return Labeled(label, input);
    }
    private Control Choice(string label, string property, string[] choices)
    { var input = new ComboBox { ItemsSource = choices, MinWidth = 240 }; Bind(input, SelectingItemsControl.SelectedItemProperty, _settings, property); return Labeled(label, input); }
    private Control Check(string label, string property)
    { var input = new CheckBox { Content = label }; Bind(input, ToggleButton.IsCheckedProperty, _settings, property); return input; }
    private Button Button(string title, Action action) => Button(title, () => { action(); return Task.CompletedTask; });
    private Button Button(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Padding = new Thickness(14, 9), Name = title.Replace(" ", "").Replace("…", "") };
        button.Click += async (_, _) => { try { Notice(""); await action(); } catch (Exception e) { Notice(e.Message); _messages.Enqueue(e.ToString()); } }; return button;
    }
    private Button Primary(string title, Func<Task> action)
    { var b = Button(title, action); b.Background = Accent; b.Foreground = Brush.Parse("#191A1C"); b.FontWeight = FontWeight.SemiBold; return b; }
    private static DataGrid Table(params (string Header, string Property)[] columns)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, MinHeight = 150, MaxHeight = 350 };
        foreach (var (header, property) in columns) grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = DataGridLength.Auto });
        return grid;
    }
    private async Task<string?> Pick(bool file, string title)
    {
        if (file)
        { var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title }); return result.FirstOrDefault()?.TryGetLocalPath(); }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title }); return folders.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<string?> SavePath(string title, string name, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = name, DefaultExtension = extension, ShowOverwritePrompt = true });
        return file?.TryGetLocalPath();
    }
    private void Open(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) { Notice("No folder is available yet."); return; }
        if (!path.StartsWith("https://", StringComparison.Ordinal)) path = RequestFactory.PathValue(path, _settings);
        if (!path.StartsWith("https://", StringComparison.Ordinal) && !Directory.Exists(path) && !File.Exists(path)) throw new DirectoryNotFoundException("The path does not exist yet: " + path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
