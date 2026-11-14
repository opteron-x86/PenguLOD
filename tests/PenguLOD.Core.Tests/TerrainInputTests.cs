using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using PenguLOD.Core.World;
using PenguLOD.Meshes.Nif;
using PenguLOD.Plugins;
using PenguLOD.Testing;
using PenguLOD.Textures;

namespace PenguLOD.Core.Tests;

/// <summary>Land without records or existing LOD, and landscape textures that are not installed: found before the first tile,
/// then either filled in (the default) or reported together.</summary>
public sealed class TerrainInputTests
{
    private const string Dirt = @"textures\landscape\dirt02.dds";
    private static void BlueDefault(TerrainFixture f) => TextureFixture.Put(f.Textures.Data, Dirt, TextureFixture.Solid(0, 0, 240));
    private static TextureImage Tile(TerrainFixture f) =>
        TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, @"textures\terrain\FixtureWorld\FixtureWorld.4.-4.-4.dds")));
    private static IEnumerable<Cell> Cells(SkyrimMod mod) => mod.Worldspaces.Single().SubCells.SelectMany(b => b.Items).SelectMany(b => b.Items);
    private sealed class Lines : IProgress<string> { public List<string> Seen { get; } = []; public void Report(string value) => Seen.Add(value); }

    [Fact]
    public void UncoveredLandIsFilledAtTheDefaultHeightWithTheDefaultTextureAndWater()
    {
        using var f = new TerrainFixture(missingCell: true); BlueDefault(f);
        var result = TerrainGeneration.Run(f.Request);
        Assert.Contains(result.Warnings, w => w.Contains("FixtureWorld: 1 cell in 1 tile have neither landscape records nor existing terrain LOD")
            && w.Contains("default land height (-2048), first cell -1,-1"));
        Assert.Equal(0, result.Worlds[0].ExistingTilesUsed);
        // The fixture's default water (64) lies above its default land (-2048), so the filled cell is under water like the other 15.
        Assert.Equal(16, result.Tiles[0].WaterCells);
        var mesh = NifGeometryReader.Read("tile.btr", File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, @"meshes\terrain\FixtureWorld\FixtureWorld.4.-4.-4.btr")), passthru: true, includeUnshaded: true);
        var land = Assert.Single(mesh.Parts, p => p.Name == "land");
        // The north-east corner lies in cell (-1,-1), which has no record; the south-west corner is real land at 0.
        Assert.Equal(-2048, land.Positions[129 * 129 - 1].Z, 3); Assert.Equal(0, land.Positions[0].Z, 3);
        var tile = Tile(f);
        Assert.InRange(tile.Pixels[(0 * 64 + 63) * 4 + 2], 225, 245); Assert.InRange(tile.Pixels[(0 * 64 + 63) * 4], 0, 20);   // north-east: default texture
        Assert.InRange(tile.Pixels[(63 * 64 + 0) * 4], 230, 245);                                                              // south-west: painted land
    }

    [Fact]
    public void UncoveredLandStopsBeforeAnyTileWhenTheFillIsOff()
    {
        using var f = new TerrainFixture(missingCell: true); BlueDefault(f);
        var lines = new Lines();
        var error = Assert.Throws<InvalidDataException>(() => TerrainGeneration.Run(f.Request with { FillUncoveredLand = false }, lines));
        Assert.Contains("FixtureWorld: 1 cell in 1 tile have neither landscape records nor existing terrain LOD (first cell -1,-1)", error.Message);
        Assert.Contains(TerrainGeneration.FillUncoveredLandOption, error.Message);
        Assert.DoesNotContain(lines.Seen, l => l.StartsWith("Terrain FixtureWorld:"));
        Assert.False(Directory.Exists(f.Output));
    }

    [Fact]
    public void ExistingTerrainLodStillCoversMissingLandBeforeTheDefaultHeightIsUsed()
    {
        using var f = new TerrainFixture(missingCell: true); BlueDefault(f);
        TextureFixture.Put(f.Textures.Data, @"meshes\terrain\FixtureWorld\FixtureWorld.4.-4.-4.btr",
            PenguLOD.Meshes.TerrainTileBuilder.Build(new("FixtureWorld", LodLevel.Lod4, -4, -4), (_, _) => 300, (_, _) => null).Bytes);
        TextureFixture.Put(f.Textures.Data, @"textures\terrain\FixtureWorld\FixtureWorld.4.-4.-4.dds", TextureFixture.Solid(8, 235, 10));
        var result = TerrainGeneration.Run(f.Request with { FillUncoveredLand = false });
        Assert.Equal(1, result.Worlds[0].ExistingTilesUsed);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("neither landscape records"));
        Assert.InRange(Tile(f).Pixels[(0 * 64 + 63) * 4 + 1], 225, 245);
    }

    [Fact]
    public void EveryMissingLandscapeTextureIsListedBeforeAnyTile()
    {
        using var f = new TerrainFixture(editMod: mod =>
        {
            var gone = mod.TextureSets.AddNew(); gone.Diffuse = @"landscape\gone.dds";
            var ltex = mod.LandscapeTextures.AddNew(); ltex.EditorID = "GoneLand"; ltex.TextureSet.SetTo(gone);
            var cell = Cells(mod).Single(c => c.Grid!.Point.X == -2 && c.Grid.Point.Y == -3);
            cell.Landscape!.Layers[0].Header!.Texture.SetTo(ltex);
        });
        BlueDefault(f); File.Delete(GamePath.ToFileSystemPath(f.Textures.Data, @"textures\red.dds"));
        var lines = new Lines();
        var error = Assert.Throws<InvalidDataException>(() => TerrainGeneration.Run(f.Request with { SubstituteMissingTextures = false }, lines));
        Assert.Contains(@"landscape texture textures\red.dds is missing and 16 cells need it (first cell -4,-4)", error.Message);
        Assert.Contains(@"landscape texture textures\landscape\gone.dds is missing and 1 cell need it (first cell -2,-3)", error.Message);
        Assert.Contains(TerrainGeneration.SubstituteMissingTexturesOption, error.Message);
        Assert.DoesNotContain(lines.Seen, l => l.StartsWith("Terrain FixtureWorld:"));
        Assert.False(Directory.Exists(f.Output));
    }

    [Fact]
    public void MissingAndUnresolvedLandscapeTexturesUseTheDefaultTexture()
    {
        using var f = new TerrainFixture(editMod: mod =>
        {
            // A layer whose landscape texture record is not in the load order, in the north-east cell.
            var cell = Cells(mod).Single(c => c.Grid!.Point.X == -1 && c.Grid.Point.Y == -1);
            foreach (var layer in cell.Landscape!.Layers) layer.Header!.Texture.SetTo(new FormKey(mod.ModKey, 0xABCDE));
            // A texture set without a diffuse map, in the south-west cell.
            var empty = mod.TextureSets.AddNew(); var ltex = mod.LandscapeTextures.AddNew(); ltex.TextureSet.SetTo(empty);
            foreach (var layer in Cells(mod).Single(c => c.Grid!.Point.X == -4 && c.Grid.Point.Y == -4).Landscape!.Layers) layer.Header!.Texture.SetTo(ltex);
        });
        BlueDefault(f); File.Delete(GamePath.ToFileSystemPath(f.Textures.Data, @"textures\red.dds"));
        var result = TerrainGeneration.Run(f.Request);
        Assert.Contains(result.Warnings, w => w.Contains(@"landscape texture textures\red.dds is missing; 14 cells use the default landscape texture instead"));
        Assert.Equal(2, result.Warnings.Count(w => w.Contains("cannot be resolved; 1 cell use the default landscape texture instead")));
        var tile = Tile(f);
        foreach (int pixel in new[] { 0 * 64 + 63, 63 * 64 + 0, 32 * 64 + 32 })
        { Assert.InRange(tile.Pixels[pixel * 4 + 2], 225, 245); Assert.InRange(tile.Pixels[pixel * 4], 0, 20); }

        var error = Assert.Throws<InvalidDataException>(() => TerrainGeneration.Run(f.Request with { SubstituteMissingTextures = false }));
        Assert.Equal(2, error.Message.Split('\n').Count(l => l.Contains("cannot be resolved and 1 cell need it")));
    }

    [Fact]
    public void AnInstalledTextureIsNotReplacedAndPbrTwinsCount()
    {
        using var f = new TerrainFixture(); BlueDefault(f);
        Assert.DoesNotContain(TerrainGeneration.Run(f.Request).Warnings, w => w.Contains("landscape texture"));
        Assert.InRange(Tile(f).Pixels[(32 * 64 + 32) * 4], 230, 245);
        // With twins enabled, the PBR copy is what gets read, so its presence is enough.
        File.Move(GamePath.ToFileSystemPath(f.Textures.Data, @"textures\red.dds"), Path.Combine(Directory.CreateDirectory(Path.Combine(f.Textures.Data, "textures", "pbr")).FullName, "red.dds"));
        var twins = f.Request with { Materials = new(PbrTwins: true, PbrAlbedo: false), SubstituteMissingTextures = false };
        Assert.DoesNotContain(TerrainGeneration.Run(twins).Warnings, w => w.Contains("is missing;"));
        Assert.Throws<InvalidDataException>(() => TerrainGeneration.Run(f.Request with { SubstituteMissingTextures = false }));
    }

    [Fact]
    public void AMissingDefaultTextureIsReportedBecauseNothingCanStandInForIt()
    {
        using var f = new TerrainFixture(missingCell: true);
        var error = Assert.Throws<InvalidDataException>(() => TerrainGeneration.Run(f.Request));
        Assert.Contains($"The default landscape texture {Dirt} is missing", error.Message);
        Assert.False(Directory.Exists(f.Output));
    }

    [Fact]
    public void SeasonalLandscapeTexturesAreCheckedWithTheirSeason()
    {
        using var f = new TerrainFixture(seasons: true); BlueDefault(f);
        File.Delete(GamePath.ToFileSystemPath(f.Textures.Data, @"textures\green.dds"));
        var error = Assert.Throws<InvalidDataException>(() => TerrainGeneration.Run(f.Request with { Seasons = true, SubstituteMissingTextures = false }));
        Assert.Contains(@"FixtureWorld (WIN): landscape texture textures\green.dds is missing and 16 cells need it", error.Message);
        Assert.DoesNotContain(@"textures\red.dds", error.Message);
    }

    [Fact]
    public void PreflightReportsTerrainGapsBeforeAnyStageRuns()
    {
        using var f = new TerrainFixture(missingCell: true); BlueDefault(f);
        File.Delete(GamePath.ToFileSystemPath(f.Textures.Data, @"textures\red.dds"));
        var filled = PipelineDiagnostics.Run(new(Terrain: f.Request));
        Assert.True(filled.CanGenerate);
        Assert.Contains(filled.Issues, i => i is { Severity: "Warning", Code: TerrainInputs.UncoveredLand });
        Assert.Contains(filled.Issues, i => i is { Severity: "Warning", Code: TerrainInputs.MissingTexture } && i.Message.Contains(@"textures\red.dds"));

        var strict = PipelineDiagnostics.Run(new(Terrain: f.Request with { FillUncoveredLand = false, SubstituteMissingTextures = false }));
        Assert.False(strict.CanGenerate);
        Assert.Contains(strict.Issues, i => i is { Severity: "Error", Code: TerrainInputs.UncoveredLand } && i.Action.Contains(TerrainGeneration.FillUncoveredLandOption));
        Assert.Contains(strict.Issues, i => i is { Severity: "Error", Code: TerrainInputs.MissingTexture } && i.Action.Contains(TerrainGeneration.SubstituteMissingTexturesOption));

        var pipeline = GenerationPipeline.Run(new(Terrain: f.Request with { FillUncoveredLand = false }), Path.Combine(f.Root, "cache"));
        Assert.False(pipeline.Success); Assert.Equal("preflight", pipeline.FailedStage);
        Assert.Contains("neither landscape records nor existing terrain LOD", pipeline.Error);
    }
}
