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
        var snapshot = _settings.Snapshot();
        return RunPersistentPipeline(thenTexturesAndLod ? "Grass, textures and LOD" : "Grass caching", snapshot,
            s => new GenerationPipelineRequest(RequestFactory.Grass(s),
                thenTexturesAndLod ? RequestFactory.Textures(s) : null,
                thenTexturesAndLod ? RequestFactory.Scan(s, true) : null,
                s.ResumeCompletedStages, s.VerifySourceContents));
    }
}
