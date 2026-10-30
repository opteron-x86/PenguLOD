using System.Text;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using AnvilLOD.Textures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AnvilLOD.Core.Tests;

public class RecipePipelineTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TextureLodSeasonalPipelineFallsBackLocallyAndStillRejectsCorruptTextures(bool objectAtlas)
    {
        using var f = new TerrainFixture(editMod: mod =>
        {
            Static Add(string id, string texture)
            {
                var tx = mod.TextureSets.AddNew(); tx.Diffuse = texture; tx.NormalOrGloss = "red_n.dds";
                var alt = new AlternateTexture { Name = "wall", Index = 0 }; alt.NewTexture.SetTo(tx);
                var record = mod.Statics.AddNew(); record.EditorID = id;
                record.Model = new Model { File = @"fixture\wall.nif", AlternateTextures = [alt] };
                record.MajorFlags |= Static.MajorFlag.HasDistantLOD;
                return record;
            }
            var normal = Add("FixtureWall", "blue.dds"); Add("WinterWall", "green.dds");
            var placed = new PlacedObject(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE) { Placement = new Placement { Position = new P3Float(-9500, -9500, 0) } };
            placed.Base.SetTo(normal); mod.Worldspaces.Single().SubCells[0].Items[0].Items[0].Temporary.Add(placed);
        });
        using var limits = RunLimits.Enter(2);
        string data = f.Textures.Data, output = Path.Combine(f.Root, "lod"), cache = Path.Combine(f.Root, "cache");
        TextureFixture.Put(data, @"textures\blue.dds", TextureFixture.Solid(10, 15, 230));
        TextureFixture.Put(data, @"meshes\fixture\wall.nif", LodNifWriter.Write("full", [TextureFixture.Card("wall", @"textures\red.dds")], preserveShapeNames: true));
        var lod = TextureFixture.Card("wall", @"textures\lod\atlas.dds");
        lod = RecipeTextureMapper.Copy(lod, lod.Material, lod.UVs.Select(v => v with { X = v.X * .5f }).ToArray());
        TextureFixture.Put(data, @"meshes\fixture\wall_lod_0.nif", LodNifWriter.Write("lod", [lod], preserveShapeNames: true));
        TextureFixture.Put(data, @"seasons\Fixture_WIN.ini", Encoding.UTF8.GetBytes("[Statics]\nFixtureWall|WinterWall\n"));
        var request = new GenerationPipelineRequest(Textures: f.Textures.Request,
            Lod: new(f.Game, new(Worldspaces: ["FixtureWorld"], TreeLod: false, DynamicLod: false, GridObjects: false),
                OutputFolder: output, Generate: true, Seasons: true, ObjectAtlas: objectAtlas, GrassLod: false, ChildWorlds: false,
                SkseDll: SkseDllChoice.None, RemoveBuried: false, RemoveUnderwater: false, CleanOutput: false), VerifySourceContents: true);
        var result = GenerationPipeline.Run(request, cache); Assert.True(result.Success, result.Error);
        var ordinary = Read(output, ""); var winter = Read(output, ".WIN");
        Assert.NotEqual(ordinary.Parts[0].Material.Textures[0], winter.Parts[0].Material.Textures[0]);
        AssertColor(output, ordinary.Parts[0], 2); AssertColor(output, winter.Parts[0], 1);
        Assert.True(File.Exists(Path.Combine(output, "PenguLOD.recipes.json")));
        Assert.Empty(Directory.GetDirectories(output, ".recipe-build-*"));
        var reused = GenerationPipeline.Run(request, cache); Assert.True(reused.Success, reused.Error);
        Assert.All(reused.Journal.Stages, s => Assert.True(s.Reused));

        string source = GamePath.ToFileSystemPath(data, @"textures\blue.dds"); var stamp = File.GetLastWriteTimeUtc(source);
        TextureFixture.Put(data, @"textures\blue.dds", TextureFixture.Solid(20, 25, 190)); File.SetLastWriteTimeUtc(source, stamp);
        var changed = GenerationPipeline.Run(request, cache); Assert.True(changed.Success, changed.Error);
        Assert.All(changed.Journal.Stages, s => Assert.False(s.Reused));
        Assert.NotEqual(ordinary.Parts[0].Material.Textures[0], Read(output, "").Parts[0].Material.Textures[0]);

        File.Delete(source);
        var fallback = GenerationPipeline.Run(request, cache);
        Assert.True(fallback.Success, fallback.Error);
        AssertColor(output, Read(output, "").Parts[0], 0, f.Textures.Output);
        AssertColor(output, Read(output, ".WIN").Parts[0], 1);
        Assert.Contains(fallback.LodReceipt!.Warnings, w => w.Contains("Texture matching fallback") && w.Contains("wall_lod_0.nif") && w.Contains("blue.dds"));
        var report = System.Text.Json.JsonSerializer.Deserialize<RecipeTextureMapper.MappingReport[]>(File.ReadAllText(Path.Combine(output, "PenguLOD.recipes.json")), PersistentPipeline.Json)!;
        Assert.Contains(report, r => r.Status == "Fallback" && r.Model.Contains("wall_lod_0.nif"));
        Assert.Contains(report, r => r.Status == "Variant");
        var fallbackReused = GenerationPipeline.Run(request, cache);
        Assert.True(fallbackReused.Success, fallbackReused.Error);
        Assert.All(fallbackReused.Journal.Stages, s => Assert.True(s.Reused));
        Assert.Contains(fallbackReused.LodReceipt!.Warnings, w => w.Contains("Texture matching fallback"));

        string path = GamePath.ToFileSystemPath(output, @"meshes\terrain\FixtureWorld\objects\FixtureWorld.4.-4.-4.bto");
        string before = OutputSnapshot.HashFile(path);
        TextureFixture.Put(data, @"textures\blue.dds", [1, 2, 3, 4]);
        var failed = GenerationPipeline.Run(request with { Preflight = false }, cache);
        Assert.False(failed.Success); Assert.Equal("lod", failed.FailedStage); Assert.Contains("Recipe ", failed.Error);
        Assert.DoesNotContain("texture missing", failed.Error);
        Assert.Equal(before, OutputSnapshot.HashFile(path));
        TextureFixture.Put(data, @"textures\blue.dds", TextureFixture.Solid(20, 25, 190));
        var recovered = GenerationPipeline.Run(request, cache); Assert.True(recovered.Success, recovered.Error);
        AssertColor(output, Read(output, "").Parts[0], 2);
        Assert.DoesNotContain(recovered.LodReceipt!.Warnings, w => w.Contains("Texture matching fallback"));
    }
    private static LodMesh Read(string output, string season)
    {
        string path = GamePath.ToFileSystemPath(output, $@"meshes\terrain\FixtureWorld\objects\FixtureWorld.4.-4.-4{season}.bto");
        return NifGeometryReader.Read(path, File.ReadAllBytes(path), passthru: true);
    }
    private static void AssertColor(string output, LodMeshPart part, int channel, string? textureOutput = null)
    {
        string texture = GamePath.ToFileSystemPath(output, part.Material.Textures[0]);
        if (!File.Exists(texture) && textureOutput is not null) texture = GamePath.ToFileSystemPath(textureOutput, part.Material.Textures[0]);
        var image = TextureImages.Decode(File.ReadAllBytes(texture));
        var uv = part.UVs.Aggregate(System.Numerics.Vector2.Zero, (a, b) => a + b) / part.UVs.Length;
        int index = (Math.Clamp((int)(uv.Y * image.Height), 0, image.Height - 1) * image.Width + Math.Clamp((int)(uv.X * image.Width), 0, image.Width - 1)) * 4;
        Assert.True(image.Pixels[index + channel] > 170);
        Assert.True(image.Pixels[index + channel] > image.Pixels[index + (channel == 0 ? 1 : 0)] + 100);
    }
}
