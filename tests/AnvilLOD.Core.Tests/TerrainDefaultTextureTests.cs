using AnvilLOD.Core.World;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AnvilLOD.Core.Tests;

public class TerrainDefaultTextureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnpaintedLandUsesSkyrimDirtTexture(bool nullBase)
    {
        using var f = new TerrainFixture(editLandscape: land =>
        {
            land.Layers.Clear();
            if (nullBase) for (int q = 0; q < 4; q++)
                land.Layers.Add(new BaseLayer { Header = new LayerHeader { Quadrant = (Quadrant)q } });
        });
        TextureFixture.Put(f.Textures.Data, @"textures\landscape\dirt02.dds", TextureFixture.Solid(8, 12, 235));
        var result = TerrainGeneration.Run(f.Request with { Game = f.Game with { GameIniPath = EmptyIni(f.Root) } });
        Assert.Single(result.Tiles);
        var image = Diffuse(f);
        Assert.InRange(image.Pixels[2], 225, 245);
        Assert.InRange(image.Pixels[0], 0, 20);
    }

    [Fact]
    public void FullyOpaquePaintDoesNotReadAnUnusedDefaultBase()
    {
        using var f = new TerrainFixture(editLandscape: land =>
        {
            var texture = land.Layers[0].Header!.Texture.FormKey;
            land.Layers.Clear();
            for (int q = 0; q < 4; q++)
            {
                var layer = new AlphaLayer { Header = new LayerHeader { Quadrant = (Quadrant)q }, AlphaLayerData = [] };
                layer.Header.Texture.SetTo(texture);
                for (ushort p = 0; p < 17 * 17; p++) layer.AlphaLayerData.Add(new AlphaLayerData { Position = p, Opacity = 1 });
                land.Layers.Add(layer);
            }
        });
        TerrainGeneration.Run(f.Request);
        Assert.InRange(Diffuse(f).Pixels[0], 230, 245);
    }

    [Theory]
    [InlineData("Custom.dds")]
    [InlineData("Landscape/Custom.dds")]
    [InlineData("Textures/Landscape/Custom.dds")]
    public void CustomIniDefaultIsUsedInTheGeneratedTexture(string path)
    {
        using var f = new TerrainFixture(editLandscape: land => land.Layers.Clear());
        File.WriteAllText(Path.Combine(f.Root, "Skyrim.ini"), "[Landscape]\nsDefaultLandDiffuseTexture=unused.dds\n");
        // Case-insensitive lookup and section/key matching must work on Linux too.
        File.WriteAllText(Path.Combine(f.Root, "sKYRIMcUSTOM.INI"), $"[LANDSCAPE]\nSDEFAULTLANDDIFFUSETEXTURE=\"{path}\"\n");
        TextureFixture.Put(f.Textures.Data, @"textures\landscape\custom.dds", TextureFixture.Solid(8, 235, 10));
        TerrainGeneration.Run(f.Request);
        Assert.InRange(Diffuse(f).Pixels[1], 225, 245);
    }

    [Fact]
    public void ProfileLocalAndExplicitIniOverridesHaveTheSamePriorityAsArchiveSettings()
    {
        using var f = new TextureFixture();
        var instance = Path.Combine(f.Root, "MO2"); var profile = Path.Combine(instance, "profiles", "Test");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(instance, "ModOrganizer.ini"), $"[General]\ngamePath={Path.GetDirectoryName(f.Data)}\nselected_profile=Test\n");
        File.WriteAllText(Path.Combine(profile, "modlist.txt"), "");
        File.WriteAllText(Path.Combine(profile, "settings.ini"), "[General]\nLocalSettings=true\n");
        File.WriteAllText(Path.Combine(profile, "Skyrim.ini"), "[Landscape]\nsDefaultLandDiffuseTexture=base.dds\n");
        File.WriteAllText(Path.Combine(profile, "SkyrimCustom.ini"), "[Landscape]\nsDefaultLandDiffuseTexture=parallax/custom.dds\n");
        var game = f.Game with { Mode = GameSourceMode.Mo2Instance, Mo2InstanceFolder = instance, Mo2Profile = "Test" };
        Assert.Equal(@"textures\landscape\parallax\custom.dds", TerrainDefaultTexture.Resolve(game, f.Data));
        string selected = Path.Combine(f.Root, "selected.ini");
        File.WriteAllText(selected, "[Landscape]\nsDefaultLandDiffuseTexture=chosen.dds\n");
        Assert.Equal(@"textures\landscape\chosen.dds", TerrainDefaultTexture.Resolve(game with { GameIniPath = selected }, f.Data));
        File.WriteAllText(Path.Combine(profile, "settings.ini"), "[General]\nLocalSettings=false\n");
        Assert.DoesNotContain(AnvilLOD.Plugins.Mo2.Mo2Instance.Open(instance).OpenProfile("Test").IniPaths(), p => Path.GetDirectoryName(p) == profile);
        Assert.Equal(@"textures\landscape\dirt02.dds", TerrainDefaultTexture.Read([]));
    }

    [Fact]
    public void ARealMissingPaintedTextureStillFailsWithItsWorldAndCell()
    {
        using var f = new TerrainFixture();
        File.Delete(GamePath.ToFileSystemPath(f.Textures.Data, @"textures\red.dds"));
        TextureFixture.Put(f.Textures.Data, @"textures\landscape\dirt02.dds", TextureFixture.Solid(0, 0, 240));
        var error = Assert.Throws<FileNotFoundException>(() => TerrainGeneration.Run(f.Request));
        Assert.Equal(@"textures\red.dds", error.FileName);
        Assert.Contains("world FixtureWorld, cell", error.Message);
        Assert.False(Directory.Exists(f.Output));
    }

    private static string EmptyIni(string root)
    {
        var path = Path.Combine(root, "explicit.ini");
        File.WriteAllText(path, "[Landscape]\nsDefaultLandDiffuseTexture=Dirt02.dds\n");
        return path;
    }

    private static TextureImage Diffuse(TerrainFixture f) => TextureImages.Decode(File.ReadAllBytes(
        GamePath.ToFileSystemPath(f.Output, @"textures\terrain\FixtureWorld\FixtureWorld.4.-4.-4.dds")));
}
