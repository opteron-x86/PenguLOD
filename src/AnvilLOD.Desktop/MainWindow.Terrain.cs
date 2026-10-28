using AnvilLOD.Desktop.Models;
using AnvilLOD.Desktop.Services;
using AnvilLOD.Plugins;
using Avalonia.Controls;

namespace AnvilLOD.Desktop;

public sealed partial class MainWindow
{
    private readonly TextBlock _terrainSummary = Text("No terrain generated in this session.", true);
    private readonly DataGrid _terrainResults = Table(("Worldspace", "Worldspace"), ("LAND cells", "LandscapeCells"), ("Fallback tiles", "ExistingTilesUsed"), ("Output tiles", "Tiles"));
    private Control TerrainPage() => Scroll(Stack(16,
        Heading("Terrain", "Native landscape meshes, water LOD and texture tiles for levels 4, 8, 16 and 32."),
        Card("OUTPUT & COVERAGE", Stack(12,
            PathField("Terrain output", nameof(DesktopSettings.TerrainOutput), hint: "Use a separate new mod folder. Previous generations are backed up when replaced."),
            Text("Uses the source and worldspace selection on Overview. Incomplete terrain coverage stops publication. Review the report before enabling the output in your profile.", true),
            Check("Fill missing landscape from existing terrain LOD", nameof(DesktopSettings.TerrainFallback)),
            Check("Generate water LOD", nameof(DesktopSettings.TerrainWater)),
            Check("Use generated terrain in object LOD generation", nameof(DesktopSettings.UseGeneratedTerrain)))),
        Card("TEXTURES & EDGES", Stack(12,
            Row(Number("Texture size", nameof(DesktopSettings.TerrainTextureSize), 64, 2048, 64), Number("Brightness", nameof(DesktopSettings.TerrainBrightness), .1m, 4, .05m)),
            Row(Number("Vertex colour strength", nameof(DesktopSettings.TerrainVertexColor), 0, 1, .05m), Number("Texture repeats per cell", nameof(DesktopSettings.TerrainRepeats), 1, 128, 1)),
            Number("Edge skirt depth", nameof(DesktopSettings.TerrainSkirt), 0, 16384, 128),
            Text("Skirts cover gaps where fine and coarse terrain meet. The initial material bake uses landscape diffuse layers, vertex colours and terrain slope normals.", true))),
        Card("TERRAIN SHADOWS", Stack(12,
            Check("Export Community / Bottled Shaders heightmaps", nameof(DesktopSettings.TerrainHeightmaps)),
            Number("Maximum heightmap dimension", nameof(DesktopSettings.TerrainHeightmapSize), 128, 16384, 128),
            Text("Heightmaps require complete rectangular coverage. Keep the generated DDS files loose so Terrain Shadows can discover them.", true))),
        Row(Primary("Generate terrain", RunTerrain), Button("Open terrain output", () => Open(RequestFactory.RequiredPath(_settings.TerrainOutput, "a terrain output folder", _settings)))),
        Card("RESULTS", Stack(12, _terrainSummary, _terrainResults))));

    internal Task RunTerrain() => RunPersistentPipeline("Terrain generation", _settings.Snapshot(), s => new GenerationPipelineRequest(
        Resume: s.ResumeCompletedStages, VerifySourceContents: s.VerifySourceContents, Terrain: RequestFactory.Terrain(s)));
}
