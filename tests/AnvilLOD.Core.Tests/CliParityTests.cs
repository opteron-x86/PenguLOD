using System.Text.Json;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Desktop.Models;
using AnvilLOD.Desktop.Services;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using Xunit;

namespace AnvilLOD.Core.Tests;

[CollectionDefinition("CLI",DisableParallelization=true)]public class CliCollection;
[Collection("CLI")]
public class CliParityTests
{
    [Fact]
    public void TreeGeometryTargetRoundTripsThroughSettingsAndRejectsInvalidCliValues()
    {
        using var f = new TextureFixture();
        var settings = new DesktopSettings { SourceMode = "Data folder / Vortex", GameFolder = f.Data, PluginsFile = f.Plugins,
            OutputFolder = f.Output, PipelineGrass = false, PipelineTextures = false, PipelineTerrain = false, PipelineLod = true };
        settings.Options["tree3d"] = JsonSerializer.SerializeToElement(true);
        settings.Options["treeFoliageTarget"] = JsonSerializer.SerializeToElement(80m);
        string preset = Path.Combine(f.Root, "preset.json"), request = Path.Combine(f.Root, "request.json");
        SettingsStore.Write(preset, settings);
        Assert.Equal(0, AnvilLOD.Cli.Program.Main(["export-job", "--settings", preset, "--output", request]));
        var exported = JsonSerializer.Deserialize<GenerationPipelineRequest>(File.ReadAllText(request))!;
        Assert.Equal(.8f, exported.Lod!.Scan.Tree3D!.FoliageTargetRatio);
        foreach (string value in new[] { "NaN", "0", "101" })
            Assert.Equal(2, AnvilLOD.Cli.Program.Main(["scan", "--data", f.Data, "--plugins", f.Plugins,
                "--tree-3d", "--tree-foliage-target", value]));
    }

    [Fact]
    public void RepeatedResourceFlagsSelectLaterPacksInScanReports()
    {
        using var f = new TerrainFixture();
        string first = Path.Combine(f.Root, "first"), second = Path.Combine(f.Root, "second"), report = Path.Combine(f.Root, "resources.json");
        const string file = @"Rules\DynDOLOD_SSE_all.ini";
        TextureFixture.Put(first, file, System.Text.Encoding.UTF8.GetBytes("[Skyrim LODGen]\n"));
        TextureFixture.Put(second, file, System.Text.Encoding.UTF8.GetBytes("[Skyrim LODGen]\n;later\n"));
        Assert.Equal(0, AnvilLOD.Cli.Program.Main(["scan", "--data", f.Textures.Data, "--plugins", f.Textures.Plugins,
            "--resources", first, "--resources", second, "--report", report, "--no-match-textures", "--cache-folder", Path.Combine(f.Root, "cache")]));
        using var result = JsonDocument.Parse(File.ReadAllText(report));
        var row = Assert.Single(result.RootElement.GetProperty("resources").GetProperty("Files").EnumerateArray());
        Assert.Contains("second", row.GetProperty("Origin").GetString());
        Assert.Single(row.GetProperty("Overridden").EnumerateArray());
    }

    [Fact]
    public void ScanHonorsTheRequestAndNeverPublishesEvenWhenGenerateIsSet()
    {
        using var f = new TerrainFixture();
        string request = Path.Combine(f.Root, "request.json"), report = Path.Combine(f.Root, "scan.json"), output = Path.Combine(f.Root, "untouched");
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "sentinel.txt"), "original");
        var lod = new ScanRequest(f.Game, new(Worldspaces: ["FixtureWorld"], TreeLod: false, DynamicLod: false, GridObjects: false),
            output, Generate: true, GrassLod: false, RemoveBuried: false, RemoveUnderwater: false,
            AutomaticObjects: new(true, NearBudget: 100, MiddleBudget: 80, FarBudget: 40));
        File.WriteAllText(request, JsonSerializer.Serialize(new GenerationPipelineRequest(Lod: lod)));
        Assert.Equal(0, AnvilLOD.Cli.Program.Main(["scan", "--request", request, "--cache-folder", Path.Combine(f.Root, "cache"), "--report", report]));
        using var result = JsonDocument.Parse(File.ReadAllText(report));
        Assert.Single(result.RootElement.GetProperty("grids").EnumerateObject());
        Assert.True(result.RootElement.GetProperty("grids").TryGetProperty("FixtureWorld", out _));
        Assert.Equal(JsonValueKind.Array, result.RootElement.GetProperty("automaticObjects").ValueKind);
        Assert.Equal("original", File.ReadAllText(Path.Combine(output, "sentinel.txt")));
        Assert.Single(Directory.GetFiles(output));
        Assert.Equal(2, AnvilLOD.Cli.Program.Main(["scan", "--request", request, "--settings", request]));
    }

    [Fact]
    public void TerrainMaterialControlsRoundTripThroughPresetAndInvalidatePipelineReuse()
    {
        using var f=new TerrainFixture();
        var settings=new DesktopSettings {SourceMode="Data folder / Vortex",GameFolder=f.Textures.Data,PluginsFile=f.Textures.Plugins,
            TerrainOutput=f.Output,Worldspaces="FixtureWorld",TerrainTextureSize=64,TerrainNormalStrength=.5m,TerrainPbr=true,TerrainPbrTwins=true,
            TerrainPbrScale=.8m,TerrainPbrGamma=1.2m,TerrainAo=.4m,
            PipelineGrass=false,PipelineTextures=false,PipelineTerrain=true,PipelineLod=false,CacheFolder=Path.Combine(f.Root,"cache")};
        string preset=Path.Combine(f.Root,"preset.json"),request=Path.Combine(f.Root,"job.json"),report=Path.Combine(f.Root,"run.json");
        SettingsStore.Write(preset,settings);
        Assert.Equal(0,AnvilLOD.Cli.Program.Main(["export-job","--settings",preset,"--output",request]));
        var exported=JsonSerializer.Deserialize<GenerationPipelineRequest>(File.ReadAllText(request))!;
        Assert.Equal(new TerrainMaterialSettings(.5f,true,true,.8f,1.2f,.4f),exported.Terrain!.Materials);
        Assert.Equal(0,AnvilLOD.Cli.Program.Main(["terrain","--request",request,"--cache-folder",settings.CacheFolder,"--report",report]));
        Assert.True(GenerationPipeline.Run(exported,settings.CacheFolder).Journal.Stages.Single().Reused);
        var changed=GenerationPipeline.Run(exported with {Terrain=exported.Terrain with {Materials=exported.Terrain.Materials! with {NormalStrength=0}}},settings.CacheFolder);
        Assert.True(changed.Success,changed.Error);Assert.False(changed.Journal.Stages.Single().Reused);
    }
    [Fact]
    public void DesktopPresetExportsRunsAndResumesThroughCliWithoutAvalonia()
    {
        using var f=new TextureFixture();
        var settings=new DesktopSettings {SourceMode="Data folder / Vortex",GameFolder=f.Data,PluginsFile=f.Plugins,
            TextureGenOutput=f.Output,TextureGenStitched=false,TextureGenTrees=true,TextureGenBaseSize=64,TextureGenMaxSize=64,
            PipelineGrass=false,PipelineTextures=true,PipelineTerrain=false,PipelineLod=false,CacheFolder=Path.Combine(f.Root,"cache")};
        string preset=Path.Combine(f.Root,"preset.json"),request=Path.Combine(f.Root,"request.json"),report=Path.Combine(f.Root,"run.json");
        SettingsStore.Write(preset,settings);
        Assert.Equal(0,AnvilLOD.Cli.Program.Main(["export-job","--settings",preset,"--output",request]));
        Assert.Equal(JsonSerializer.Serialize(RequestFactory.Pipeline(settings),PersistentPipeline.Json),File.ReadAllText(request));
        Assert.Equal(0,AnvilLOD.Cli.Program.Main(["texgen","--settings",preset,"--report",report]));
        Assert.True(TextureGenerationManifest.Read(f.Output,true)!.Complete);
        Assert.Equal(0,AnvilLOD.Cli.Program.Main(["pipeline","--request",request,"--cache-folder",settings.CacheFolder,"--report",report]));
        using var result=JsonDocument.Parse(File.ReadAllText(report));
        Assert.True(result.RootElement.GetProperty("Journal").GetProperty("Stages")[0].GetProperty("Reused").GetBoolean());
        var folder=result.RootElement.GetProperty("JobFolder").GetString()!;
        Assert.Equal(0,AnvilLOD.Cli.Program.Main(["job-status","--job-folder",folder]));
        Assert.DoesNotContain(typeof(RequestFactory).Assembly.GetReferencedAssemblies(),a=>a.Name!.StartsWith("Avalonia"));
    }
    [Fact]
    public void UndefinedStageAndConflictingInputFormatsAreErrors()
    {
        using var f=new TextureFixture();string request=Path.Combine(f.Root,"request.json");
        File.WriteAllText(request,JsonSerializer.Serialize(new GenerationPipelineRequest(Textures:f.Request),PersistentPipeline.Json));
        Assert.Equal(2,AnvilLOD.Cli.Program.Main(["terrain","--request",request]));
        Assert.Equal(2,AnvilLOD.Cli.Program.Main(["pipeline","--request",request,"--settings",request]));
        Assert.False(Directory.Exists(f.Output));
    }
}
