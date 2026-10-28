using System.Text.Json;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;

namespace AnvilLOD.Core.Tests;

public sealed class PipelineDiagnosticTests
{
    private static GenerationPipelineRequest Request(TextureFixture f) => new(Lod:new(f.Game,new(TreeLod:false,DynamicLod:true,GridObjects:false,RenderTreeBillboards:false),OutputFolder:Path.Combine(f.Root,"lod"),SkseDll:SkseDllChoice.None));
    [Fact]
    public void RuntimeDependencyAndWinningAssetAreReportedWithoutAbsolutePathsOrOutputWrites()
    {
        using var f=new TextureFixture();var report=PipelineDiagnostics.Run(Request(f),inspectAsset:@"textures\red.dds");
        Assert.True(report.CanGenerate);Assert.Contains(report.Issues,i=>i.Code=="runtime-plugin-missing");
        var origin=Assert.Single(report.Issues,i=>i.Code=="asset-precedence");Assert.Equal("Loose layer: Data",origin.Source);
        var json=JsonSerializer.Serialize(report);Assert.DoesNotContain(f.Root,json);Assert.False(Directory.Exists(Path.Combine(f.Root,"lod")));
    }
    [Fact]
    public void InvalidRuntimeModuleIsABlockingFinding()
    {
        using var f=new TextureFixture();TextureFixture.Put(f.Data,@"skse\plugins\AnvilLOD.dll","not a PE library"u8.ToArray());
        var report=PipelineDiagnostics.Run(Request(f));Assert.False(report.CanGenerate);Assert.Contains(report.Issues,i=>i.Code=="runtime-plugin-format"&&i.Severity=="Error");
    }
    [Fact]
    public void StaleGrassStopsPipelineBeforeReplacingOutput()
    {
        using var f=new GrassFixture();GrassGeneration.Run(f.Request);
        string manifest=Path.Combine(f.Output,GrassGenerationManifest.FileName);
        var m=JsonSerializer.Deserialize<GrassGenerationManifest>(File.ReadAllText(manifest))!;m.LoadOrderFingerprint="outdated";File.WriteAllText(manifest,JsonSerializer.Serialize(m));
        var request=Request(f.Textures);request=request with{Lod=request.Lod! with{GeneratedGrassFolder=f.Output}};
        Directory.CreateDirectory(request.Lod!.OutputFolder!);string log=Path.Combine(request.Lod.OutputFolder!,"AnvilLOD.log");File.WriteAllText(log,"known-good");
        var result=GenerationPipeline.Run(request,Path.Combine(f.Root,"cache"));Assert.False(result.Success);Assert.Equal("preflight",result.FailedStage);
        Assert.Contains(result.Diagnostics!.Issues,i=>i.Code=="stale-grass-cache"&&i.Severity=="Error");Assert.Equal("known-good",File.ReadAllText(log));
        var rebuilding=PipelineDiagnostics.Run(request with{Grass=f.Request});Assert.DoesNotContain(rebuilding.Issues,i=>i.Code=="stale-grass-cache"&&i.Severity=="Error");
    }
    [Fact]
    public void DeepScanReportsMissingWorldspacesAndDoesNotGenerateAnything()
    {
        using var f=new TextureFixture();var report=PipelineDiagnostics.Run(Request(f),deepScan:true);
        Assert.False(report.CanGenerate);Assert.Contains(report.Issues,i=>i.Code=="no-worldspaces");Assert.False(Directory.Exists(Path.Combine(f.Root,"lod")));
        Assert.True(report.MeasuredSeconds>=0);Assert.DoesNotContain(report.Estimates,e=>e.Unit=="seconds");
    }
}
