using PenguLOD.Desktop.Models;
using PenguLOD.Desktop.Services;
using PenguLOD.Plugins;
using Avalonia.Controls;

namespace PenguLOD.Desktop;

public sealed partial class MainWindow
{
    private readonly TextBlock _terrainSummary = Text("No terrain generated in this session.", true);
    private readonly DataGrid _terrainResults = Table(("Worldspace", "Worldspace"), ("LAND cells", "LandscapeCells"), ("Fallback tiles", "ExistingTilesUsed"), ("Output tiles", "Tiles"));
    private Control TerrainPage() => Scroll(Stack(16,
        Heading("Terrain"),
        Card("Output", Stack(12,
            PathField("Terrain output", nameof(DesktopSettings.TerrainOutput)),
            WorldspaceField("Worldspaces", nameof(DesktopSettings.Worldspaces), false, "Shared with Object LOD. Use comma-separated worldspace IDs, or * for all worlds. Choose… lists the worldspaces in the load order."),
            Check("Fill missing landscape from existing terrain LOD", nameof(DesktopSettings.TerrainFallback)),
            Help(Check(TerrainGeneration.FillUncoveredLandOption, nameof(DesktopSettings.TerrainFillUncovered)),
                "Where a tile has neither landscape records nor existing terrain LOD. Off: the run stops and lists those tiles."),
            Help(Check(TerrainGeneration.SubstituteMissingTexturesOption, nameof(DesktopSettings.TerrainSubstituteTextures)),
                "Off: the run stops and lists every missing texture."),
            Check("Generate seasonal terrain from Seasons of Skyrim swaps", nameof(DesktopSettings.Seasons)),
            Check("Generate water LOD", nameof(DesktopSettings.TerrainWater)),
            Check("Use generated terrain for object LOD", nameof(DesktopSettings.UseGeneratedTerrain)),
            Button("Open terrain output", () => Open(RequestFactory.RequiredPath(_settings.TerrainOutput, "a terrain output folder", _settings))))),
        Card("Textures & edges", Stack(12,
            Row(Number("Texture size", nameof(DesktopSettings.TerrainTextureSize), 64, 2048, 64), Number("Brightness", nameof(DesktopSettings.TerrainBrightness), .1m, 4, .05m)),
            Row(Number("Vertex colour strength", nameof(DesktopSettings.TerrainVertexColor), 0, 1, .05m), Number("Texture repeats per cell", nameof(DesktopSettings.TerrainRepeats), 1, 128, 1)),
            Help(Number("Edge skirt depth", nameof(DesktopSettings.TerrainSkirt), 0, 16384, 128), "Covers gaps between terrain LOD levels."))),
        Card("Materials", Stack(12,
            Help(Number("Source normal strength", nameof(DesktopSettings.TerrainNormalStrength), 0, 2, .1m), "0 uses only the terrain slope."),
            Check("Calibrate PBR albedo", nameof(DesktopSettings.TerrainPbr)),
            Help(Check("Look for textures/pbr equivalents", nameof(DesktopSettings.TerrainPbrTwins)), "Enable when your in-game shader setup uses those materials."),
            Row(Number("PBR scale", nameof(DesktopSettings.TerrainPbrScale), .1m, 4, .05m), Number("PBR gamma", nameof(DesktopSettings.TerrainPbrGamma), .1m, 4, .05m)),
            Number("PBR ambient occlusion", nameof(DesktopSettings.TerrainAo), 0, 1, .05m))),
        Card("Terrain shadows", Stack(12,
            Help(Check("Export Community Shaders heightmaps", nameof(DesktopSettings.TerrainHeightmaps)), "Requires complete rectangular coverage. Install the generated DDS files loose."),
            Number("Maximum heightmap dimension", nameof(DesktopSettings.TerrainHeightmapSize), 128, 16384, 128))),
        Card("Results", Stack(12, _terrainSummary, _terrainResults))));

    internal Task RunTerrain() => RunPersistentPipeline("Terrain generation", _settings.Snapshot(), s => RequestFactory.Stages(s, terrain: true));
}
