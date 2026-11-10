using System.Text.Json;
using PenguLOD.Core.Incremental;
using PenguLOD.Core.Lod;
using PenguLOD.Core.Paths;
using PenguLOD.Meshes.Nif;
using PenguLOD.Meshes.Authoring;
using PenguLOD.Plugins;
using PenguLOD.Testing;

namespace PenguLOD.Core.Tests;

public sealed class ProductIdentityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pengulod-identity-").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("AnvilLOD.esm")]
    [InlineData("ANVILLOD.ESP")]
    [InlineData("AnvilLOD Effects 2.esp")]
    [InlineData("PenguLOD.esm")]
    [InlineData("PenguLOD Effects 2.esp")]
    public void NeitherGenerationOfOutputCanFeedBackIntoSourceObjects(string name)
    {
        Assert.True(GeneratedLodInputs.IsPlugin(name));
        Assert.False(GeneratedLodInputs.IsPlugin("My " + name));
    }

    [Theory]
    [InlineData("AnvilLOD.manifest.json")]
    [InlineData("AnvilLOD.textures.json")]
    [InlineData("AnvilLOD.grass.json")]
    [InlineData("AnvilLOD Effects 2.esp")]
    public void LegacyDestinationIsRejectedBeforeCleanupOrPublication(string name)
    {
        using var fixture = new TextureFixture();
        string original = Path.Combine(_root, name);
        File.WriteAllText(original, "keep this baseline");
        var error = Assert.Throws<InvalidOperationException>(() => GenerationDestination.Validate(fixture.Game, _root));
        Assert.Contains(_root, error.Message);
        Assert.Contains(name, error.Message);
        Assert.False(OutputManagement.Plan([fixture.Data], _root).CanPublish);
        Assert.Equal("keep this baseline", File.ReadAllText(original));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void ReadsOldManifestsWithoutRenamingAndPrefersCurrentOnes()
    {
        File.WriteAllText(Path.Combine(_root, "AnvilLOD.textures.json"), JsonSerializer.Serialize(new TextureGenerationManifest { Source = "legacy" }));
        Assert.Equal("legacy", TextureGenerationManifest.Read(_root)!.Source);
        new TextureGenerationManifest { Source = "current" }.Save(_root);
        Assert.Equal("current", TextureGenerationManifest.Read(_root)!.Source);
        Assert.Contains("legacy", File.ReadAllText(Path.Combine(_root, "AnvilLOD.textures.json")));
        File.WriteAllText(Path.Combine(_root, "AnvilLOD.grass.json"), JsonSerializer.Serialize(new GrassGenerationManifest {
            Complete = true, Files = new() { [@"grass\Worldx0000y0000.cgid"] = new string('A', 64) } }));
        Assert.True(GrassGenerationManifest.Read(_root, requireFull: true)!.Complete);
        File.WriteAllText(Path.Combine(_root, "AnvilLOD.manifest.json"), JsonSerializer.Serialize(new BuildManifest {
            Quads = new() { [@"meshes\terrain\World\objects\World.4.0.0.bto"] = "legacy-hash" } }));
        Assert.Equal("legacy-hash", Assert.Single(BuildManifest.LoadOrEmpty(_root).Quads).Value);
    }

    [Theory]
    [InlineData("AL:")]
    [InlineData("PL:")]
    public void CostReportsReadBothLayerIdentities(string prefix)
    {
        byte[] mesh = LodNifWriter.Write(prefix + "tree3d:4", [TextureFixture.Card("tree", @"textures\red.dds")]);
        var cost = Assert.Single(NifCostReader.Read(mesh));
        Assert.Equal("tree3d", cost.Layer);
        Assert.Equal(2, cost.Triangles);
    }

    [Fact]
    public void LegacyRuntimeIsReportedWithoutChangingAnyAssets()
    {
        using var f = new TextureFixture();
        TextureFixture.Put(f.Data, @"skse\plugins\AnvilLOD.dll", "legacy"u8.ToArray());
        var request = new GenerationPipelineRequest(Lod: new(f.Game, new(TreeLod: false, DynamicLod: true, GridObjects: false, RenderTreeBillboards: false), OutputFolder: Path.Combine(_root, "new"), SkseDll: SkseDllChoice.None));
        var report = PipelineDiagnostics.Run(request);
        Assert.Contains(report.Issues, i => i.Code == "legacy-runtime-installed");
        Assert.Contains(report.Issues, i => i.Code == "runtime-plugin-missing");
        Assert.False(Directory.Exists(Path.Combine(_root, "new")));
    }
}
