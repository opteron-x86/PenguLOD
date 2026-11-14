using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using PenguLOD.Core.World;
using PenguLOD.Plugins;
using PenguLOD.Testing;

namespace PenguLOD.Core.Tests;

/// <summary>The list of worldspaces to choose from before generating.</summary>
public sealed class WorldspaceCatalogTests
{
    [Fact]
    public void EveryWorldspaceIsListedWithItsIdNamePluginParentAndLodGrid()
    {
        using var f = new TerrainFixture(editMod: mod =>
        {
            var parent = mod.Worldspaces.Single(); parent.Name = "Fixture Land";
            var city = mod.Worldspaces.AddNew(); city.EditorID = "FixtureCity"; city.Parent = new WorldspaceParent(); city.Parent.Worldspace.SetTo(parent);
            var large = mod.Worldspaces.AddNew(); large.EditorID = "arnimaDUPLICATE003"; large.Name = "The Reach";
            var damaged = mod.Worldspaces.AddNew(); damaged.EditorID = "DamagedWorld";
            mod.Worldspaces.AddNew();   // No editor ID: nothing can select it.
        });
        TextureFixture.Put(f.Textures.Data, @"lodsettings\arnimaDUPLICATE003.lod", new LodSettings(-64, -64, 128, 4, 32).ToBytes());
        TextureFixture.Put(f.Textures.Data, @"lodsettings\DamagedWorld.lod", [1, 2, 3]);

        var worlds = WorldspaceCatalog.List(f.Game);

        Assert.Equal(
        [
            new WorldspaceInfo("arnimaDUPLICATE003", "The Reach", "Skyrim.esm", null, true, 128),
            new WorldspaceInfo("FixtureWorld", "Fixture Land", "Skyrim.esm", null, true, 4),
            new WorldspaceInfo("DamagedWorld", null, "Skyrim.esm", null, true, null),
            new WorldspaceInfo("FixtureCity", null, "Skyrim.esm", "FixtureWorld", false, null),
        ], worlds);
    }
}
