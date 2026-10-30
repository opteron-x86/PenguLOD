using AnvilLOD.Core.Lod;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Testing;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AnvilLOD.Core.Tests;

public class GeneratedLodInputTests
{
    [Theory]
    [InlineData("AnvilLOD.ESP", true)]
    [InlineData("PenguLOD.esm", true)]
    [InlineData("AnvilLOD Effects.esp", true)]
    [InlineData("AnvilLOD Effects 3.esp", true)]
    [InlineData("AnvilLOD Underside.esm", true)]
    [InlineData("AnvilLOD Effects Patch.esp", false)]
    [InlineData("AnvilLOD Resources.esp", false)]
    [InlineData("Skyrim.esm", false)]
    public void OnlyGeneratedPluginIdentitiesAreExcluded(string plugin, bool excluded)
        => Assert.Equal(excluded, GeneratedLodInputs.IsPlugin(plugin));

    [Theory]
    [InlineData(@"Meshes/Terrain/Tamriel/Tamriel_Underside.nif")]
    [InlineData(@"terrain\OtherWorld\OtherWorld_underside.nif")]
    [InlineData(@"meshes\pengulod\generated\objects\ab\model.nif")]
    public void AutomaticAuthoringCannotReprocessGeneratedGeometry(string path)
    {
        using var f = new TextureFixture();
        var source = new AutomaticObjectLod(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), new(true), Path.Combine(f.Root, "cache"));
        Assert.Null(source.Resolve(path, 0));
        Assert.Contains("not a source object", Assert.Single(source.Report).Reason);
        Assert.False(Directory.Exists(Path.Combine(f.Root, "cache")));
    }

    [Fact]
    public void PreviousUndersidePluginIsExcludedWhileOriginalObjectsStillGenerate()
    {
        using var f = new TerrainFixture(editMod: mod =>
        {
            var wall = mod.Statics.AddNew(); wall.Model = new Model { File = @"fixture\wall.nif" };
            var placed = new PlacedObject(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE)
            { Placement = new Placement { Position = new P3Float(-9500, -9500, 0) } };
            placed.Base.SetTo(wall); mod.Worldspaces.Single().SubCells[0].Items[0].Items[0].Temporary.Add(placed);
        });
        byte[] model = LodNifWriter.Write("fixture", [TextureFixture.Card("wall", @"textures\red.dds")]);
        TextureFixture.Put(f.Textures.Data, @"meshes\fixture\wall.nif", model);
        TextureFixture.Put(f.Textures.Data, UndersideBuilder.RelativePath("FixtureWorld"), model);
        using (var game = GameContext.Open(f.Game)) UndersidePluginWriter.Write(game, ["FixtureWorld"], f.Textures.Data);
        File.AppendAllText(f.Textures.Plugins, "\n*AnvilLOD.esp\n");
        using var limits = AnvilLOD.Core.Pipeline.RunLimits.Enter(2, cacheDirectory: Path.Combine(f.Root, "cache"));
        var scan = ScanPipeline.Run(new(f.Game, new(TreeLod: false, DynamicLod: false, GridObjects: false), Generate: false,
            GrassLod: false, ChildWorlds: false, RemoveBuried: false, RemoveUnderwater: false, AutomaticObjects: new(true)));
        Assert.True(scan.Stats.SkippedGenerated > 0);
        Assert.True(scan.Stats.LodReferencesFound > 0);
        Assert.DoesNotContain(scan.AutomaticObjects!, r => r.Model.Contains("underside", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scan.AutomaticObjects!, r => r.Model.EndsWith(@"fixture\wall.nif"));
    }

    [Fact]
    public void InstalledOutputsAreDiagnosedEvenWhenGenerationUsesAnotherDirectory()
    {
        var issues = Preflight.Check(["Renamed installed output"], ["Skyrim.esm", "AnvilLOD.esp"], Path.GetTempPath(), null);
        Assert.Contains(issues, i => i.Code == Preflight.OutputModEnabled && i.Message.Contains("outside"));
    }
}
