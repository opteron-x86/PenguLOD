using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using PenguLOD.Core;
using PenguLOD.Core.Lod;
using PenguLOD.Core.Paths;
using PenguLOD.Core.Textures;
using PenguLOD.Desktop.Models;
using PenguLOD.Desktop.Services;
using PenguLOD.Plugins;
using PenguLOD.Plugins.Mo2;
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

namespace PenguLOD.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly SettingsStore _store;
    private DesktopSettings _settings;
    private readonly JobRunner _runner = new();
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly Queue<string> _logLines = new();
    private readonly DispatcherTimer _logTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private WrapPanel? _pageActions;
    private readonly ContentControl _page = new();
    private readonly TextBlock _status = new() { Text = "Ready", TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _elapsed = new() { Foreground = Muted };
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, Foreground = Accent };
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("monospace"), FontSize = 12, MinHeight = 130, MaxHeight = 270 };
    private readonly ProgressBar _progress = new() { Height = 3, IsVisible = false, IsIndeterminate = true };
    private readonly Button _cancel = new() { Content = "Cancel job", IsEnabled = false };
    private readonly ObservableCollection<RuleEntry> _rules = [];
    private readonly ComboBox _profiles = new() { MinWidth = 180, PlaceholderText = "Available profiles" };
    private readonly TextBlock _counts = new() { Text = "No scan results yet.", TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _worlds = Table(("Worldspace", "World"), ("LOD 4", "Lod4"), ("LOD 8", "Lod8"), ("LOD 16", "Lod16"), ("LOD 32", "Lod32"));
    private readonly DataGrid _resourceCoverage = Table(("Resource", "Path"), ("Selected source", "Origin"), ("Enabled mod / Data", "FromEnabledMods"));
    private readonly DataGrid _objectCoverage = Table(("Model", "Model"), ("Source level", "Level"), ("Status", "Status"), ("Source triangles", "SourceTriangles"), ("LOD triangles", "Triangles"), ("Budget", "Budget"), ("Details", "Reason"));
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
        Title = ProductInfo.Name;
        Width = 1240; Height = 900; MinWidth = 960; MinHeight = 680;
        Background = Brush.Parse("#121720");
        FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter");
        FontSize = 14;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PenguLOD.Desktop/Assets/pengulod.png"))); } catch (FileNotFoundException) { }
        _cancel.Click += (_, _) => { _runner.Cancel(); _status.Text = "Cancelling… waiting for active workers"; _cancel.IsEnabled = false; };
        _profiles.SelectionChanged += (_, _) => { if (_profiles.SelectedItem is string p) _settings.Profile = p; };
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
        else if (_store.MigratedFrom is { } source) Notice($"Imported settings from {source}.");
    }

    private bool _saveAllowed = true;
    private void AttachSettings() => _settings.PropertyChanged += (_, _) => { ScheduleSave(); UpdateSourceSummary(); };
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
        foreach (var control in new Control[] { _notice, _page, _status, _elapsed, _cancel, _progress, _log, _profiles, _counts, _worlds, _issues, _textures, _textureGenResults, _textureGenSummary, _grassSummary, _pipelineSummary, _pipelineStages, _terrainSummary, _terrainResults, _treeCoverage, _objectCoverage, _resourceCoverage, _diagnosticSummary, _diagnosticIssues, _diagnosticEstimates, _seasonCoverage, _costFolder, _costBefore, _costAfter, _outputFolders, _packageFolder, _outputSummary, _costGroups, _costBlocks, _settingChanges, _sourceSummary })
        {
            if (control.Parent is Panel panel) panel.Children.Remove(control);
            else if (control.Parent is ContentControl content) content.Content = null;
        }
        _pages.Clear(); _navigation.Clear();
        _pages["Overview"] = GenerationOverview();
        _pages["Game & resources"] = GameSource();
        _pages["Grass cache"] = GrassGenerationPage();
        _pages["Textures"] = TextureGenerationPage();
        _pages["Terrain"] = TerrainPage();
        _pages["Object LOD"] = ObjectLodPage();
        _pages["Scan results"] = ScanResults();
        _pages["Reports & outputs"] = ReportsPage();
        _pages["Diagnostics"] = DiagnosticsPage();
        _pages["Mesh maker"] = MeshMaker();
        _pages["Mod author"] = ModAuthor();
        _pages["Texture tools"] = TextureTools();
        _pages["Custom rules"] = RulesPage();
        _pages["Preferences"] = Preferences();
        var nav = Stack(3);
        nav.Children.Add(new TextBlock { Text = "PENGULOD", FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Accent, Margin = new Thickness(10, 8, 0, 14) });
        var sections = new Dictionary<string, string> { ["Overview"] = "Setup", ["Grass cache"] = "Generation",
            ["Scan results"] = "Review", ["Mesh maker"] = "Tools" };
        foreach (var title in _pages.Keys)
        {
            if (sections.TryGetValue(title, out var section))
                nav.Children.Add(new TextBlock { Text = section, FontSize = 11, Foreground = Muted, Margin = new Thickness(12, 12, 0, 4) });
            var button = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 9), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            button.Click += (_, _) => SelectPage(title); _navigation[title] = button; nav.Children.Add(button);
        }
        var side = new DockPanel();
        var version = Text($"v{ProductInfo.Version}\nSkyrim SE / AE / VR", true); version.Margin = new Thickness(12, 0, 0, 10);
        DockPanel.SetDock(version, Dock.Bottom); side.Children.Add(version); side.Children.Add(new ScrollViewer { Content = nav });
        var sidebar = new Border { Background = Surface, Padding = new Thickness(12, 16), Child = side };
        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(28, 20, 28, 16), RowSpacing = 12 };
        _notice.IsVisible = !string.IsNullOrEmpty(_notice.Text);
        main.Children.Add(_notice);
        Grid.SetRow(_page, 1); main.Children.Add(_page);
        var logHeader = Row(Button("Open run folder", () => Open(_lastRunFolder)), Button("Clear visible log", () => { _logLines.Clear(); _log.Text = ""; }));
        var logPanel = Stack(8, logHeader, _log);
        var expander = new Expander { Header = "Activity log", Content = logPanel, HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = false };
        Grid.SetRow(expander, 2); main.Children.Add(expander);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 16, RowSpacing = 8 };
        footer.Children.Add(_status); Grid.SetColumn(_elapsed, 1); footer.Children.Add(_elapsed); Grid.SetColumn(_cancel, 2); footer.Children.Add(_cancel);
        _pageActions = Row(); _pageActions.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(_pageActions, 1); Grid.SetColumnSpan(_pageActions, 3); footer.Children.Add(_pageActions);
        Grid.SetRow(_progress, 2); Grid.SetColumnSpan(_progress, 3); footer.Children.Add(_progress);
        Grid.SetRow(footer, 3); main.Children.Add(footer);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("205,*") };
        root.Children.Add(sidebar); Grid.SetColumn(main, 1); root.Children.Add(main); Content = root;
        SelectPage(_selectedPage);
    }
    private void SelectPage(string title)
    {
        _selectedPage = title; _page.Content = _pages[title];
        UpdatePageActions(title);
        foreach (var (key, button) in _navigation) { button.Background = key == title ? Brush.Parse("#343B47") : Brushes.Transparent; button.Foreground = key == title ? Accent : Brushes.White; }
    }

    private Control GameSource()
    {
        var source = Choice("Source", nameof(DesktopSettings.SourceMode), ["Mod Organizer 2", "Data folder / Vortex"]);
        var profileRow = Row(Button("Read profiles", LoadProfiles), _profiles);
        var prefix = PathField("Wine / Proton prefix", nameof(DesktopSettings.WinePrefix), hint: "Optional pfx folder; resolves C: and custom drive mappings from MO2's INI.");
        prefix.IsVisible = !OperatingSystem.IsWindows();
        var advanced = new Expander { Header = "Advanced source paths", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Stack(10,
                prefix,
                PathField("Mods folder override", nameof(DesktopSettings.ModsFolder)),
                PathField("Profiles folder override", nameof(DesktopSettings.ProfilesFolder)),
                PathField("Overwrite folder override", nameof(DesktopSettings.OverwriteFolder)),
                PathField("Skyrim.ini override", nameof(DesktopSettings.GameIni), file: true)) };
        var mo2 = Stack(12, PathField("Mod Organizer 2 instance", nameof(DesktopSettings.Instance)),
            Field("Profile", nameof(DesktopSettings.Profile)), profileRow);
        var data = Stack(12, PathField("Game / Data folder", nameof(DesktopSettings.GameFolder)),
            PathField("plugins.txt", nameof(DesktopSettings.PluginsFile), file: true, hint: "Choose an explicit plugin list; an empty file works for a vanilla game."));
        mo2.Bind(IsVisibleProperty, new Binding(nameof(DesktopSettings.SourceMode)) { Source = _settings, Converter = new FuncValueConverter<string, bool>(v => v == "Mod Organizer 2") });
        data.Bind(IsVisibleProperty, new Binding(nameof(DesktopSettings.SourceMode)) { Source = _settings, Converter = new FuncValueConverter<string, bool>(v => v != "Mod Organizer 2") });
        var mo2Game = PathField("Game folder override (MO2)", nameof(DesktopSettings.Mo2GameFolder), hint: "Use this if the gamePath saved in MO2 points to an old location.");
        mo2Game.Bind(IsVisibleProperty, new Binding(nameof(DesktopSettings.SourceMode)) { Source = _settings, Converter = new FuncValueConverter<string, bool>(v => v == "Mod Organizer 2") });
        ((StackPanel)advanced.Content!).Children.Insert(1, mo2Game);
        // An override hidden in a collapsed section is easy to forget; show the section when one is set.
        advanced.IsExpanded = !string.IsNullOrWhiteSpace(_settings.Mo2GameFolder) && _settings.SourceMode == "Mod Organizer 2";
        return Scroll(Stack(16, Heading("Game & resources"),
            Card("Game source", Stack(12, Row(source, Choice("Game edition", nameof(DesktopSettings.Edition), ["SE / AE", "VR"])), mo2, data, advanced)),
            Card("LOD resources", Stack(12,
                PathField("Primary resource folder (optional)", nameof(DesktopSettings.DynDolodFolder), hint: "Additional LOD rules, recipes and models. Resources in enabled mods are found automatically."),
                MultiField("Additional resource folders · one per line", nameof(DesktopSettings.ResourceFolders)),
                PathField("Custom rule file", nameof(DesktopSettings.CustomRulesFile), file: true)))));
    }

    private Control ScanResults() => Scroll(Stack(16, Heading("Scan results"),
        Card("Worldspaces", Stack(12, _counts, _worlds, _issues)),
        Card("Resource files", Help(_resourceCoverage, "Enabled mods take priority over external resource folders.")),
        Card("Automatic object LOD", Help(_objectCoverage, "Source levels 0, 1 and 2 normally supply LOD 4, 8 and 16.")),
        Card("Tree coverage", Help(_treeCoverage, "Geometry counts before objects are combined into LOD blocks."))));

    private Control ObjectLodPage()
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
                    // Whole-number options step by one; fractional options use a step that suits their range.
                    var number = spec.Whole
                        ? new NumericUpDown { Minimum = spec.Min, Maximum = spec.Max, Increment = 1, FormatString = "0", Width = 145 }
                        : new NumericUpDown { Minimum = spec.Min, Maximum = spec.Max, Increment = spec.Max <= 10 ? .05m : 1, FormatString = "0.##", Width = 145 };
                    Bind(number, NumericUpDown.ValueProperty, value, nameof(OptionValue.Number)); input = Labeled(spec.Label, number);
                }
                else
                {
                    var choice = new ComboBox { ItemsSource = spec.Choices, MinWidth = 170 }; Bind(choice, SelectingItemsControl.SelectedItemProperty, value, nameof(OptionValue.Choice)); input = Labeled(spec.Label, choice);
                }
                var row = Help(Stack(2, input), spec.Help);
                // Bindings hold their sources weakly. Keep the per-option adapter alive with its row.
                row.DataContext = value;
                row.Tag = (spec.Label + " " + spec.Help).ToLowerInvariant(); rows.Children.Add(row);
            }
            var card = new Expander { Header = group.Key, Content = rows, IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12), Background = Surface };
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
        return Scroll(Stack(16, Heading("Object LOD"),
            Card("Output", Stack(12,
                PathField("Object LOD output", nameof(DesktopSettings.OutputFolder)),
                Field("Worldspaces", nameof(DesktopSettings.Worldspaces), "Shared with terrain. Use comma-separated worldspace IDs, or * for all worlds."),
                Help(Check("Clear previous output before generation", nameof(DesktopSettings.CleanOutput)), "Rebuilds the entire object LOD output. Leave this off to reuse unchanged blocks."),
                Row(Button("Open object output", () => Open(_settings.OutputFolder)), Button("View scan results", () => SelectPage("Scan results"))))),
            Row(Button("Balanced preset", () => ApplyPreset("Balanced")), Button("High quality", () => ApplyPreset("High")), Button("Objects only", () => ApplyPreset("Objects"))),
            search, groups));
    }
    private Control MeshMaker() => Scroll(Stack(16,
        Heading("Mesh maker"),
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
        Heading("Mod author"),
        Card("AUTHORING", Stack(12,
            Field("Plugin", nameof(DesktopSettings.AuthorPlugin), "Use a plugin filename, or * for missing tree billboards across the load order."),
            PathField("Authoring output folder", nameof(DesktopSettings.AuthorOutput)),
            Number("Minimum object size", nameof(DesktopSettings.AuthorMinSize), 0, 100000), Number("Minimum tree height", nameof(DesktopSettings.AuthorTreeHeight), 0, 100000),
            Check("Create LOD meshes", nameof(DesktopSettings.AuthorMeshes)), Check("Render tree billboards", nameof(DesktopSettings.AuthorBillboards)), Check("Write mesh rules", nameof(DesktopSettings.AuthorRules)),
            Check("Create stand-ins for missing tree models", nameof(DesktopSettings.AuthorStandIns)), Check("Replace generated files", nameof(DesktopSettings.AuthorOverwrite)),
            Row(Button("Analyze only", () => RunAuthor(false)), Primary("Generate assets", () => RunAuthor(true))))), ToolResults("Mod author")));
    private Control TextureTools() => Scroll(Stack(16,
        Heading("Texture tools"),
        Card("SCAN & REPAIR", Stack(12, MultiField("Input mod folders · one per line", nameof(DesktopSettings.TextureInputs)),
            Button("Add folder", () => AddInput(nameof(DesktopSettings.TextureInputs), false)), Check("Also scan all enabled MO2 mod folders", nameof(DesktopSettings.TextureAllMods)),
            PathField("Repaired texture output", nameof(DesktopSettings.TextureOutput), hint: "A separate override mod folder; original textures remain in place."),
            Row(Button("Scan textures", ScanTextures), Help(Primary("Repair BC6H textures", RepairTextures), "Converts loose BC6H textures to BC7 overrides. Packed BSA textures are not repaired.")))), _textures));
    private Control RulesPage()
    {
        var grid = Table(("Mask / FormID", "Mask"), ("LOD4", "Lod4"), ("LOD8", "Lod8"), ("LOD16", "Lod16"), ("LOD32", "Lod32"), ("Grid", "Grid"), ("Reference", "Reference"), ("Flags", "Flags"), ("Description", "Description"));
        grid.IsReadOnly = false; grid.ItemsSource = _rules; grid.MinHeight = 320; grid.MaxHeight = 600;
        foreach (var col in grid.Columns.OfType<DataGridTextColumn>())
        {
            col.IsReadOnly = col.Header as string is "Reference" or "Flags";
            if (col.Binding is Binding binding) binding.Mode = col.IsReadOnly ? BindingMode.OneWay : BindingMode.TwoWay;
        }
        return Scroll(Stack(16, Heading("Custom mesh rules"),
            Text("LOD choices: None, Level0–Level3, Full model, Billboard1–Billboard6. Billboard1–6 currently use the same billboard behavior. Grid: None, Near LOD, Far LOD, Far Full, Never Fade LOD.", true),
            Text("Reference and Flags are read-only: imported values are preserved when saving, but these actions are not applied during generation.", true),
            Row(Button("Load rule file", LoadRules), Button("Add rule", () => { _rules.Add(new RuleEntry { Mask = "new-mask", Lod4 = "Level0", Lod8 = "Level1", Lod16 = "Level2", IsCustom = true }); }),
                Button("Remove selected", () => { if (grid.SelectedItem is RuleEntry rule) _rules.Remove(rule); }),
                Primary("Save rules as…", async () => { grid.CommitEdit(); await SaveRules(); })), grid,
            Text("Save a custom file to activate these rules for generation. The editor writes LODGen entries only; retain your original file if it also contains other settings.", true)));
    }
    private Control Preferences() => Scroll(Stack(16, Heading("Preferences"),
        Card("PROCESSING", Stack(12, Help(Choice("Texture compute", nameof(DesktopSettings.TextureCompute), ["Cpu", "Auto", "Vulkan"]), "Auto uses Vulkan when its speed and quality checks pass. CPU is used for unsupported textures."),
            Button("Check texture acceleration", CheckTextureCompute),
            Help(Number("Worker threads", nameof(DesktopSettings.MaxThreads), 0, 1024), "0 uses available processors minus one."),
            PathField("Run history and cache folder", nameof(DesktopSettings.CacheFolder), hint: "Blank uses " + SettingsStore.CacheRoot),
            Button("Detect Steam installation", DetectInstall))),
        Card("SETTINGS & PRESETS", Stack(12, Text("Settings file\n" + _store.FilePath, true),
            Row(Button("Import settings…", ImportSettings), Button("Export settings…", ExportSettings)),
            Text("Import replaces the current settings and saves a backup.", true))),
        Card("ABOUT PENGULOD", Stack(10, Text(ProductInfo.Description),
            Text("GPL-3.0-only"),
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
    private static Control Heading(string title) => new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeight.SemiBold };
    private static T Help<T>(T control, string? text) where T : Control
    {
        if (!string.IsNullOrWhiteSpace(text))
            ToolTip.SetTip(control, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        return control;
    }
    private static Border Card(string title, Control content) => new() { Background = Surface, CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Child = Stack(14, new TextBlock { Text = title, Foreground = Accent, FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.3 }, content) };
    private static ScrollViewer Scroll(Control content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
    private static Control Labeled(string label, Control input) => Stack(5, Text(label), input);
    private static void Bind(AvaloniaObject target, AvaloniaProperty property, object source, string path) => target.Bind(property, new Binding(path) { Source = source, Mode = BindingMode.TwoWay });
    private Control Field(string label, string property, string? hint = null)
    {
        var box = new TextBox { Name = property, HorizontalAlignment = HorizontalAlignment.Stretch }; Bind(box, TextBox.TextProperty, _settings, property);
        return Help(Labeled(label, box), hint);
    }
    private Control MultiField(string label, string property)
    {
        var box = new TextBox { Name = property, AcceptsReturn = true, MinHeight = 90, MaxHeight = 180, TextWrapping = TextWrapping.Wrap }; Bind(box, TextBox.TextProperty, _settings, property); return Labeled(label, box);
    }
    private Control PathField(string label, string property, bool file = false, string? hint = null)
    {
        var box = new TextBox { Name = property, PlaceholderText = (OperatingSystem.IsWindows() ? @"C:\path\to\" : "/path/to/") + (file ? "file" : "folder") }; Bind(box, TextBox.TextProperty, _settings, property);
        var browse = Button("Browse…", async () =>
        {
            var path = await Pick(file, label);
            if (path is not null) typeof(DesktopSettings).GetProperty(property)!.SetValue(_settings, path);
        });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 }; grid.Children.Add(box); Grid.SetColumn(browse, 1); grid.Children.Add(browse);
        return Help(Labeled(label, grid), hint);
    }
    private Control Number(string label, string property, decimal min, decimal max, decimal step = 1)
    {
        var input = new NumericUpDown { Name = property, Minimum = min, Maximum = max, Increment = step, FormatString = "0.##", Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        Bind(input, NumericUpDown.ValueProperty, _settings, property); return Labeled(label, input);
    }
    private Control Choice(string label, string property, string[] choices)
    { var input = new ComboBox { Name = property, ItemsSource = choices, MinWidth = 240 }; Bind(input, SelectingItemsControl.SelectedItemProperty, _settings, property); return Labeled(label, input); }
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
