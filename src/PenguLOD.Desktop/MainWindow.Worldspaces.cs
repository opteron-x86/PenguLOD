using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PenguLOD.Desktop.Models;
using PenguLOD.Desktop.Services;
using PenguLOD.Plugins;

namespace PenguLOD.Desktop;

public sealed partial class MainWindow
{
    /// <summary>A worldspace selection field with a button that lists the load order's worldspaces to choose from.</summary>
    /// <param name="grass">The grass cache field: its word for every worldspace is "all", and LOD settings do not matter to it.</param>
    private Control WorldspaceField(string label, string property, bool grass, string? hint = null)
    {
        var box = new TextBox { Name = property, HorizontalAlignment = HorizontalAlignment.Stretch }; Bind(box, TextBox.TextProperty, _settings, property);
        var choose = Button("Choose…", () => ChooseWorldspaces(property, grass));
        choose.Name = "Choose" + property;
        ToolTip.SetTip(choose, "Lists the worldspaces in the selected game source.");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 }; grid.Children.Add(box); Grid.SetColumn(choose, 1); grid.Children.Add(choose);
        return Help(Labeled(label, grid), hint);
    }

    /// <summary>Reads the worldspaces of the selected game source, then lets the user tick the ones to generate.</summary>
    internal async Task ChooseWorldspaces(string property, bool grass)
    {
        if (_runner.IsRunning) throw new InvalidOperationException("Wait for the active job to finish.");
        var game = RequestFactory.Game(_settings.Snapshot());
        IReadOnlyList<WorldspaceInfo> worlds;
        _page.IsEnabled = false; if (_pageActions is not null) _pageActions.IsEnabled = false; _progress.IsVisible = true;
        _status.Text = "Reading worldspaces…";
        try
        {
            var progress = new Progress<string>(_messages.Enqueue);
            worlds = await Task.Run(() => WorldspaceCatalog.List(game, progress));
            _status.Text = "Ready";
        }
        catch { _status.Text = "Could not read worldspaces"; throw; }
        finally { _page.IsEnabled = true; if (_pageActions is not null) _pageActions.IsEnabled = true; _progress.IsVisible = false; }
        if (worlds.Count == 0) throw new InvalidOperationException("The selected game source has no worldspaces. Check the game source and the enabled plugins.");

        var settings = typeof(DesktopSettings).GetProperty(property)!;
        var picker = new WorldspacePicker(worlds, (string?)settings.GetValue(_settings) ?? "", grass)
        { Background = Background, FontFamily = FontFamily, FontSize = FontSize, Icon = Icon };
        if (await picker.ShowDialog<string?>(this) is { } chosen) settings.SetValue(_settings, chosen);
    }

    /// <summary>
    /// The list of worldspaces to tick. Closes with the text for the settings field, or null when cancelled.
    /// </summary>
    internal sealed class WorldspacePicker : Window
    {
        private sealed record Entry(WorldspaceInfo World, CheckBox Check, Grid Row, string Search);
        private readonly List<Entry> _entries = [];
        private readonly string _everything;
        private readonly CheckBox _all;
        private readonly TextBox _filter = new() { PlaceholderText = "Filter by ID, name, plugin or parent", Name = "WorldspaceFilter" };
        private readonly TextBlock _summary = Text("", true);
        private readonly Button _use;
        private readonly StackPanel _rows = new();
        private const string Columns = "2.7*,2*,2.4*,1.7*,130";

        internal WorldspacePicker(IReadOnlyList<WorldspaceInfo> worlds, string current, bool grass)
        {
            _everything = grass ? "all" : "*";
            Title = grass ? "Worldspaces for the grass cache" : "Worldspaces for terrain and object LOD";
            Width = 980; Height = 720; MinWidth = 720; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var wanted = RequestFactory.Worldspaces(current) ?? [];
            bool everything = grass ? wanted.Contains("all", StringComparer.OrdinalIgnoreCase) : wanted.Length == 0;
            var known = worlds.Select(w => w.EditorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = everything ? [] : wanted.Where(w => !known.Contains(w)).ToArray();
            var ticked = wanted.ToHashSet(StringComparer.OrdinalIgnoreCase);
            int usable = worlds.Count(w => Usable(w, grass));

            _all = new CheckBox
            {
                Name = "AllWorldspaces", IsChecked = everything,
                Content = grass ? $"Every worldspace ({worlds.Count:N0})" : $"Every worldspace with LOD settings ({usable:N0})",
            };
            // Those that can be chosen come first, by ID: the size order of the command-line list scatters them.
            foreach (var world in worlds.OrderByDescending(w => Usable(w, grass)).ThenBy(w => w.EditorId, StringComparer.OrdinalIgnoreCase))
            {
                bool usableWorld = Usable(world, grass);
                var check = new CheckBox
                {
                    Content = new TextBlock { Text = world.EditorId, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap }, Name = "World_" + world.EditorId, IsEnabled = usableWorld,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IsChecked = usableWorld && !everything && ticked.Contains(world.EditorId), Margin = new Thickness(0),
                };
                ToolTip.SetTip(check, world.EditorId);
                check.IsCheckedChanged += (_, _) => Refresh();
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), ColumnSpacing = 14, MinHeight = 34 };
                row.Children.Add(check);
                AddCell(row, 1, world.Name ?? "", usableWorld); AddCell(row, 2, world.Plugin, usableWorld); AddCell(row, 3, world.Parent ?? "", usableWorld);
                AddCell(row, 4, !world.HasLodSettings ? "no LOD settings" : world.Cells is { } cells ? $"{cells} × {cells} cells" : "unreadable", usableWorld && world.HasLodSettings);
                _entries.Add(new(world, check, row, string.Join('\n', world.EditorId, world.Name, world.Plugin, world.Parent)));
                _rows.Children.Add(row);
            }

            // The header and the rows share one width: both leave the same room for the scroll bar.
            var gutter = new Thickness(0, 0, 18, 0); _rows.Margin = gutter;
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), ColumnSpacing = 14, Margin = new Thickness(0, 8, 18, 8) };
            foreach (var (title, column) in new[] { ("ID", 0), ("Name", 1), ("Plugin", 2), ("Parent", 3), ("LOD grid", 4) })
            { var cell = new TextBlock { Text = title.ToUpperInvariant(), Foreground = Accent, FontSize = 11, FontWeight = FontWeight.SemiBold }; Grid.SetColumn(cell, column); header.Children.Add(cell); }

            var selectShown = new Button { Content = "Select shown", Name = "SelectShown", Padding = new Thickness(14, 9) };
            var clearShown = new Button { Content = "Clear shown", Name = "ClearShown", Padding = new Thickness(14, 9) };
            selectShown.Click += (_, _) => SetShown(true); clearShown.Click += (_, _) => SetShown(false);
            _filter.TextChanged += (_, _) => Refresh();
            _all.IsCheckedChanged += (_, _) => Refresh();
            var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
            tools.Children.Add(_filter); Grid.SetColumn(selectShown, 1); tools.Children.Add(selectShown); Grid.SetColumn(clearShown, 2); tools.Children.Add(clearShown);

            _use = new Button { Content = "Use selection", Name = "UseSelection", Padding = new Thickness(18, 9), Background = Accent, Foreground = Brush.Parse("#191A1C"), FontWeight = FontWeight.SemiBold };
            var cancel = new Button { Content = "Cancel", Name = "CancelSelection", Padding = new Thickness(14, 9) };
            _use.Click += (_, _) => Close(Selection()); cancel.Click += (_, _) => Close(null);
            var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
            _summary.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(_summary); Grid.SetColumn(cancel, 1); footer.Children.Add(cancel); Grid.SetColumn(_use, 2); footer.Children.Add(_use);

            string intro = grass
                ? $"{worlds.Count:N0} worldspaces in this load order. The grass cache skips those the game grows no grass in."
                : $"{worlds.Count:N0} worldspaces in this load order, {usable:N0} with LOD settings. Terrain and object LOD skip a worldspace without LOD settings.";
            var top = Stack(12, Text(intro, true), _all, tools);
            if (unknown.Length > 0)
                top.Children.Add(new TextBlock
                {
                    Name = "UnknownWorldspaces", TextWrapping = TextWrapping.Wrap, Foreground = Accent,
                    Text = $"Not in this load order, and left out when you use this selection: {string.Join(", ", unknown)}.",
                });

            var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(22) };
            layout.Children.Add(top);
            var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            var table = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            table.Children.Add(header); Grid.SetRow(scroll, 1); table.Children.Add(scroll);
            var list = new Border { Background = Surface, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 6, 4, 6), Child = table };
            Grid.SetRow(list, 1); layout.Children.Add(list); Grid.SetRow(footer, 2); layout.Children.Add(footer);
            Content = layout;
            Refresh();
        }

        private static bool Usable(WorldspaceInfo world, bool grass) => grass || world.HasLodSettings;

        private static void AddCell(Grid row, int column, string text, bool usable)
        {
            var cell = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = usable ? Brushes.White : Muted, FontSize = 13 };
            if (text.Length > 0) ToolTip.SetTip(cell, text);
            Grid.SetColumn(cell, column); row.Children.Add(cell);
        }

        private bool Shown(Entry entry) => string.IsNullOrWhiteSpace(_filter.Text)
            || _filter.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => entry.Search.Contains(word, StringComparison.OrdinalIgnoreCase));

        private void SetShown(bool selected)
        {
            if (selected) _all.IsChecked = false;
            foreach (var entry in _entries.Where(e => e.Check.IsEnabled && Shown(e))) entry.Check.IsChecked = selected;
        }

        private void Refresh()
        {
            bool everything = _all.IsChecked == true;
            int shown = 0;
            foreach (var entry in _entries) { bool visible = Shown(entry); entry.Row.IsVisible = visible; if (visible) shown++; }
            _rows.Opacity = everything ? .45 : 1; _rows.IsEnabled = !everything;
            int selected = _entries.Count(e => e.Check.IsChecked == true);
            _use.IsEnabled = everything || selected > 0;
            _summary.Text = (everything ? "Every worldspace is selected." : selected == 0 ? "Tick at least one worldspace." : $"{selected:N0} selected.")
                + (shown < _entries.Count ? $" Showing {shown:N0} of {_entries.Count:N0}." : "");
        }

        /// <summary>The text for the settings field: the word for everything, or the ticked IDs in list order.</summary>
        internal string Selection() => _all.IsChecked == true ? _everything
            : string.Join(", ", _entries.Where(e => e.Check.IsChecked == true).Select(e => e.World.EditorId));
    }
}
