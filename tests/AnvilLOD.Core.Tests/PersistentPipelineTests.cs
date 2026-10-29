using AnvilLOD.Core.Pipeline;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;

namespace AnvilLOD.Core.Tests;

public sealed class PersistentPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pengulod-pipeline-" + Guid.NewGuid().ToString("N"));
    private string Output(string id) => Path.Combine(_root, id);
    private PersistentPipeline Open(bool reuse = true, CancellationToken ct = default) => new(Path.Combine(_root, "cache"),
        [new("grass", "Grass", Output("grass"), []), new("textures", "Textures", Output("textures"), []),
         new("lod", "LOD", Output("lod"), ["grass", "textures"])], reuse, ct: ct);
    private string Write(string id, string value)
    { Directory.CreateDirectory(Output(id)); File.WriteAllText(Path.Combine(Output(id), "result.bin"), value); return value; }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void FailureInMiddlePreservesAndReusesCompletedStage()
    {
        using (var job = Open())
        {
            job.Run("grass", "g1", () => Write("grass", "grass"));
            Assert.Throws<IOException>(() => job.Run<string>("textures", "t1", () => throw new IOException("fixture")));
            Assert.Equal("Complete", job.Journal.Stages[0].Status);
            Assert.Equal("Failed", job.Journal.Stages[1].Status);
            Assert.Equal("Invalidated", job.Journal.Stages[2].Status);
        }
        using (var job = Open())
        {
            Assert.Equal("grass", job.Run<string>("grass", "g1", () => throw new Exception("Must reuse")));
            Assert.True(job.Journal.Stages[0].Reused);
            job.Run("textures", "t1", () => Write("textures", "textures"));
            job.Run("lod", "l1", () => Write("lod", "lod"));
            Assert.All(job.Journal.Stages, s => Assert.Equal("Complete", s.Status));
        }
    }

    [Fact]
    public void ChangedSettingsInvalidateOnlyDependentStages()
    {
        using (var job = Open())
        {
            job.Run("grass", "g1", () => Write("grass", "grass"));
            job.Run("textures", "t1", () => Write("textures", "textures"));
            job.Run("lod", "l1", () => Write("lod", "lod"));
        }
        using var resumed = Open();
        resumed.Run("grass", "g2", () => Write("grass", "new grass"));
        resumed.Run<string>("textures", "t1", () => throw new Exception("Independent texture stage must be reused"));
        resumed.Run("lod", "l1", () => Write("lod", "new LOD"));
        Assert.False(resumed.Journal.Stages[0].Reused);
        Assert.True(resumed.Journal.Stages[1].Reused);
        Assert.False(resumed.Journal.Stages[2].Reused);
    }

    [Fact]
    public void CorruptOutputIsNeverReusedEvenWithUnchangedMetadata()
    {
        using (var job = Open()) job.Run("grass", "g", () => Write("grass", "original"));
        var path = Path.Combine(Output("grass"), "result.bin"); var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "modified"); File.SetLastWriteTimeUtc(path, time);
        using var resumed = Open();
        resumed.Run("grass", "g", () => Write("grass", "repaired"));
        Assert.False(resumed.Journal.Stages[0].Reused);
        Assert.Equal("repaired", File.ReadAllText(path));
    }

    [Fact]
    public void CancelledStageIsRecordedAndCanResume()
    {
        using var cts = new CancellationTokenSource();
        using (var job = Open(ct: cts.Token))
        {
            job.Run("grass", "g", () => Write("grass", "grass"));
            Assert.Throws<OperationCanceledException>(() => job.Run("textures", "t", () => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); return ""; }));
            Assert.Equal("Cancelled", job.Journal.Stages[1].Status);
        }
        using var next = Open();
        next.Run<string>("grass", "g", () => throw new Exception("Must reuse"));
        next.Run("textures", "t", () => Write("textures", "textures"));
        Assert.Equal("Complete", next.Journal.Stages[1].Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentJobsRespectHostPathIdentityEvenWithDifferentCaches(bool changeCase)
    {
        using var job = Open();
        PipelineStagePlan[] stages = [new("different", "Other", Output(changeCase ? "GRASS" : "grass"), [])];
        string cache = Path.Combine(_root, "other-cache");
        if (OperatingSystem.IsWindows() || !changeCase)
            Assert.Throws<IOException>(() => new PersistentPipeline(cache, stages));
        else
        {
            // A case-sensitive host treats these as two separate output directories.
            using var separate = new PersistentPipeline(cache, stages);
            Assert.NotEqual(job.Folder, separate.Folder);
        }
    }

    [Fact]
    public void StrictInputsDetectChangedAssetBytesWhenTimestampsAndLengthMatch()
    {
        using var fixture = new TextureFixture();
        var before = PipelineInputs.Capture(fixture.Game, [fixture.Output], [fixture.Resources], true);
        string file = Path.Combine(fixture.Data, "textures", "red.dds");
        var time = File.GetLastWriteTimeUtc(file); var bytes = File.ReadAllBytes(file); bytes[^1] ^= 1;
        File.WriteAllBytes(file, bytes); File.SetLastWriteTimeUtc(file, time);
        var after = PipelineInputs.Capture(fixture.Game, [fixture.Output], [fixture.Resources], true);
        Assert.NotEqual(before.Fingerprint, after.Fingerprint);
        Assert.Equal(before.Files, after.Files);
    }

    [Fact]
    public void GeneratedOutputChangesDoNotInvalidateSourceInventory()
    {
        using var fixture = new TextureFixture();
        var before = PipelineInputs.Capture(fixture.Game, [fixture.Output], [fixture.Resources], false);
        Directory.CreateDirectory(fixture.Output); File.WriteAllText(Path.Combine(fixture.Output, "owned.txt"), "new output");
        var after = PipelineInputs.Capture(fixture.Game, [fixture.Output], [fixture.Resources], false);
        Assert.Equal(before.Fingerprint, after.Fingerprint);
    }

    [Theory]
    [InlineData("../escaped")]
    [InlineData("/escaped")]
    [InlineData("nested/../escaped")]
    [InlineData("D:/escaped")]
    public void CheckpointCannotReferenceFilesOutsideOutput(string relative)
    {
        Assert.Throws<InvalidDataException>(() => OutputSnapshot.Resolve(_root, relative));
    }
}

public sealed class GenerationPipelineTests
{
    [Fact]
    public void FailedTexturesResumeTheSameCombinedJobWithoutRebuildingGrass()
    {
        using var fixture = new GrassFixture();
        var recipe = File.ReadAllText(fixture.Textures.RuleFile);
        File.WriteAllText(fixture.Textures.RuleFile, TextureFixture.Tile(@"textures\missing.dds", @"textures\lod\atlas.dds", 0));
        var request = new GenerationPipelineRequest(fixture.Request, fixture.Textures.Request);
        var cache = Path.Combine(fixture.Root, "Cache");
        var first = GenerationPipeline.Run(request, cache);
        Assert.False(first.Success); Assert.Equal("textures", first.FailedStage);
        Assert.NotNull(first.Grass); Assert.NotNull(first.Textures);
        string grass = File.ReadAllText(Path.Combine(fixture.Output, GrassGenerationManifest.FileName));
        File.WriteAllText(fixture.Textures.RuleFile, recipe);
        var resumed = GenerationPipeline.Run(request, cache);
        Assert.True(resumed.Success, resumed.Error);
        Assert.True(resumed.Journal.Stages.Single(s => s.Plan.Id == "grass").Reused);
        Assert.False(resumed.Journal.Stages.Single(s => s.Plan.Id == "textures").Reused);
        Assert.Equal(grass, File.ReadAllText(Path.Combine(fixture.Output, GrassGenerationManifest.FileName)));
        var third = GenerationPipeline.Run(request, cache);
        Assert.True(third.Success); Assert.All(third.Journal.Stages, s => Assert.True(s.Reused));
    }

    [Fact]
    public void FailedLodDoesNotChangePreviouslyPublishedOutput()
    {
        using var fixture = new TextureFixture();
        string output = Path.Combine(fixture.Root, "LOD"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "AnvilLOD.log"), "Previous working LOD");
        var request = new GenerationPipelineRequest(Lod: new(fixture.Game, new(), output, Generate: true));
        var result = GenerationPipeline.Run(request, Path.Combine(fixture.Root, "Cache"));
        Assert.False(result.Success); Assert.Equal("lod", result.FailedStage);
        Assert.Equal("Previous working LOD", File.ReadAllText(Path.Combine(output, "AnvilLOD.log")));
        Assert.True(File.Exists(Path.Combine(result.JobFolder, "scan-report.json")));
        Assert.Empty(Directory.GetDirectories(fixture.Root, ".pengulod-build-*"));
    }

    [Fact]
    public void PublicationKeepsPreviousOutputAndFailedStagingNeverTouchesIt()
    {
        using var fixture = new TextureFixture();
        string output = Path.Combine(fixture.Root, "Atomic"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "old.bin"), "old");
        using (var stage = new StagedOutput(output, seedPrevious: false))
            File.WriteAllText(Path.Combine(stage.Payload, "new.bin"), "abandoned");
        Assert.Equal("old", File.ReadAllText(Path.Combine(output, "old.bin")));
        using (var stage = new StagedOutput(output, seedPrevious: true))
        {
            File.WriteAllText(Path.Combine(stage.Payload, "new.bin"), "new");
            stage.Commit();
            Assert.Equal("old", File.ReadAllText(Path.Combine(stage.PreviousOutput!, "old.bin")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(output, "new.bin")));
        }
    }

    [Fact]
    public void ConcurrentDestinationEditPreventsPublication()
    {
        using var fixture = new TextureFixture();
        string output = Path.Combine(fixture.Root, "Atomic"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "old.bin"), "old");
        using var stage = new StagedOutput(output, seedPrevious: false);
        File.WriteAllText(Path.Combine(stage.Payload, "new.bin"), "new");
        File.WriteAllText(Path.Combine(output, "edited.bin"), "external edit");
        Assert.Throws<IOException>(() => stage.Commit());
        Assert.True(File.Exists(Path.Combine(output, "edited.bin")));
        Assert.False(File.Exists(Path.Combine(output, "new.bin")));
    }
}
