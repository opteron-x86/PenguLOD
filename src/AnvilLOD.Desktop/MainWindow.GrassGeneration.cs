using System.Text.Json;
using AnvilLOD.Desktop.Models;
using AnvilLOD.Desktop.Services;
using AnvilLOD.Plugins;
using Avalonia.Controls;

namespace AnvilLOD.Desktop;

public sealed partial class MainWindow
{
    private readonly TextBlock _grassSummary = Text("No grass cache generated yet.", true);

    private Control GrassGenerationPage() => Scroll(Stack(16,
        Heading("Grass cache", "Generate NGIO-compatible grass offline with FasterNGIO. Uses the game source and profile selected on Overview."),
        Card("OUTPUT", Stack(12,
            PathField("Grass output mod folder", nameof(DesktopSettings.GrassOutput), hint: "Choose a separate empty folder. The app creates its grass subfolder. Completed runs retain the previous output alongside it."),
            Field("Worldspaces", nameof(DesktopSettings.GrassWorldspaces), "all, or editor IDs separated by commas, such as Tamriel, DLC2SolstheimWorld. Independent of the LOD worldspace selection."),
            Check("Use generated grass cache for LOD", nameof(DesktopSettings.UseGeneratedGrass)),
            Text("Install the completed output as its own mod and disable the old grass cache. Keep NGIO installed. Game and NGIO settings are read without being changed.", true))),
        Card("GENERATION", Stack(12,
            Row(Choice("Placement", nameof(DesktopSettings.GrassPlacement), ["Smooth", "Vanilla"]),
                Choice("Collision rejection", nameof(DesktopSettings.GrassRejection), ["Auto", "CPU", "GPU"]),
                Choice("Seasons", nameof(DesktopSettings.GrassSeasons), ["Auto", "On", "Off"])),
            Text("Smooth blends grass across cell and texture boundaries. Vanilla follows the game's placement. The bundled generator uses CPU BVH; GPU requires a compatible external FasterNGIO build.", true),
            Check("Small-area preview", nameof(DesktopSettings.GrassPreview)),
            Row(Number("Cell X", nameof(DesktopSettings.GrassCellX), -32768, 32767, 1),
                Number("Cell Y", nameof(DesktopSettings.GrassCellY), -32768, 32767, 1),
                Number("Radius in cells", nameof(DesktopSettings.GrassRadius), 0, 32, 1)),
            Text("Preview needs one worldspace and a separate output folder. Radius 0 generates one cell; radius 1 selects a 3 × 3 area. Inspect the cache in game before a full run.", true))),
        new Expander { Header = "Advanced", Content = Stack(12,
            PathField("FasterNGIO executable override", nameof(DesktopSettings.GrassExecutable), file: true, hint: "Blank uses the bundled native CPU generator. Choose a compatible native executable to use Vulkan GPU acceleration."),
            PathField("GrassControl.ini override", nameof(DesktopSettings.GrassNgioConfig), file: true, hint: "Blank reads the winning configuration from the selected profile, including Overwrite and *_NGIO.ini files."),
            Row(Number("Minimum grass size · 0 = profile", nameof(DesktopSettings.GrassMinSize), 0, 512, 1),
                Number("Writer threads", nameof(DesktopSettings.GrassWriters), 1, 64, 1)),
            Text("Worker threads follow Preferences. Lower minimum grass size means denser grass. Profile INIs and NGIO settings apply unless overridden here.", true),
            Check("Use visible geometry for rejection (experimental)", nameof(DesktopSettings.GrassRenderGeometry))) },
        Row(Primary("Generate grass cache", () => RunGrassGeneration(false)), Button("Grass, textures, then LOD", () => RunGrassGeneration(true))),
        Card("RESULTS", Stack(12, _grassSummary,
            Button("Open grass output", () => Open(RequestFactory.RequiredPath(_settings.GrassOutput, "a grass output folder", _settings)))))));

    internal Task RunGrassGeneration(bool thenTexturesAndLod)
    {
        var s = _settings.Snapshot();
        return RunJob(thenTexturesAndLod ? "Grass, textures and LOD" : "Grass caching", s, (progress, ct) =>
        {
            // Request preparation reads the profile and output folders, so it belongs on the worker too.
            progress.Report("Checking grass generation settings and output paths...");
            if (thenTexturesAndLod && s.GrassPreview) throw new ArgumentException("Turn off small-area preview before running grass, textures and LOD together.");
            var grass = RequestFactory.Grass(s);
            var textures = thenTexturesAndLod ? RequestFactory.Textures(s) : null;
            if (textures is { ModelFilter.Length: > 0 }) throw new ArgumentException("Clear the texture model filter before running the combined job.");
            var lod = thenTexturesAndLod ? RequestFactory.Scan(s, true) with { GeneratedGrassFolder = grass.OutputFolder, GeneratedTextureFolder = textures!.OutputFolder } : null;
            ct.ThrowIfCancellationRequested();
            var cached = GrassGeneration.Run(grass, progress, ct);
            TextureGenerationResult? generated = null;
            ScanSummary? summary = null;
            string? laterError = null;
            if (textures is not null)
            {
                try
                {
                    ct.ThrowIfCancellationRequested(); progress.Report("Grass cache published. Starting texture generation...");
                    generated = TextureGeneration.Run(textures, progress, ct);
                    if (!generated.Published) laterError = "Texture generation failed; LOD was not started. Review the texture results and log.";
                    else { ct.ThrowIfCancellationRequested(); summary = ScanPipeline.Run(lod!, progress, ct); }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { laterError = ex.Message; progress.Report("ERROR: " + ex); }
            }
            return (Grass: cached, Textures: generated, Lod: summary, LaterError: laterError);
        }, (result, folder, elapsed) =>
        {
            _grassSummary.Text = $"{result.Grass.Files:N0} cache files · {result.Grass.Instances:N0} grass instances · {result.Grass.EmptyFiles:N0} empty cells · {result.Grass.Warnings.Count:N0} warnings · {result.Grass.Elapsed:hh\\:mm\\:ss}";
            if (result.Grass.PreviousOutput is { } previous) _grassSummary.Text += "\nPrevious output: " + previous;
            File.WriteAllText(Path.Combine(folder, "grass-report.json"), JsonSerializer.Serialize(result.Grass, new JsonSerializerOptions { WriteIndented = true }));
            _settings.UseGeneratedGrass = !s.GrassPreview;
            if (result.Textures is { } t)
            {
                _lastTextureGeneration = t; _textureGenResults.ItemsSource = t.Items;
                _textureGenSummary.Text = $"{t.Files:N0} texture files · {t.Errors:N0} failures · {t.Warnings.Count:N0} warnings";
                File.WriteAllText(Path.Combine(folder, "texture-report.json"), JsonSerializer.Serialize(t, new JsonSerializerOptions { WriteIndented = true }));
                if (t.Published) _settings.UseGeneratedTextures = true;
            }
            Save();
            if (result.LaterError is { } error)
            {
                _status.Text = "Grass complete; later stage failed";
                Notice(error + " The completed grass cache is available. After correcting the issue, use 'Textures, then LOD' to resume without rebuilding grass.");
                return;
            }
            if (result.Lod is { } summary)
            {
                ScanReport.Write(Path.Combine(folder, "scan-report.json"), summary, elapsed); ShowSummary(summary);
                if (summary.Generation is { BlocksFailed: > 0 } || summary.Grids.Count == 0)
                { _status.Text = "Grass and textures complete; review LOD results"; Notice("Review Scan results and the run log before installing output."); return; }
            }
            _status.Text = s.GrassPreview ? "Grass preview complete" : "Grass cache complete";
            if (thenTexturesAndLod) _status.Text = "Grass, textures and LOD complete";
            if (result.Grass.Warnings.Count > 0) _status.Text += " with warnings";
            Notice(s.GrassPreview ? "Preview generated. Inspect this area in game; use a full cache for normal play and LOD generation."
                : "Install the grass output as a separate mod and disable the old grass cache. Review the run log and inspect grass in game. Keep the previous cache until you are satisfied.");
        });
    }
}
