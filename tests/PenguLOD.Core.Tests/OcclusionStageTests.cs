using PenguLOD.Core.Occlusion;
using PenguLOD.Core.World;
using PenguLOD.Plugins;
using PenguLOD.Testing;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace PenguLOD.Core.Tests;

public class OcclusionStageTests
{
    private static Worldspace World(SkyrimMod mod, FormKey key, out WorldspaceSubBlock sub)
    {
        var world = new Worldspace(key, SkyrimRelease.SkyrimSE) { EditorID = "FixtureWorld" };
        var block = new WorldspaceBlock { BlockNumberX = 0, BlockNumberY = 0, GroupType = (GroupTypeEnum)4 };
        sub = new WorldspaceSubBlock { BlockNumberX = 0, BlockNumberY = 0, GroupType = (GroupTypeEnum)5 };
        block.Items.Add(sub); world.SubCells.Add(block); mod.Worldspaces.Add(world);
        return world;
    }

    [Fact]
    public void AnEarlierOcclusionPluginIsNotTheSourceOfCellData()
    {
        using var f = new TextureFixture();
        // Skyrim.esm: 8x8 hilly cells. ModA.esp edits one cell. An older Occlusion.esp, loaded last, still carries the pre-edit cell.
        var master = new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE);
        var world = World(master, master.GetNextFormKey(), out var sub);
        world.LandDefaults = new WorldspaceLandDefaults { DefaultLandHeight = -2048, DefaultWaterHeight = -10000 };
        Cell? edited = null;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                var heights = new Array2d<sbyte>(new P2Int(33, 33), 0);
                for (int i = 1; i < 33; i++) heights[i, 0] = 4;
                var cell = new Cell(master.GetNextFormKey(), SkyrimRelease.SkyrimSE) { Grid = new CellGrid { Point = new P2Int(x, y) }, Name = "Original",
                    Landscape = new Landscape(master.GetNextFormKey(), SkyrimRelease.SkyrimSE) { VertexHeightMap = new LandscapeVertexHeightMap { Offset = 0, HeightMap = heights } } };
                sub.Items.Add(cell);
                if (x == 3 && y == 3) edited = cell;
            }
        master.WriteToBinary(Path.Combine(f.Data, "Skyrim.esm"));
        foreach (var (name, label) in new[] { ("ModA.esp", "Edited"), (OcclusionStage.FileName, "Stale") })
        {
            var mod = new SkyrimMod(ModKey.FromNameAndExtension(name), SkyrimRelease.SkyrimSE);
            World(mod, world.FormKey, out var overrides);
            overrides.Items.Add(new Cell(edited!.FormKey, SkyrimRelease.SkyrimSE) { Grid = new CellGrid { Point = new P2Int(3, 3) }, Name = label });
            mod.WriteToBinary(Path.Combine(f.Data, name));
        }
        File.WriteAllText(f.Plugins, "*ModA.esp\n*" + OcclusionStage.FileName + "\n");

        // Disposed before the fixture: Windows cannot delete plugins that are still open.
        using var game = GameContext.Open(f.Game);
        var terrain = TerrainReader.Read(game, ["FixtureWorld"]);
        var grids = new Dictionary<string, LodGrid> { { "FixtureWorld", new("FixtureWorld", new(-64, -64, 128, 4, 32)) } };
        string folder = Path.Combine(f.Root, "occlusion");
        var result = OcclusionStage.Run(game, terrain.Heights, grids, [], null, game.BuildAssets(), folder, new OcclusionSettings(), null, _ => { }, default);
        Assert.NotNull(result.Path);
        using var written = SkyrimMod.CreateFromBinaryOverlay(result.Path!, SkyrimRelease.SkyrimSE);
        var cells = written.Worldspaces.SelectMany(w => w.SubCells).SelectMany(b => b.Items).SelectMany(s => s.Items).ToList();
        Assert.Equal("Edited", Assert.Single(cells, c => c.FormKey == edited!.FormKey).Name?.String);
    }
}
