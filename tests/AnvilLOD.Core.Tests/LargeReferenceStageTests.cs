using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AnvilLOD.Core.Tests;

public class LargeReferenceStageTests
{
    [Theory]
    [InlineData("AnvilLOD.esm", 1)]
    [InlineData("PenguLOD.esm", 1)]
    [InlineData("ExistingLargeReferences.esm", 0)]
    public void ReplacementRebuildsItsOwnListsButHonorsOtherMasters(string installedName, int expected)
    {
        using var f = Fixture();
        string firstOutput = Path.Combine(f.Root, "first");
        LargeReferenceStage.Result first;
        using (var game = GameContext.Open(f.Game))
            first = LargeReferenceStage.Run(game, ["FixtureWorld"], firstOutput, null, Assert.Fail, default);
        Assert.Equal(1, first.Listed);
        Assert.NotNull(first.Plugin);
        File.Copy(first.Plugin, Path.Combine(f.Textures.Data, installedName));
        File.AppendAllText(f.Textures.Plugins, $"\n*{installedName}\n");

        string replacement = Path.Combine(f.Root, "replacement");
        using (var game = GameContext.Open(f.Game))
        {
            var result = LargeReferenceStage.Run(game, ["FixtureWorld"], replacement, null, Assert.Fail, default);
            Assert.Equal(expected, result.Listed);
            if (expected == 0) { Assert.Null(result.Plugin); return; }
            using var written = SkyrimMod.CreateFromBinaryOverlay(result.Plugin!, SkyrimRelease.SkyrimSE);
            Assert.Equal(1, written.Worldspaces.Sum(w => w.LargeReferences.Sum(c => c.References.Count)));
            Assert.DoesNotContain(written.ModHeader.MasterReferences, m => m.Master.FileName.String == installedName);
            Assert.All(written.Worldspaces.SelectMany(w => w.LargeReferences).SelectMany(c => c.References),
                r => Assert.Equal(ModKey.FromNameAndExtension("Skyrim.esm"), r.Reference.FormKey.ModKey));
        }
    }

    [Fact]
    public void ReplacementDoesNotRestoreReferencesDisabledBySourcePatch()
    {
        using var f = Fixture();
        using (var game = GameContext.Open(f.Game))
        {
            var result = LargeReferenceStage.Run(game, ["FixtureWorld"], f.Textures.Data, null, Assert.Fail, default);
            Assert.Equal(1, result.Listed);
            var source = game.LoadOrder.ListedOrder.Single(l => l.ModKey.FileName.String == "Skyrim.esm").Mod!;
            var patch = new SkyrimMod(ModKey.FromNameAndExtension("DisableWall.esp"), SkyrimRelease.SkyrimSE);
            var world = source.Worldspaces.Single().DeepCopy();
            world.SubCells[0].Items[0].Items[0].Temporary.OfType<PlacedObject>().Single().MajorRecordFlagsRaw |= 0x800;
            patch.Worldspaces.Add(world);
            patch.WriteToBinary(Path.Combine(f.Textures.Data, "DisableWall.esp"));
        }
        File.AppendAllText(f.Textures.Plugins, "\n*AnvilLOD.esm\n*DisableWall.esp\n");
        using var updated = GameContext.Open(f.Game);
        var replacement = LargeReferenceStage.Run(updated, ["FixtureWorld"], f.Output, null, Assert.Fail, default);
        Assert.Equal(0, replacement.Listed);
        Assert.Null(replacement.Plugin);
        Assert.False(File.Exists(Path.Combine(f.Output, LargeReferenceStage.FileName)));
    }

    private static TerrainFixture Fixture() => new(editMod: mod =>
    {
        mod.IsMaster = true;
        var wall = mod.Statics.AddNew();
        wall.ObjectBounds = new ObjectBounds { First = new P3Int16(-1000, -1000, 0), Second = new P3Int16(1000, 1000, 2000) };
        var placed = new PlacedObject(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE)
        { Placement = new Placement { Position = new P3Float(-14000, -14000, 0) } };
        placed.Base.SetTo(wall);
        mod.Worldspaces.Single().SubCells[0].Items[0].Items[0].Temporary.Add(placed);
    });
}
