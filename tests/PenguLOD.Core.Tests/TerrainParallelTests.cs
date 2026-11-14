using PenguLOD.Core.Pipeline;
using PenguLOD.Core.World;
using PenguLOD.Plugins;
using PenguLOD.Testing;

namespace PenguLOD.Core.Tests;

/// <summary>Terrain tiles built on several workers at once.</summary>
public sealed class TerrainParallelTests
{
    /// <summary>A worldspace whose grid has a tile at each of the four levels, most of it without landscape records.</summary>
    private static TerrainFixture FourLevels(bool existingTerrain = false)
    {
        var f = new TerrainFixture();
        TextureFixture.Put(f.Textures.Data, @"lodsettings\FixtureWorld.lod", new LodSettings(-32, -32, 32, 4, 32).ToBytes());
        // The default texture covers land the plugin lacks. Its normal map is absent, which every run must report once.
        TextureFixture.Put(f.Textures.Data, @"textures\landscape\dirt02.dds", TextureFixture.Solid(90, 70, 50));
        if (existingTerrain)
        {
            // An existing LOD8 tile supplies the land and texture the plugin lacks around the four cells of LAND.
            TextureFixture.Put(f.Textures.Data, @"meshes\terrain\FixtureWorld\FixtureWorld.8.-8.-8.btr",
                PenguLOD.Meshes.TerrainTileBuilder.Build(new("FixtureWorld", LodLevel.Lod8, -8, -8), (x, y) => (x + y) / 64, (_, _) => null).Bytes);
            TextureFixture.Put(f.Textures.Data, @"textures\terrain\FixtureWorld\FixtureWorld.8.-8.-8.dds", TextureFixture.Solid(40, 50, 60));
        }
        return f;
    }

    private static TerrainGenerationResult Run(TerrainFixture f, int workers, string output)
    {
        using var limits = RunLimits.Enter(workers);
        return TerrainGeneration.Run(f.Request with { OutputFolder = Path.Combine(f.Root, output), Levels = null });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryWorkerCountWritesTheSameFilesInTheSameOrder(bool existingTerrain)
    {
        using var f = FourLevels(existingTerrain);
        var one = Run(f, 1, "one"); var many = Run(f, 8, "many");
        Assert.True(one.Tiles.Count >= 4, $"{one.Tiles.Count} tiles");
        Assert.Equal(one.Tiles, many.Tiles);
        Assert.Equal(one.Worlds.Select(w => w with { Heightmap = null }), many.Worlds.Select(w => w with { Heightmap = null }));
        Assert.Equal(one.Warnings, many.Warnings);
        Assert.Equal(one.Materials, many.Materials);
        var expected = one.Output.Files.Where(p => p.Key != TerrainGeneration.ManifestName).OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
        Assert.True(expected.Length >= 4 * 3 + 2, $"{expected.Length} files");
        Assert.Equal(expected, many.Output.Files.Where(p => p.Key != TerrainGeneration.ManifestName).OrderBy(p => p.Key, StringComparer.Ordinal));
        if (existingTerrain) Assert.True(one.Worlds.Single().ExistingTilesUsed > 0);
    }

    [Fact]
    public void TheFirstFailingTileIsReportedAsItWouldBeOnOneWorker()
    {
        using var f = FourLevels();
        // No fill and no existing terrain: the LOD8 tile is the first that reaches land the plugin lacks.
        var request = f.Request with { Levels = null, FillUncoveredLand = false, UseExistingTerrain = false };
        string Failure(int workers)
        {
            using var limits = RunLimits.Enter(workers);
            return Assert.Throws<InvalidDataException>(() => TerrainGeneration.Run(request with { OutputFolder = Path.Combine(f.Root, "out" + workers) })).Message;
        }
        Assert.Equal(Failure(1), Failure(8));
    }

    [Fact]
    public void OrderedLoopThrowsTheEarliestFailureUnwrappedAfterRunningEverythingBeforeIt()
    {
        using var limits = RunLimits.Enter(8);
        var ran = new bool[200];
        var error = Assert.Throws<InvalidDataException>(() => OrderedParallel.For(ran.Length, () => 0, (index, _) =>
        {
            ran[index] = true;
            if (index is 60 or 120 or 180) { Thread.Sleep(index == 60 ? 30 : 0); throw new InvalidDataException("item " + index); }
        }));
        Assert.Equal("item 60", error.Message);
        Assert.All(ran[..60], Assert.True);

        using var cancel = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => OrderedParallel.For(200, () => 0, (index, _) =>
        {
            if (index == 5) cancel.Cancel();
            cancel.Token.ThrowIfCancellationRequested();
        }, cancel.Token));
    }

    [Fact]
    public void WorkersShareDecodedTexturesAndEachTextureIsDecodedOnce()
    {
        using var f = new TextureFixture();
        var names = Enumerable.Range(0, 24).Select(i => $@"textures\landscape\shared{i}.dds").ToArray();
        foreach (string name in names) TextureFixture.Put(f.Data, name, TextureFixture.Solid((byte)(name.Length * 7), 90, 30));
        var assets = AssetIndex.Build(Mutagen.Bethesda.GameRelease.SkyrimSE, f.Data);
        var materials = new TerrainMaterials(assets, new(), default);
        var expected = names.Select(n => new TerrainMaterials(assets, new(), default).Read(n, .3f, .7f, 1 / 64f, color: true)).ToArray();
        Parallel.For(0, 16, _ =>
        {
            var view = materials.Fork();
            for (int pass = 0; pass < 3; pass++)
            {
                view.Reset();
                for (int i = 0; i < names.Length; i++) Assert.Equal(expected[i], view.Read(names[i], .3f, .7f, 1 / 64f, color: true));
            }
        });
        Assert.Equal(names.Length, materials.Loads);
    }
}
