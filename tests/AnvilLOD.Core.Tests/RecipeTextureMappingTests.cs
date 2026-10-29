using System.Numerics;
using AnvilLOD.Core.Lod;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Textures;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda;

namespace AnvilLOD.Core.Tests;

public class RecipeTextureMappingTests
{
    [Fact]
    public void NormalGlossDoesNotWeightNormalDirectionsDuringReduction()
    {
        var pixels = Enumerable.Range(0, 64).SelectMany(i => i % 2 == 0 ? new byte[] { 255, 128, 128, 0 } : new byte[] { 128, 128, 255, 255 }).ToArray();
        var target = TextureImages.Blank(4, 4, true);
        var tile = new TextureTile("textures\\source.dds", "textures\\out.dds", 1, 1, 0, 0, 1, 1, 0, 1, 1, 1, false);
        TextureImages.Blit(target, new(pixels, 8, 8), tile, 4, true);
        Assert.InRange(target.Pixels[0], 215, 220); Assert.InRange(target.Pixels[2], 215, 220);
        Assert.InRange(target.Pixels[3], 127, 129);
    }
    private const string Red = @"textures\red.dds", Green = @"textures\green.dds", Blue = @"textures\blue.dds", Atlas = @"textures\lod\atlas.dds";
    private static TextureRecipeSet Recipes(params string[] lines) => new() { BaseSize = 64, Entries = lines.Select((line, i) => new TextureRecipeEntry("fixture", i + 1, Tile: TextureRecipe.ParseTile(line, false))).ToList() };
    private static TextureMatchInput Input(string source, string desired, string? normal = null, string shape = "shape") => new(shape, source, null, desired, normal, source != desired || normal is not null);
    private static TextureImage Image(string output, string path) => TextureImages.Decode(File.ReadAllBytes(GamePath.ToFileSystemPath(output, path)));
    private static RecipeTextureMapper Mapper(TextureFixture f, TextureRecipeSet rules) => new(rules, AssetIndex.Build(GameRelease.SkyrimSE, f.Data), Path.Combine(f.Root, "lod"));

    [Fact]
    public void FullTextureUsesAtlasRectangleAndPreservesGeometryAndShader()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        var mapper = Mapper(f, Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Green, Atlas, 1)));
        var part = TextureFixture.Card("shape", Red);
        var mapped = mapper.Map(part, [Input(Red, Red)])!;
        Assert.NotNull(mapped); Assert.Equal(new Vector2(.5f, 1), mapped.UVs[1]);
        Assert.Same(part.Positions, mapped.Positions); Assert.Same(part.Normals, mapped.Normals); Assert.Same(part.Triangles, mapped.Triangles);
        Assert.Equal(part.Material.AlphaFlags, mapped.Material.AlphaFlags);
        var image = Image(Path.Combine(f.Root, "lod"), mapped.Material.Textures[0]);
        Assert.True(image.Pixels[0] > 220); Assert.True(image.Pixels[80 * 4 + 1] > 220);
    }

    [Fact]
    public void NormalOnlyVariantPreservesOtherAtlasTilesAndOriginalUvCoordinates()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        string normal = @"textures\custom_normal.dds";
        TextureFixture.Put(f.Data, normal, TextureFixture.Solid(255, 128, 128));
        var mapper = Mapper(f, Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Green, Atlas, 1)));
        var part = TextureFixture.Card("shape", Atlas);
        var mapped = mapper.Map(part, [Input(Red, Red, normal)])!;
        Assert.Equal(part.UVs, mapped.UVs);
        var image = Image(Path.Combine(f.Root, "lod"), mapped.Material.Textures[0]);
        var normals = Image(Path.Combine(f.Root, "lod"), mapped.Material.Textures[1]);
        Assert.True(image.Pixels[0] > 220); Assert.True(image.Pixels[80 * 4 + 1] > 220);
        Assert.True(normals.Pixels[0] > 245); Assert.True(normals.Pixels[80 * 4 + 2] > 245);
        Assert.Contains(mapper.Report, r => r.Status == "Variant");
    }

    [Fact]
    public void NormalOverridesFlowThroughExplicitNormalCopyRecipes()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        string normal = @"textures\custom_n.dds"; TextureFixture.Put(f.Data, normal, TextureFixture.Solid(255, 128, 128));
        var rules = Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Green, Atlas, 1));
        rules.Entries.Add(new("normal", 3, Copy: new(@"textures\red_n.dds", @"textures\lod\atlas_n.dds")));
        var mapper = Mapper(f, rules);
        var mapped = mapper.Map(TextureFixture.Card("shape", Atlas), [Input(Red, Red, normal)])!;
        var normals = Image(Path.Combine(f.Root, "lod"), mapped.Material.Textures[1]);
        Assert.True(normals.Pixels[0] > 245); Assert.InRange(normals.Pixels[2], 125, 132);
    }

    [Fact]
    public void CopyAndNestedAtlasKeepUvProvenanceAndReplacementNormals()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        TextureFixture.Put(f.Data, Blue, TextureFixture.Solid(10, 20, 240));
        string normal = @"textures\special_n.dds"; TextureFixture.Put(f.Data, normal, TextureFixture.Solid(255, 128, 128));
        string inner = @"dyndolod-temp\textures\inner.dds", copy = @"dyndolod-temp\textures\copy.dds";
        var rules = Recipes(TextureFixture.Tile(Red, inner, 0), TextureFixture.Tile(Green, inner, 1), TextureFixture.Tile(copy, Atlas, 1));
        rules.Entries.Add(new("copy", 1, Copy: new(inner, copy)));
        var mapper = Mapper(f, rules); var part = TextureFixture.Card("shape", Red);
        var mapped = mapper.Map(part, [Input(Red, Blue, normal)])!;
        Assert.Equal(.5f, mapped.UVs[0].X); Assert.Equal(.75f, mapped.UVs[1].X);
        var image = Image(Path.Combine(f.Root, "lod"), mapped.Material.Textures[0]);
        var normals = Image(Path.Combine(f.Root, "lod"), mapped.Material.Textures[1]);
        Assert.True(image.Pixels[72 * 4 + 2] > 220); Assert.True(image.Pixels[110 * 4 + 1] > 220);
        Assert.True(normals.Pixels[72 * 4] > 240);
        Assert.Empty(Directory.GetDirectories(Path.Combine(f.Root, "lod"), ".recipe-build-*"));
    }

    [Fact]
    public void RepeatedUvsRequireCompleteMatchingCoverageIncludingCroppedTiles()
    {
        string tile(float x, float y) => FormattableString.Invariant($"0\t1\t1\t1\t{Red}\t1\t1\t{x}\t{y}\t{Atlas}\t2\t2");
        var rules = Recipes(tile(0, 0), tile(1, 0), tile(0, 1), tile(1, 1));
        var layout = new TextureRecipeLayout(rules, _ => true);
        var mapping = Assert.Single(layout.Find(Red, Vector2.Zero, new(2, 2)));
        Assert.Equal(new Vector2(.5f), mapping.Scale);
        rules.Entries.RemoveAt(3);
        Assert.Empty(new TextureRecipeLayout(rules, _ => true).Find(Red, Vector2.Zero, new(2, 2)));
        rules = Recipes(tile(-.5f, 0));
        Assert.Empty(new TextureRecipeLayout(rules, _ => true).Find(Red, Vector2.Zero, Vector2.One));
        Assert.Single(new TextureRecipeLayout(rules, _ => true).Find(Red, new(.5f, 0), Vector2.One));
    }

    [Fact]
    public void OverlappingDifferentTilesAndAmbiguousLayoutsNeverGetWrongUvMapping()
    {
        var rules = Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Green, Atlas, 0));
        Assert.Empty(new TextureRecipeLayout(rules, _ => true).Find(Red, Vector2.Zero, Vector2.One));
        using var f = new TextureFixture();
        rules = Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Red, @"textures\lod\second.dds", 0));
        var mapper = Mapper(f, rules);
        Assert.Null(mapper.Map(TextureFixture.Card("shape", Red), [Input(Red, Red)]));
        Assert.Contains(mapper.Report, r => r.Status == "Retained" && r.Detail.Contains("ambiguous"));
    }

    [Fact]
    public void MergedShapeRebuildsEveryOverrideAndConflictsStopPublication()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        TextureFixture.Put(f.Data, Blue, TextureFixture.Solid(10, 20, 240));
        var mapper = Mapper(f, Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Green, Atlas, 1)));
        var part = TextureFixture.Card("A - B", Atlas);
        var mapped = mapper.Map(part, [Input(Red, Blue, shape: "A"), Input(Green, Red, shape: "B")])!;
        var image = Image(Path.Combine(f.Root, "lod"), mapped.Material.Textures[0]);
        Assert.True(image.Pixels[2] > 220); Assert.True(image.Pixels[80 * 4] > 220);
        Assert.Throws<InvalidDataException>(() => mapper.Map(part, [Input(Red, Blue, shape: "A"), Input(Red, Green, shape: "B")]));
        Assert.Throws<InvalidDataException>(mapper.ThrowIfFailed);
    }

    [Fact]
    public void GeneratorMatchesAllMergedFullShapesAndHandlesNormalOnlyFullTextures()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        TextureFixture.Put(f.Data, Blue, TextureFixture.Solid(10, 20, 240));
        string full = @"meshes\full.nif", lod = @"meshes\lod.nif", normal = @"textures\special_n.dds";
        TextureFixture.Put(f.Data, normal, TextureFixture.Solid(255, 128, 128));
        TextureFixture.Put(f.Data, full, LodNifWriter.Write("full", [TextureFixture.Card("A", Red), TextureFixture.Card("B", Green)], preserveShapeNames: true));
        TextureFixture.Put(f.Data, lod, LodNifWriter.Write("lod", [TextureFixture.Card("A - B", Atlas)], preserveShapeNames: true));
        var variant = TextureVariant.Create(full, [new("A", Blue, normal), new("B", Red, null)]);
        var assets = AssetIndex.Build(GameRelease.SkyrimSE, f.Data);
        var mapper = new RecipeTextureMapper(Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Green, Atlas, 1)), assets, Path.Combine(f.Root, "lod"));
        var generator = new LodGenerator(assets) { TextureVariants = new Dictionary<string, TextureVariant> { [variant.Id] = variant }, RecipeMapper = mapper.Map };
        var mesh = generator.GetMesh(LodMeshKey.Make(lod, variant.Id))!;
        var image = Image(Path.Combine(f.Root, "lod"), mesh.Parts[0].Material.Textures[0]);
        Assert.True(image.Pixels[2] > 220); Assert.True(image.Pixels[80 * 4] > 220);
        var onlyNormal = TextureVariant.Create(full, [new("A", null, normal)]);
        var plain = new LodGenerator(assets) { TextureVariants = new Dictionary<string, TextureVariant> { [onlyNormal.Id] = onlyNormal } };
        Assert.Equal(normal, plain.GetMesh(LodMeshKey.Make(full, onlyNormal.Id))!.Parts[0].Material.Textures[1]);
    }

    [Fact]
    public void PatchedFullTextureCanBeAssociatedByOneAtlasRegionButNotAnOverlap()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        TextureFixture.Put(f.Data, Blue, TextureFixture.Solid(10, 20, 240));
        var rules = Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Green, Atlas, 1));
        var part = TextureFixture.Card("shape", Atlas);
        part = RecipeTextureMapper.Copy(part, part.Material, part.UVs.Select(uv => uv with { X = uv.X * .5f }).ToArray());
        var mapper = Mapper(f, rules);
        var mapped = mapper.Map(part, [Input(Blue, Blue)])!;
        Assert.True(Image(Path.Combine(f.Root, "lod"), mapped.Material.Textures[0]).Pixels[2] > 220);
        rules.Entries.Add(new("overlay", 3, Tile: TextureRecipe.ParseTile(TextureFixture.Tile(Green, Atlas, 0).Replace("\t1\t1\t0\t0\t", "\t0.5\t1\t0\t0\t"), false)));
        var overlapping = Mapper(f, rules);
        overlapping.Map(part, [Input(Blue, Blue)]);
        Assert.Contains(overlapping.Report, r => r.Status == "Retained" && r.Detail.Contains("unique atlas tile"));
    }

    [Fact]
    public void SourceContentChangesInvalidateFingerprintsEvenWithSameSizeAndTimestamp()
    {
        using var f = new TextureFixture(); var rules = Recipes(TextureFixture.Tile(Red, Atlas, 0));
        var before = Mapper(f, rules).InputFingerprint([]);
        string file = GamePath.ToFileSystemPath(f.Data, Red); var stamp = File.GetLastWriteTimeUtc(file); long size = new FileInfo(file).Length;
        TextureFixture.Put(f.Data, Red, TextureFixture.Solid(200, 20, 25)); File.SetLastWriteTimeUtc(file, stamp);
        Assert.Equal(size, new FileInfo(file).Length); Assert.NotEqual(before, Mapper(f, rules).InputFingerprint([]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleSourceAtlasVariantsPreserveWrappingUvs(bool copied)
    {
        using var f = new TextureFixture();
        TextureFixture.Put(f.Data, Blue, TextureFixture.Solid(10, 20, 240));
        var rules = Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Red, Atlas, 1));
        if (copied) rules.Entries = [new("copy", 1, Copy: new(Red, Atlas))];
        var part = TextureFixture.Card("shape", Atlas);
        part = RecipeTextureMapper.Copy(part, part.Material, part.UVs.Select(uv => uv * 2 - new Vector2(.2f)).ToArray());
        // The patched full model names a different source; the atlas still has just one contributor.
        var mapper = Mapper(f, rules);
        var mapped = mapper.Map(part, [Input(Green, Blue)])!;
        Assert.Equal(part.UVs, mapped.UVs);
        Assert.Same(part.Positions, mapped.Positions);
        var image = Image(Path.Combine(f.Root, "lod"), mapped.Material.Textures[0]);
        for (int i = 0; i < image.Pixels.Length; i += 4) Assert.True(image.Pixels[i + 2] > 220);
        mapper.ThrowIfFailed();
    }

    [Fact]
    public void FullShapeAlreadyUsingAtlasDoesNotSubstituteAtlasIntoItsOwnTiles()
    {
        using var f = new TextureFixture();
        TextureFixture.Put(f.Data, Atlas, TextureFixture.Solid(10, 20, 240));
        string normal = TextureRecipeRenderer.Normal(Atlas);
        TextureFixture.Put(f.Data, normal, TextureFixture.Solid(128, 128, 255));
        var mapper = Mapper(f, Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Green, Atlas, 1)));
        var part = TextureFixture.Card("platform", Atlas);
        part = RecipeTextureMapper.Copy(part, part.Material with { Textures = [Atlas, normal] },
            part.UVs.Select(uv => uv with { X = uv.X * .5f }).ToArray());
        Assert.Same(part, mapper.Map(part, [new("platform", Atlas, normal, Atlas, normal, false)]));
        Assert.Empty(mapper.Report);
        Assert.False(Directory.Exists(Path.Combine(f.Root, "lod")));
    }

    [Fact]
    public void WrappingUnknownAlternateCannotChooseBetweenDifferentAtlasSources()
    {
        using var f = new TextureFixture();
        TextureFixture.Put(f.Data, Blue, TextureFixture.Solid(10, 20, 240));
        var mapper = Mapper(f, Recipes(TextureFixture.Tile(Red, Atlas, 0), TextureFixture.Tile(Green, Atlas, 1)));
        var part = TextureFixture.Card("shape", Atlas);
        part = RecipeTextureMapper.Copy(part, part.Material, part.UVs.Select(uv => uv * 2).ToArray());
        Assert.Throws<InvalidDataException>(() => mapper.Map(part, [Input("textures\\patched.dds", Blue)]));
        Assert.Contains(mapper.Report, r => r.Status == "Failed" && r.Detail.Contains("cannot be associated"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DuplicateFullShapeNamesRequireAUniqueMaterialAndNoAmbiguousAlternate(bool alternate, bool unknownLodTexture)
    {
        using var f = new TextureFixture();
        string full = @"meshes\fixture\duplicate.nif", lod = @"meshes\fixture\duplicate_lod_0.nif";
        TextureFixture.Put(f.Data, full, LodNifWriter.Write("full",
            [TextureFixture.Card("roof", Green), TextureFixture.Card("roof", Red)], preserveShapeNames: true));
        TextureFixture.Put(f.Data, lod, LodNifWriter.Write("lod",
            [TextureFixture.Card("roof", unknownLodTexture ? Blue : Red)], preserveShapeNames: true));
        var variant = TextureVariant.Create(full, alternate ? [new("roof", Blue, null)] : []);
        var generator = new LodGenerator(AssetIndex.Build(GameRelease.SkyrimSE, f.Data))
        { TextureVariants = new Dictionary<string, TextureVariant> { [variant.Id] = variant } };
        if (alternate || unknownLodTexture)
        {
            var error = Assert.Throws<InvalidDataException>(() => generator.GetMesh(LodMeshKey.Make(lod, variant.Id)));
            Assert.Contains(alternate ? "duplicated" : "conflicting full textures", error.Message);
        }
        else Assert.Equal(Red, Assert.Single(generator.GetMesh(LodMeshKey.Make(lod, variant.Id))!.Parts).Material.Textures[0]);
    }

    [Fact]
    public void ExistingLodMaterialCannotDiscardOtherNamedContributorsToMergedShape()
    {
        using var f = new TextureFixture();
        string full = @"meshes\fixture\merged.nif", lod = @"meshes\fixture\merged_lod_0.nif";
        TextureFixture.Put(f.Data, full, LodNifWriter.Write("full",
            [TextureFixture.Card("A", Red), TextureFixture.Card("B", Green)], preserveShapeNames: true));
        TextureFixture.Put(f.Data, lod, LodNifWriter.Write("lod", [TextureFixture.Card("A - B", Red)], preserveShapeNames: true));
        var variant = TextureVariant.Create(full, []);
        var generator = new LodGenerator(AssetIndex.Build(GameRelease.SkyrimSE, f.Data))
        { TextureVariants = new Dictionary<string, TextureVariant> { [variant.Id] = variant } };
        Assert.Contains("conflicting full textures",
            Assert.Throws<InvalidDataException>(() => generator.GetMesh(LodMeshKey.Make(lod, variant.Id))).Message);
    }
}
