using System.Text;
using AnvilLOD.Core.Lod;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins;
using AnvilLOD.Plugins.Authoring;
using AnvilLOD.Testing;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AnvilLOD.Core.Tests;

public class AutomaticObjectLodTests
{
    private const string Model = @"meshes\fixture\wall.nif";

    [Fact]
    public void DenseModelsFitDistinctMonotonicBudgets()
    {
        using var f = new TextureFixture();
        const int n = 40;
        var vertices = new List<System.Numerics.Vector3>(); var triangles = new List<ushort>();
        for (int y = 0; y <= n; y++) for (int x = 0; x <= n; x++)
            vertices.Add(new(x * 20, y * 20, MathF.Sin(x * .4f) * MathF.Cos(y * .4f) * 20));
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        { ushort a = (ushort)(y * (n + 1) + x), b = (ushort)(a + 1), c = (ushort)(a + n + 1), d = (ushort)(c + 1); triangles.AddRange([a, b, d, a, d, c]); }
        var part = TextureFixture.Card("surface", @"textures\red.dds");
        var dense = new AnvilLOD.Meshes.LodMeshPart { Name = part.Name, Material = part.Material, Positions = vertices.ToArray(),
            Normals = vertices.Select(_ => System.Numerics.Vector3.UnitZ).ToArray(),
            Tangents = vertices.Select(_ => System.Numerics.Vector3.UnitX).ToArray(), Bitangents = vertices.Select(_ => System.Numerics.Vector3.UnitY).ToArray(),
            UVs = vertices.Select(v => new System.Numerics.Vector2(v.X / 800, v.Y / 800)).ToArray(), Triangles = triangles.ToArray() };
        TextureFixture.Put(f.Data, Model, LodNifWriter.Write("dense", [dense], preserveShapeNames: true));
        var source = new AutomaticObjectLod(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), new(true, 300, 120, 40), Path.Combine(f.Root, "cache"));
        for (int l = 0; l < 3; l++) Assert.NotNull(source.Resolve(Model, l));
        var report = source.Report;
        Assert.All(report, r => Assert.InRange(r.Triangles, 1, r.Budget));
        Assert.True(report[0].Triangles >= report[1].Triangles && report[1].Triangles >= report[2].Triangles);
        Assert.Equal(3200, report[0].SourceTriangles);
    }

    [Fact]
    public void OnlyRequestedMissingLevelsAreGeneratedAndExclusionsWin()
    {
        var rules = new LodRules();
        rules.AddFile("rules.ini", "[Skyrim LODGen]\nLODGen1=skip,None,None,None,,Unchanged,0\nLODGen2=water,,,,FarLOD,Unchanged,0\nLODGen3=\\,Level0,None,Level2,None,Unchanged,0\n");
        var called = new List<int>();
        var resolver = new LodMeshResolver(LodMeshIndex.Build([]), rules, _ => true)
        { MissingLevel = (_, level) => { called.Add(level); return "generated" + level; } };
        Assert.False(resolver.Resolve(null, null, @"meshes\skip.nif", null).Meshes.HasAny);
        Assert.False(resolver.Resolve(null, null, @"meshes\water.nif", null).Meshes.HasAny);
        Assert.Empty(called);
        var resolution = resolver.Resolve(null, null, Model, ["installed", null, null, null]);
        Assert.Equal("installed", resolution.Meshes.Lod4); Assert.Null(resolution.Meshes.Lod8);
        Assert.Equal("generated2", resolution.Meshes.Lod16); Assert.Equal([2], called);
    }

    [Fact]
    public void VerifiedCacheRepairsCorruptionAndInvalidatesOnSourceOrSettingsChanges()
    {
        using var f = new TextureFixture(); Put(f, @"textures\red.dds");
        string cache = Path.Combine(f.Root, "cache");
        AutomaticObjectLod Source(AutomaticObjectSettings? settings = null) => new(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings ?? new(true), cache);
        var first = Source(); string path = first.Resolve(Model, 0)!; Assert.NotNull(path);
        Assert.Equal("Generated", Assert.Single(first.Report).Status);
        var again = Source(); Assert.Equal(path, again.Resolve(Model, 0)); Assert.Equal("Cached", Assert.Single(again.Report).Status);
        File.WriteAllBytes(GamePath.ToFileSystemPath(cache, path), [1, 2, 3]);
        var repaired = Source(); Assert.Equal(path, repaired.Resolve(Model, 0)); Assert.Equal("Generated", Assert.Single(repaired.Report).Status);
        Assert.Equal(2, NifGeometryReader.Read(path, File.ReadAllBytes(GamePath.ToFileSystemPath(cache, path))).Parts.Sum(p => p.TriangleCount));
        Assert.NotEqual(path, Source(new(true, Detail: 2)).Resolve(Model, 0));
        Put(f, @"textures\green.dds"); Assert.NotEqual(path, Source().Resolve(Model, 0));
    }

    [Theory]
    [InlineData("missing", "Missing source texture")]
    [InlineData("small", "minimum model size")]
    [InlineData("budget", "triangle budget")]
    [InlineData("corrupt", "")]
    public void UnusableSourcesRemainAbsentWithReasons(string kind, string reason)
    {
        using var f = new TextureFixture(); Put(f, kind == "missing" ? @"textures\absent.dds" : @"textures\red.dds");
        if (kind == "corrupt") TextureFixture.Put(f.Data, Model, [1, 2]);
        var settings = kind == "small" ? new AutomaticObjectSettings(true, MinSize: 1000)
            : kind == "budget" ? new AutomaticObjectSettings(true, NearBudget: 1, MiddleBudget: 1, FarBudget: 1) : new(true);
        var source = new AutomaticObjectLod(AssetIndex.Build(GameRelease.SkyrimSE, f.Data), settings, Path.Combine(f.Root, "cache"));
        Assert.Null(source.Resolve(Model, 0)); var report = Assert.Single(source.Report);
        Assert.Equal("Skipped", report.Status); Assert.Contains(reason, report.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GenerationAppliesRecordAndSeasonVariantsAndOptionalAtlases(bool atlas, bool optimize)
    {
        using var f = new TerrainFixture(editMod: mod =>
        {
            var normal = mod.Statics.AddNew(); normal.EditorID = "FixtureObject"; normal.Model = new Model { File = @"fixture\wall.nif" };
            var winter = mod.Statics.AddNew(); winter.EditorID = "WinterObject"; winter.Model = new Model { File = @"fixture\wall.nif" };
            var tx = mod.TextureSets.AddNew(); tx.Diffuse = @"green.dds";
            var alt = new AlternateTexture { Name = "wall", Index = 0 }; alt.NewTexture.SetTo(tx); winter.Model.AlternateTextures = [alt];
            var placed = new PlacedObject(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE) { Placement = new Placement { Position = new P3Float(-9500, -9500, 0) } };
            placed.Base.SetTo(normal); mod.Worldspaces.Single().SubCells[0].Items[0].Items[0].Temporary.Add(placed);
        });
        Put(f.Textures, @"textures\red.dds");
        if (optimize) TextureFixture.Put(f.Textures.Data, @"textures\red.dds",
            ObjectTextureOptimizationTests.Raw(256, 256, (_, _) => (240, 12, 6, 255)));
        TextureFixture.Put(f.Textures.Data, @"textures\default_n.dds", TextureFixture.Solid(128, 128, 255));
        TextureFixture.Put(f.Textures.Data, @"seasons\Fixture_WIN.ini", Encoding.UTF8.GetBytes("[Statics]\nFixtureObject|WinterObject\n"));
        string output = Path.Combine(f.Root, "lod");
        using var limits = RunLimits.Enter(2, cacheDirectory: Path.Combine(f.Root, "cache"));
        var result = ScanPipeline.Run(new(f.Game, new(TreeLod: false, DynamicLod: false, GridObjects: false), output,
            Generate: true, GrassLod: false, ChildWorlds: false, SkseDll: SkseDllChoice.None, Seasons: true,
            RemoveBuried: false, RemoveUnderwater: false, DetailedShips: false, ObjectAtlas: atlas, AtlasCellSize: 64,
            OptimizeObjectTextures: optimize, ObjectTextureSize: 128,
            AutomaticObjects: new(true)));
        Assert.Equal(0, result.Generation!.BlocksFailed); Assert.Contains(result.AutomaticObjects!, r => r.Mesh is not null);
        var paths = Directory.GetFiles(output, "*.bto", SearchOption.AllDirectories);
        Assert.NotEmpty(paths);
        var winter = Assert.Single(paths, p => p.EndsWith("WIN.bto", StringComparison.Ordinal));
        Assert.Equal(@"textures\green.dds", GamePath.NormalizeTexture(Assert.Single(NifGeometryReader.Read(winter, File.ReadAllBytes(winter), passthru: true).Parts).Material.Textures[0]), ignoreCase: true);
        var normal = Assert.Single(paths, p => !p.EndsWith("WIN.bto", StringComparison.Ordinal));
        Assert.Contains(NifGeometryReader.Read(normal, File.ReadAllBytes(normal), passthru: true).Parts,
            p => atlas ? p.Material.Textures[0].Contains(@"pengulod\objects\") : optimize ? p.Material.Textures[0].Contains(@"pengulod\object-textures\")
                : GamePath.NormalizeTexture(p.Material.Textures[0]).Equals(@"textures\red.dds", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(output, "PenguLOD.objects.json")));
        Assert.False(Directory.Exists(Path.Combine(f.Textures.Data, "meshes", "pengulod")));
    }

    private static void Put(TextureFixture f, string texture) => TextureFixture.Put(f.Data, Model,
        LodNifWriter.Write("model", [TextureFixture.Card("wall", texture)], preserveShapeNames: true));
}
