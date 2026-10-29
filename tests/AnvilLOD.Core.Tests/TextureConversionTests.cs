using System.Buffers.Binary;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Textures;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;

namespace AnvilLOD.Core.Tests;

public class TextureConversionTests
{
    private static TextureConversion Rule(int filter = 7, float contrast = 0, float brightness = 0, int start = 2, bool alpha = true)
        => new(@"textures\source.dds", @"textures\converted.dds", filter, contrast, brightness, start, 0, 1, 1, 1, alpha);
    private static TextureImage Solid(byte value, byte alpha = 75)
        => new(Enumerable.Range(0, 64).SelectMany(_ => new[] { value, value, value, alpha }).ToArray(), 8, 8);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void EveryFilterProducesFiniteFullMipChainWithoutChangingInput(int filter)
    {
        var source = Solid(100); var before = source.Pixels.ToArray();
        var levels = TextureConversionRenderer.Levels(source, Rule(filter)).ToArray();
        Assert.Equal(new[] { 8, 4, 2, 1 }, levels.Select(l => l.Width));
        foreach (var level in levels)
        {
            Assert.InRange(level.Pixels[0], 97, 102);
            Assert.InRange(level.Pixels[3], 73, 77);
        }
        Assert.Equal(before, source.Pixels);
    }

    [Fact]
    public void ProgressiveContrastBrightnessStartsAtRequestedMipAndDoesNotCompound()
    {
        var levels = TextureConversionRenderer.Levels(Solid(100), Rule(0, 10, 10, 1)).ToArray();
        Assert.Equal(new byte[] { 100, 136, 171, 206 }, levels.Select(l => l.Pixels[0]));
        Assert.All(levels, l => Assert.Equal(75, l.Pixels[3]));
        var noAlpha = TextureConversionRenderer.Levels(Solid(100), Rule(alpha: false)).ToArray();
        Assert.All(noAlpha, l => Assert.All(l.Pixels.Where((_, i) => i % 4 == 3), a => Assert.Equal(255, a)));
    }

    [Fact]
    public void NearestUsesUpperLeftSamplesWhileLinearAveragesTheFootprint()
    {
        var source = new TextureImage([0, 0, 0, 255, 200, 200, 200, 255, 200, 200, 200, 255, 0, 0, 0, 255], 2, 2);
        Assert.Equal(0, TextureConversionRenderer.Resize(source, 1, 1, 0).Pixels[0]);
        Assert.Equal(100, TextureConversionRenderer.Resize(source, 1, 1, 1).Pixels[0]);
        byte[] dds = TextureConversionRenderer.Encode(Solid(100), Rule());
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(28)));
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(16)));
        Assert.Equal(148 + 64 + 16 + 16 + 16, dds.Length);
    }

    [Theory]
    [InlineData("10", "0", "0", "0")]
    [InlineData("7", "NaN", "0", "0")]
    [InlineData("7", "0", "101", "0")]
    [InlineData("7", "0", "0", "-1")]
    public void InvalidConversionSettingsFailBeforePublication(string filter, string contrast, string brightness, string start)
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        Assert.True(TextureGeneration.Run(f.Request).Published);
        string before = File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName));
        File.AppendAllText(f.RuleFile, $"\n0\t1\t1\t1\ttextures\\red.dds\tx\t{filter}\t{contrast}\t{brightness}\ttextures\\red.dds\t{start}\tx");
        Assert.Throws<InvalidDataException>(() => TextureGeneration.Run(f.Request));
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.Output, TextureGenerationManifest.FileName)));
    }

    [Fact]
    public void SelfConversionRetainsVerifiedOriginalInputAndItsFingerprint()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        byte[] source = File.ReadAllBytes(GamePath.ToFileSystemPath(f.Data, @"textures\red.dds"));
        File.AppendAllText(f.RuleFile, "\n0\t1\t1\t1\ttextures\\red.dds\tx\t7\t12\t0\ttextures\\red.dds\t2\tx");
        Assert.True(TextureGeneration.Run(f.Request).Published);
        var manifest = TextureGenerationManifest.Read(f.Output, true)!; manifest.Verify(f.Output);
        Assert.Equal(source, File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, manifest.RecipeInputs[@"textures\red.dds"])));
        string before = manifest.Fingerprint;
        manifest.Recipes!.Entries[0] = manifest.Recipes.Entries[0] with { Line = 90 };
        Assert.NotEqual(before, manifest.Fingerprint);
    }

    [Fact]
    public void ExplicitNormalRecipeWinsOverAutomaticAtlasCompanion()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        TextureFixture.Put(f.Data, @"textures\custom_n.dds", TextureFixture.Solid(255, 128, 128));
        string source = @"textures\custom_n.dds", destination = @"textures\lod\atlas_n.dds";
        File.WriteAllText(Path.Combine(f.Resources, "Configs", "DynDOLOD_SSE_TexGen_copy_skyrimesm.txt"), source + "," + destination);
        Assert.True(TextureGeneration.Run(f.Request).Published);
        Assert.Equal(File.ReadAllBytes(GamePath.ToFileSystemPath(f.Data, source)), File.ReadAllBytes(GamePath.ToFileSystemPath(f.Output, destination)));
    }

    [Fact]
    public void ConversionCopyAndStitchChainsResolveInDependencyOrderAndCyclesFail()
    {
        var set = new TextureRecipeSet { BaseSize = 4, Entries = [
            new("fixture", 1, Copy: new(@"textures\converted.dds", @"textures\copy.dds")),
            new("fixture", 2, Conversion: Rule() with { Source = @"textures\missing.dds|textures\source.dds" }),
            new("fixture", 3, Tile: TextureRecipe.ParseTile(TextureFixture.Tile(@"textures\copy.dds", @"textures\atlas.dds", 0), false))] };
        var renderer = new TextureRecipeRenderer(set, p => p == @"textures\source.dds" ? TextureFixture.Solid(10, 200, 20) : null, _ => { });
        var atlas = TextureImages.Decode(renderer.Build(@"textures\atlas.dds")); Assert.True(atlas.Pixels[1] > 190);
        set.Entries.Add(new("cycle", 1, Copy: new(@"textures\copy.dds", @"textures\source.dds")));
        var error = Assert.Throws<InvalidDataException>(() => new TextureRecipeRenderer(set, _ => null, _ => { }).Build(@"textures\copy.dds"));
        Assert.Contains("Cyclic", error.Message);
    }
}
