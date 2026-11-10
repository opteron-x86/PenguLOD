using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PenguLOD.Desktop.Models;

namespace PenguLOD.Desktop;

public sealed partial class MainWindow
{
    private readonly TextBlock _sourceSummary = Text("");
    private Button? _runStagesButton;

    private void UpdateSourceSummary()
    {
        bool managed = _settings.SourceMode == "Mod Organizer 2";
        string path = managed ? _settings.Instance : _settings.GameFolder;
        _sourceSummary.Text = string.IsNullOrWhiteSpace(path) ? "Choose a game source to get started."
            : managed ? $"{path}\nProfile: {(string.IsNullOrWhiteSpace(_settings.Profile) ? "Selected instance profile" : _settings.Profile)}"
            : $"{path}\n{_settings.Edition}";
        if (_runStagesButton is not null)
            _runStagesButton.IsEnabled = _settings.PipelineGrass || _settings.PipelineTextures || _settings.PipelineTerrain || _settings.PipelineLod;
    }

    private void UpdatePageActions(string page)
    {
        if (_pageActions is null) return;
        _pageActions.Children.Clear();
        _runStagesButton = null;
        void Add(Button button) { button.Margin = new Thickness(8, 0, 0, 0); _pageActions.Children.Add(button); }
        switch (page)
        {
            case "Overview":
                Add(Button("Check setup", async () => { SelectPage("Diagnostics"); await RunDiagnostics(); }));
                _runStagesButton = Primary("Run selected stages", RunSelectedPipeline);
                Add(_runStagesButton);
                break;
            case "Game & resources":
                Add(Button("Scan load order", () => RunScan(false)));
                Add(Button("Generation overview", () => SelectPage("Overview")));
                break;
            case "Grass cache": Add(Primary("Generate grass cache", () => RunGrassGeneration(false))); break;
            case "Textures": Add(Primary("Generate textures", () => RunTextureGeneration(false))); break;
            case "Terrain": Add(Primary("Generate terrain", RunTerrain)); break;
            case "Object LOD":
                Add(Button("Scan load order", () => RunScan(false)));
                Add(Primary("Generate object LOD", () => RunScan(true)));
                break;
            case "Scan results": Add(Primary("Scan load order", () => RunScan(false))); break;
        }
        _pageActions.IsVisible = _pageActions.Children.Count > 0;
        _pageActions.IsEnabled = !Busy;
        UpdateSourceSummary();
    }

    private Control WorkflowSource()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        row.Children.Add(_sourceSummary);
        var change = Button("Change game source", () => SelectPage("Game & resources"));
        change.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(change, 1); row.Children.Add(change);
        return Card("Game source", row);
    }

    private Control WorkflowWorldspaces()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        row.Children.Add(Field("Terrain and object LOD", nameof(DesktopSettings.Worldspaces), "Comma-separated worldspace IDs, such as Tamriel. * includes all worlds with LOD settings."));
        var grass = Field("Grass cache", nameof(DesktopSettings.GrassWorldspaces), "Independent of terrain and object LOD. Use all, or comma-separated worldspace IDs.");
        Grid.SetColumn(grass, 1); row.Children.Add(grass);
        return Card("Worldspaces", row);
    }

    private Grid StageSelection()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 14, RowSpacing = 14 };
        var stages = new[]
        {
            ("1. Grass cache", nameof(DesktopSettings.PipelineGrass), nameof(DesktopSettings.GrassOutput), "Grass cache"),
            ("2. Textures", nameof(DesktopSettings.PipelineTextures), nameof(DesktopSettings.TextureGenOutput), "Textures"),
            ("3. Terrain", nameof(DesktopSettings.PipelineTerrain), nameof(DesktopSettings.TerrainOutput), "Terrain"),
            ("4. Object LOD", nameof(DesktopSettings.PipelineLod), nameof(DesktopSettings.OutputFolder), "Object LOD")
        };
        for (int i = 0; i < stages.Length; i++)
        {
            var (title, selected, output, page) = stages[i];
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            var check = Check(title, selected); check.Name = selected;
            header.Children.Add(check);
            var configure = Button("Settings", () => SelectPage(page));
            configure.Name = "Configure" + selected;
            Help(configure, "Open " + page.ToLowerInvariant() + " settings.");
            Grid.SetColumn(configure, 1); header.Children.Add(configure);
            var folder = PathField("Output folder", output);
            var card = new Border { Name = "Stage" + selected, Background = Surface, CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Child = Stack(10, header, folder) };
            Grid.SetRow(card, i / 2); Grid.SetColumn(card, i % 2); grid.Children.Add(card);
        }
        return grid;
    }
}
