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
