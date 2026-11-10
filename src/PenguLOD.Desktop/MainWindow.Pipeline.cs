using System.Text.Json;
using PenguLOD.Core.Pipeline;
using PenguLOD.Desktop.Models;
using PenguLOD.Desktop.Services;
using PenguLOD.Plugins;
using Avalonia.Controls;

namespace PenguLOD.Desktop;

public sealed partial class MainWindow
{
    private async Task ExportPipelineRequest()
    {
        var request = RequestFactory.Pipeline(_settings.Snapshot());
        var file = await SavePath("Export pipeline request", "PenguLOD-pipeline.json", "json");
        if (file is null) return;
        File.WriteAllText(file, JsonSerializer.Serialize(request, PersistentPipeline.Json));
        Notice("Exported the selected pipeline stages for the CLI: " + file);
    }
    private readonly TextBlock _pipelineSummary = Text("No runs yet.", true);
    private readonly DataGrid _pipelineStages = Table(("Stage", "Stage"), ("Status", "Status"), ("Reused", "Reused"),
        ("Generation (s)", "Processing"), ("Verification (s)", "Verification"));
    private string? _lastPipelineFolder;

    private Control GenerationOverview() => Scroll(Stack(16,
        Heading("Generation Overview"),
        WorkflowSource(),
        StageSelection(),
        WorkflowWorldspaces(),
        new Expander { Header = "Run options", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Content = Stack(12,
            Check("Check setup before generation", nameof(DesktopSettings.PipelinePreflight)),
            Help(Check("Reuse completed stages", nameof(DesktopSettings.ResumeCompletedStages)), "Completed output is checked before reuse. Changed inputs or settings trigger a rebuild."),
            Help(Check("Keep existing worlds and levels", nameof(DesktopSettings.ExpandOutputScope)), "Expands the run when needed to preserve the existing output's coverage."),
            Button("Review output scope", ReviewOutputScope),
            Help(Check("Verify all source files", nameof(DesktopSettings.VerifySourceContents)), "Reads every source file instead of checking file sizes and modification times. This can take longer on large mod lists."),
            Button("Export run configuration", ExportPipelineRequest)) },
        Card("Results", Stack(12, _pipelineSummary, _pipelineStages,
            Row(Button("View scan results", () => SelectPage("Scan results")), Button("Open saved job", () => Open(_lastPipelineFolder)))))));

    internal Task RunSelectedPipeline()
    {
        var s = _settings.Snapshot();
        return RunPersistentPipeline("Pipeline", s, RequestFactory.Pipeline);
    }

    private Task RunPersistentPipeline(string title, DesktopSettings snapshot, Func<DesktopSettings, GenerationPipelineRequest> request)
        => RunJob(title, snapshot, (progress, ct) =>
        {
            progress.Report("Checking pipeline settings and output paths...");
            string cache = RequestFactory.Optional(snapshot.CacheFolder) is { } folder ? RequestFactory.PathValue(folder, snapshot) : SettingsStore.CacheRoot;
            return GenerationPipeline.Run(request(snapshot) with {TextureCompute=snapshot.TextureCompute, ExpandOutputScope=snapshot.ExpandOutputScope}, cache, progress, ct);
        }, (result, folder, elapsed) =>
        {
            _lastPipelineFolder = result.JobFolder;
            if (result.Diagnostics is { } diagnostics) ShowDiagnostics(diagnostics, Path.Combine(result.JobFolder, "diagnostics.json"));
            File.WriteAllText(Path.Combine(folder, "pipeline.json"), JsonSerializer.Serialize(result.Journal, PersistentPipeline.Json));
            _pipelineStages.ItemsSource = result.Journal.Stages.Select(s => new PipelineStageRow(s.Plan.Title, s.Status,
                s.Reused ? "Yes" : "No", s.Reused ? "—" : s.Timings.GetValueOrDefault("preparation").ToString("F1"), s.Reused ? "—" : s.ProcessingSeconds.ToString("F1"),
                !s.Reused && s.Timings.TryGetValue("file-write-worker", out double writes) ? writes.ToString("F1") : "—",
                s.VerificationSeconds.ToString("F1"))).ToArray();
            _pipelineSummary.Text = $"Elapsed: {elapsed:hh\\:mm\\:ss}";
            if (result.Grass is { } grass)
            {
                File.WriteAllText(Path.Combine(folder, "grass-report.json"), JsonSerializer.Serialize(grass, PersistentPipeline.Json));
                _settings.UseGeneratedGrass = !grass.Preview;
                _grassSummary.Text = $"{grass.Files:N0} cache files · {grass.Instances:N0} instances · {grass.Warnings.Count:N0} warnings";
                if (grass.PreviousOutput is { } previous) _grassSummary.Text += "\nPrevious output: " + previous;
            }
            if (result.Textures is { } textures)
            {
                File.WriteAllText(Path.Combine(folder, "texture-report.json"), JsonSerializer.Serialize(textures, PersistentPipeline.Json));
                _lastTextureGeneration = textures; _textureGenResults.ItemsSource = textures.Items;
                _textureGenSummary.Text = $"{textures.Files:N0} files · {textures.Errors:N0} failures · {textures.Warnings.Count:N0} warnings";
                if (textures.Published) _settings.UseGeneratedTextures = string.IsNullOrWhiteSpace(snapshot.TextureGenFilter);
            }
            if (result.Terrain is { } terrain)
            {
                _settings.UseGeneratedTerrain = true;
                _terrainResults.ItemsSource = terrain.Worlds;
                bool stageReused=result.Journal.Stages.Any(stage=>stage.Plan.Id=="terrain"&&stage.Reused);
                string detail=stageReused ? "Completed terrain output verified and reused." : terrain.Performance is { } performance
                    ? $"{performance.BuiltTiles} tiles generated · {performance.ReusedTiles} tiles restored · {performance.Timings.GetValueOrDefault("terrain-sampling"):F2}s sampling · {performance.Timings.GetValueOrDefault("terrain-encoding"):F2}s encoding."
                    : $"{terrain.Seconds:F1}s generation.";
                _terrainSummary.Text = $"{terrain.Tiles.Count:N0} terrain tiles · {terrain.Warnings.Count:N0} warnings.\n{detail}\nPrevious output: {terrain.PreviousOutput ?? "none"}";
                File.WriteAllText(Path.Combine(folder, "terrain-report.json"), JsonSerializer.Serialize(terrain, PersistentPipeline.Json));
            }
            if (result.Lod is { } summary)
            { ScanReport.Write(Path.Combine(folder, "scan-report.json"), summary, elapsed); ShowSummary(summary); }
            else if (result.LodReceipt is { } receipt)
                _counts.Text = $"Reused completed LOD: {receipt.Files:N0} verified files. See the saved job for its generation report.";
            Save();
            if (!result.Success)
            {
                _status.Text = result.Error?.Contains("No matching LOD worldspaces", StringComparison.Ordinal) == true
                    ? "No matching worldspaces; previous LOD preserved"
                    : result.Grass is not null ? "Grass complete; later stage failed"
                    : result.Textures is { Published: false } ? "Textures not published" : title + " failed";
                Notice(result.Error + " Completed stages are saved. Correct the issue and run the same selection to resume without rebuilding verified stages.");
                return;
            }
            _status.Text = title switch
            {
                "Grass caching" => snapshot.GrassPreview ? "Grass preview complete" : "Grass cache complete",
                "Grass, textures and LOD" => "Grass, textures and LOD complete",
                _ => "Completed"
            };
            Notice("Generation complete. Review the results and warnings before installing the output.");
        });

    public sealed record PipelineStageRow(string Stage, string Status, string Reused, string Preparation, string Processing, string Writing, string Verification);
}
