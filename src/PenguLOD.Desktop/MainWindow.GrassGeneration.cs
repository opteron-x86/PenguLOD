using System.Text.Json;
using PenguLOD.Desktop.Models;
using PenguLOD.Desktop.Services;
using PenguLOD.Plugins;
using Avalonia.Controls;

namespace PenguLOD.Desktop;

public sealed partial class MainWindow
{
    private readonly TextBlock _grassSummary = Text("No grass cache generated yet.", true);

    private Control GrassGenerationPage() => Scroll(Stack(16,
        Heading("Grass cache"),
        Card("Output", Stack(12,
            PathField("Grass cache output", nameof(DesktopSettings.GrassOutput)),
            Field("Worldspaces", nameof(DesktopSettings.GrassWorldspaces), "Use all, or comma-separated worldspace IDs. This selection only affects the grass cache."),
            Check("Use generated grass cache for LOD", nameof(DesktopSettings.UseGeneratedGrass)),
            Button("Open grass output", () => Open(RequestFactory.RequiredPath(_settings.GrassOutput, "a grass output folder", _settings))))),
        Card("Placement", Stack(12,
            Row(Help(Choice("Placement", nameof(DesktopSettings.GrassPlacement), ["Smooth", "Vanilla"]), "Smooth blends grass across cell boundaries. Vanilla follows the game's placement."),
                Help(Choice("Collision rejection", nameof(DesktopSettings.GrassRejection), ["Auto", "CPU", "GPU"]), "GPU requires a compatible external FasterNGIO build."),
                Choice("Seasons", nameof(DesktopSettings.GrassSeasons), ["Auto", "On", "Off"])),
            Help(Check("Small-area preview", nameof(DesktopSettings.GrassPreview)), "Choose one worldspace and a separate output folder for a preview."),
            Row(Number("Cell X", nameof(DesktopSettings.GrassCellX), -32768, 32767, 1),
                Number("Cell Y", nameof(DesktopSettings.GrassCellY), -32768, 32767, 1),
                Help(Number("Radius in cells", nameof(DesktopSettings.GrassRadius), 0, 32, 1), "0 generates one cell. 1 generates a 3 × 3 area.")))),
        new Expander { Header = "Advanced", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Content = Stack(12,
            PathField("FasterNGIO executable override", nameof(DesktopSettings.GrassExecutable), file: true, hint: "Leave blank to use the bundled generator."),
            PathField("GrassControl.ini override", nameof(DesktopSettings.GrassNgioConfig), file: true, hint: "Leave blank to use the selected profile's grass configuration."),
            Row(Help(Number("Minimum grass size", nameof(DesktopSettings.GrassMinSize), 0, 512, 1), "0 uses the profile setting. Lower values produce denser grass."),
                Number("Writer threads", nameof(DesktopSettings.GrassWriters), 1, 64, 1)),
            Check("Use visible geometry for rejection (experimental)", nameof(DesktopSettings.GrassRenderGeometry))) },
        Card("Results", _grassSummary)));

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
