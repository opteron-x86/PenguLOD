using AnvilLOD.Core.Lod;
using AnvilLOD.Core.Textures;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda;

namespace AnvilLOD.Core.Tests;

public class RecipeTextureFallbackTests
{
    private const string Red = @"textures\red.dds", Green = @"textures\green.dds", Blue = @"textures\blue.dds";
    private const string Atlas = @"textures\lod\atlas.dds", Normal = @"textures\lod\atlas_n.dds";
    private const string Model = @"meshes\fixture\wall_lod_0.nif", Missing = @"textures\missing.dds";

    private static TextureRecipeSet Rules(string kind = "tile")
    {
        var rules = new TextureRecipeSet { BaseSize = 64 };
        if (kind == "copy") rules.Entries.Add(new("fixture", 1, Copy: new(Red, Atlas)));
        else if (kind == "conversion") rules.Entries.Add(new("fixture", 1, Conversion: new(Red, Atlas, 0, 0, 0, 0, 0, 1, 1, 1, false)));
        else
        {
            string destination = kind == "nested" ? @"dyndolod-temp\textures\inner.dds" : Atlas;
            rules.Entries.Add(new("fixture", 1, Tile: TextureRecipe.ParseTile(TextureFixture.Tile(Red, destination, 0), false)));
            rules.Entries.Add(new("fixture", 2, Tile: TextureRecipe.ParseTile(TextureFixture.Tile(Green, destination, 1), false)));
            if (kind == "nested") rules.Entries.Add(new("fixture", 3, Copy: new(destination, Atlas)));
        }
        return rules;
    }

    private static LodMeshPart Supplied(TextureFixture f)
    {
        TextureFixture.Put(f.Data, Atlas, TextureFixture.Solid(240, 12, 6));
        TextureFixture.Put(f.Data, Normal, TextureFixture.Solid(128, 128, 255));
        var part = TextureFixture.Card("wall", Atlas);
        return RecipeTextureMapper.Copy(part, part.Material with { Textures = [Atlas, Normal] }, part.UVs);
    }

    [Theory]
    [InlineData("tile", false)] [InlineData("tile", true)]
    [InlineData("copy", false)] [InlineData("copy", true)]
    [InlineData("conversion", false)] [InlineData("conversion", true)]
    [InlineData("nested", false)] [InlineData("nested", true)]
    public void MissingReplacementRetainsCompleteMaterialAndUvsWithOneWarning(string kind, bool normal)
    {
        using var f = new TextureFixture();
        var part = Supplied(f);
        var warnings = new List<string>();
        string output = Path.Combine(f.Root, "lod");
        var mapper = new RecipeTextureMapper(Rules(kind), AssetIndex.Build(GameRelease.SkyrimSE, f.Data), output, warn: warnings.Add);
        TextureMatchInput[] inputs = [new("wall", Red, null, normal ? Red : Missing, normal ? Missing : null, true)];
        Assert.Same(part, mapper.Map(Model, part, inputs));
        Assert.Same(part, mapper.Map(Model, part, inputs));
        var warning = Assert.Single(warnings);
        Assert.Contains(Model, warning); Assert.Contains("wall", warning); Assert.Contains(Missing, warning);
        Assert.Contains(normal ? "normal texture missing" : "diffuse texture missing", warning);
        var row = Assert.Single(mapper.Report);
        Assert.Equal("Fallback", row.Status); Assert.Equal(Model, row.Model); Assert.Equal(Atlas, row.Destination);
        mapper.ThrowIfFailed();
        Assert.Empty(Directory.GetDirectories(output, ".recipe-build-*"));
        Assert.Empty(Directory.GetFiles(output, "*.dds", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingSuppliedTextureCannotBeUsedAsFallback(bool normal)
    {
        using var f = new TextureFixture();
        var part = Supplied(f);
        File.Delete(GamePath.ToFileSystemPath(f.Data, normal ? Normal : Atlas));
        var warnings = new List<string>();
        var mapper = new RecipeTextureMapper(Rules(), AssetIndex.Build(GameRelease.SkyrimSE, f.Data), Path.Combine(f.Root, "lod"), warn: warnings.Add);
        var error = Assert.Throws<InvalidDataException>(() => mapper.Map(Model, part, [new("wall", Red, null, Missing, null, true)]));
        Assert.Contains("cannot retain", error.Message); Assert.Contains(normal ? Normal : Atlas, error.Message);
        Assert.Empty(warnings); Assert.Equal("Failed", Assert.Single(mapper.Report).Status);
        Assert.Throws<InvalidDataException>(mapper.ThrowIfFailed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FallbackIsLocalToTheMaterialAndVariantAndDoesNotDisableOtherMatches(bool normal)
    {
        using var f = new TextureFixture();
        string full = @"meshes\fixture\wall.nif", output = Path.Combine(f.Root, "lod");
        var supplied = Supplied(f);
        var good = TextureFixture.Card("good", Atlas);
        TextureFixture.Put(f.Data, Blue, TextureFixture.Solid(10, 15, 230));
        TextureFixture.Put(f.Data, full, LodNifWriter.Write("full", [TextureFixture.Card("wall", Red), TextureFixture.Card("good", Red)], preserveShapeNames: true));
        TextureFixture.Put(f.Data, Model, LodNifWriter.Write("lod", [supplied, good], preserveShapeNames: true));
        var affected = TextureVariant.Create(full, [new("wall", normal ? Blue : Missing, normal ? Missing : null), new("good", Blue, null)]);
        var valid = TextureVariant.Create(full, [new("wall", Blue, null), new("good", Blue, null)]);
        var assets = AssetIndex.Build(GameRelease.SkyrimSE, f.Data); var warnings = new List<string>();
        var mapper = new RecipeTextureMapper(Rules(), assets, output, warn: warnings.Add);
        var generator = new LodGenerator(assets)
        {
            TextureVariants = new Dictionary<string, TextureVariant> { [affected.Id] = affected, [valid.Id] = valid }, RecipeMapper = mapper.Map
        };
        var mesh = generator.GetMesh(LodMeshKey.Make(Model, affected.Id))!;
        var original = NifGeometryReader.Read(Model, File.ReadAllBytes(GamePath.ToFileSystemPath(f.Data, Model)));
        Assert.Equal(original.Parts[0].Material.Textures, mesh.Parts[0].Material.Textures);
        Assert.Equal(original.Parts[0].UVs, mesh.Parts[0].UVs);
        AssertBlue(mesh.Parts[1]);
        Assert.All(generator.GetMesh(LodMeshKey.Make(Model, valid.Id))!.Parts, AssertBlue);
        Assert.Single(warnings); mapper.ThrowIfFailed();
        void AssertBlue(LodMeshPart part)
        {
            Assert.NotEqual(Atlas, part.Material.Textures[0]);
            var image = TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(output, part.Material.Textures[0])));
            Assert.True(image.Pixels[2] > 220);
        }
    }

    [Fact]
    public void FullTextureFallbackCannotFallThroughToTheOrdinaryMatcher()
    {
        using var f = new TextureFixture();
        string full = @"meshes\fixture\full.nif", output = Path.Combine(f.Root, "lod");
        TextureFixture.Put(f.Data, Blue, TextureFixture.Solid(10, 15, 230));
        TextureFixture.Put(f.Data, full, LodNifWriter.Write("full", [TextureFixture.Card("wall", Red)], preserveShapeNames: true));
        TextureFixture.Put(f.Data, Model, LodNifWriter.Write("lod", [TextureFixture.Card("wall", Red)], preserveShapeNames: true));
        var variant = TextureVariant.Create(full, [new("wall", Blue, Missing)]);
        var assets = AssetIndex.Build(GameRelease.SkyrimSE, f.Data);
        var mapper = new RecipeTextureMapper(Rules(), assets, output);
        var generator = new LodGenerator(assets) { TextureVariants = new Dictionary<string, TextureVariant> { [variant.Id] = variant }, RecipeMapper = mapper.Map };
        var part = Assert.Single(generator.GetMesh(LodMeshKey.Make(Model, variant.Id))!.Parts);
        Assert.Equal(Red, part.Material.Textures[0]);
        Assert.Equal(TextureFixture.Card("wall", Red).UVs, part.UVs);
        Assert.Equal("Fallback", Assert.Single(mapper.Report).Status);
    }

    [Theory]
    [InlineData("corrupt")] [InlineData("cycle")] [InlineData("source")] [InlineData("conflict")]
    public void OtherRecipeFailuresAreNotDowngradedToFallbacks(string failure)
    {
        using var f = new TextureFixture();
        var part = Supplied(f); var rules = Rules(); var warnings = new List<string>();
        TextureFixture.Put(f.Data, Blue, failure == "corrupt" ? [1, 2, 3, 4] : TextureFixture.Solid(10, 15, 230));
        if (failure == "cycle")
            rules.Entries = [new("fixture", 1, Copy: new(@"textures\loop.dds", Atlas)), new("fixture", 2, Copy: new(Atlas, @"textures\loop.dds"))];
        byte[]? Read(string path) => failure == "source" && path == Red ? null
            : File.Exists(GamePath.ToFileSystemPath(f.Data, path)) ? File.ReadAllBytes(GamePath.ToFileSystemPath(f.Data, path)) : null;
        var mapper = new RecipeTextureMapper(rules, AssetIndex.Build(GameRelease.SkyrimSE, f.Data), Path.Combine(f.Root, "lod"), Read, warnings.Add);
        TextureMatchInput[] inputs = failure == "conflict"
            ? [new("wall", Red, null, Blue, null, true), new("wall", Red, null, Green, null, true)]
            : [new("wall", Red, null, failure == "source" ? Red : Blue, failure == "source" ? @"textures\green_n.dds" : null, true)];
        Assert.Throws<InvalidDataException>(() => mapper.Map(Model, part, inputs));
        Assert.Empty(warnings); Assert.Equal("Failed", Assert.Single(mapper.Report).Status);
        Assert.Throws<InvalidDataException>(mapper.ThrowIfFailed);
    }
}
