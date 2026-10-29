using System.Text.Json;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Desktop.Models;
using AnvilLOD.Desktop.Services;
using AnvilLOD.Plugins;
using Avalonia.Controls;

namespace AnvilLOD.Desktop;

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
    private readonly TextBlock _pipelineSummary = Text("No pipeline job has completed in this session. Rerun the same selection to resume a saved job.", true);
    private readonly DataGrid _pipelineStages = Table(("Stage", "Stage"), ("Status", "Status"), ("Reused", "Reused"),
        ("Prepare s", "Preparation"), ("Process s", "Processing"), ("Write worker s", "Writing"), ("Verify s", "Verification"));
    private string? _lastPipelineFolder;

    private Control PipelinePage() => Scroll(Stack(16,
        Heading("Pipeline", "Generate a complete set in stages. Completed stages survive cancellation, failure and application restarts."),
        Card("STAGES", Stack(12,
            Row(Check("Cache grass", nameof(DesktopSettings.PipelineGrass)), Check("Textures", nameof(DesktopSettings.PipelineTextures)),
                Check("Terrain and water", nameof(DesktopSettings.PipelineTerrain)), Check("Object / tree / grass LOD", nameof(DesktopSettings.PipelineLod))),
            Text("Configure the source and output folders on Overview, Grass cache, Texture generation and Terrain. Each stage needs its own output folder. New grass and textures feed LOD automatically.", true))),
        Card("RESUME & VERIFICATION", Stack(12,
            Check("Run preflight before generation", nameof(DesktopSettings.PipelinePreflight)),
            Check("Reuse completed stages after verifying their files", nameof(DesktopSettings.ResumeCompletedStages)),
            Check("Verify all source asset contents", nameof(DesktopSettings.VerifySourceContents)),
            Text("Plugins and configuration are always content-checked. The normal input check uses file size and timestamps for large assets. Full verification hashes every source asset and can take longer on large lists. Published outputs are always content-verified before reuse.", true),
            Text("Changed settings or inputs invalidate the affected stage and its dependents. LOD is built in a separate folder and published only when successful; the previous output is retained alongside it.", true))),
        Row(Primary("Run / resume pipeline", RunSelectedPipeline), Button("Export pipeline request", ExportPipelineRequest), Button("Open saved job", () => Open(_lastPipelineFolder))),
        Card("STAGE RESULTS", Stack(12, _pipelineSummary, _pipelineStages,
            Text("Times are measured, not estimates. Process time includes generation and its I/O. Write worker time is the sum of instrumented file writes across workers and may overlap process time. The external grass backend includes writing in its process time.", true)))));

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
            return GenerationPipeline.Run(request(snapshot) with {TextureCompute=snapshot.TextureCompute}, cache, progress, ct);
        }, (result, folder, elapsed) =>
        {
            _lastPipelineFolder = result.JobFolder;
            if (result.Diagnostics is { } diagnostics) ShowDiagnostics(diagnostics, Path.Combine(result.JobFolder, "diagnostics.json"));
            File.WriteAllText(Path.Combine(folder, "pipeline.json"), JsonSerializer.Serialize(result.Journal, PersistentPipeline.Json));
            _pipelineStages.ItemsSource = result.Journal.Stages.Select(s => new PipelineStageRow(s.Plan.Title, s.Status,
                s.Reused ? "Yes" : "No", s.Reused ? "—" : s.Timings.GetValueOrDefault("preparation").ToString("F1"), s.Reused ? "—" : s.ProcessingSeconds.ToString("F1"),
                !s.Reused && s.Timings.TryGetValue("file-write-worker", out double writes) ? writes.ToString("F1") : "—",
                s.VerificationSeconds.ToString("F1"))).ToArray();
            _pipelineSummary.Text = $"Inputs: {result.Inputs.Files:N0} files checked in {result.Inputs.Seconds:F1}s. Total elapsed: {elapsed:hh\\:mm\\:ss}.\nSaved job: {result.JobFolder}";
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
            Notice("Completed. Review the stage results and warnings, then install the generated folders as separate mods. The saved job can verify and reuse these stages on the next run.");
        });

    public sealed record PipelineStageRow(string Stage, string Status, string Reused, string Preparation, string Processing, string Writing, string Verification);
}
