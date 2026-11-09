using System.Text.Json;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Meshes.Authoring;
using AnvilLOD.Meshes.Nif;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;

namespace AnvilLOD.Core.Tests;

public sealed class OutputManagementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pengulod-output-tests-" + Guid.NewGuid().ToString("N"));
    private string Folder(string name) => Path.Combine(_root, name);
    private string Put(string folder, string path, byte[] bytes)
    {
        string file = Path.Combine(Folder(folder), path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes); return file;
    }
    private string Stage(string folder, string stage, string path, string value = "fixture", OutputDependency[]? dependencies = null)
    {
        Put(folder, path, System.Text.Encoding.UTF8.GetBytes(value));
        OutputSetReceipt.Record(Folder(folder), stage, "source", OutputSnapshot.Capture(Folder(folder)), dependencies);
        return Folder(folder);
    }
    private static byte[] Mesh(string layer = "objects", int parts = 1) => LodNifWriter.Write("AL:" + layer + ":4",
        Enumerable.Range(0, parts).Select(i => TextureFixture.Card("card" + i, @"textures\red.dds")).ToArray());
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void MeasuresAllSavedBlocksAndSeparatesMissingCountsFromEmptyBlocks()
    {
        Put("lod", "meshes/terrain/World/objects/World.4.0.0.bto", Mesh("tree3d", 2));
        Put("lod", "meshes/terrain/Other/objects/Other.8.0.0.WIN.bto", Mesh("grass"));
        Put("lod", "meshes/terrain/World/objects/World.4.4.0.bto", Mesh(parts: 0));
        Put("lod", "meshes/terrain/World/objects/World.4.8.0.bto", [1, 2, 3]);
        var report = OutputCostReport.Analyze(Folder("lod"));
        Assert.Equal(4, report.Blocks.Count); Assert.Equal(6, report.Groups.Sum(g => g.Triangles)); Assert.Equal(3, report.Groups.Sum(g => g.MeshGroups));
        Assert.Contains(report.Groups, g => g.World == "Other" && g.Layer == "grass" && g.Season == "WIN");
        Assert.Null(Assert.Single(report.Blocks.Where(b => b.File.EndsWith("8.0.bto"))).Layers);
        Assert.Empty(Assert.Single(report.Blocks.Where(b => b.File.EndsWith("4.0.bto"))).Layers!);
        Assert.Equal(report.Blocks.OrderBy(b => b.File, StringComparer.Ordinal), report.Blocks);
        string path = Folder("report.json"); report.Write(path);
        Assert.Equal(report.Groups, OutputCostReport.Read(path).Groups);
    }

    [Fact]
    public void GeometryReaderRejectsTruncatedBuffers()
    {
        var bytes = Mesh(); var nif = NifFile.Read(bytes); var shape = nif.Blocks.First(b => b.Type == "BSTriShape");
        // ObjectNet (12), AVObject (60), shape header (40): data size at byte 112.
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, shape.Offset + 112);
        Assert.Throws<InvalidDataException>(() => NifCostReader.Read(bytes));
    }

    [Fact]
    public void ComparisonsOnlySumCommonPathsAndReportSettingsAndUnknownMeasurements()
    {
        var before = new OutputCostReport { Blocks = [new("a", "W", 4, "base", 10, [new("objects", 100, 80, 10)]), new("removed", "Other", 4, "base", 99, null)], Settings = new() { ["grass"] = "0.05" } };
        var after = new OutputCostReport { Blocks = [new("a", "W", 4, "base", 8, [new("objects", 60, 50, 4)]), new("added", "W", 8, "base", 500, null)], Settings = new() { ["grass"] = "0.06" } };
        var delta = OutputCostReport.Compare(before, after);
        Assert.Equal(-40, Assert.Single(delta.CommonBlocks).Triangles); Assert.Equal(-6, delta.CommonBlocks[0].MeshGroups);
        Assert.Single(delta.Settings); Assert.Single(delta.AddedBlocks); Assert.Single(delta.RemovedBlocks); Assert.NotEmpty(delta.Warnings);
        after.Blocks[0] = after.Blocks[0] with { Layers = null };
        Assert.Null(OutputCostReport.Compare(before, after).CommonBlocks[0].Triangles);
    }

    [Fact]
    public void MatchingDependenciesPublishTogetherAndKeepPreviousPackage()
    {
        string textures = Stage("tex", "textures", "textures/lod/tree.dds", "texture");
        var dependency = new OutputDependency("textures", OutputSetReceipt.Read(textures).Stages[0].Files);
        string lod = Stage("lod", "lod", "meshes/terrain/World/objects/World.4.0.0.bto", dependencies: [dependency]);
        Assert.False(OutputManagement.Verify([lod]).Valid); Assert.True(OutputManagement.Verify([lod, textures]).Valid);
        var plan = OutputManagement.Plan([textures, lod], Folder("package")); Assert.True(plan.CanPublish, plan.Summary());
        Assert.Null(OutputManagement.Publish(plan)); Assert.True(OutputManagement.Verify([Folder("package")]).Valid);
        string terrain = Stage("terrain", "terrain", "meshes/terrain/World/World.4.0.0.btr");
        var update = OutputManagement.Plan([terrain], Folder("package")); Assert.True(update.CanPublish, update.Summary());
        string? previous = OutputManagement.Publish(update);
        Assert.True(Directory.Exists(previous)); Assert.Equal(3, OutputSetReceipt.Read(Folder("package")).Stages.Count);
        Assert.Equal(2, OutputSetReceipt.Read(previous!).Stages.Count);
    }

    [Fact]
    public void ChangedTexturesRequireDependentLodRegeneration()
    {
        string textures = Stage("tex", "textures", "textures/lod/tree.dds", "old");
        string lod = Stage("lod", "lod", "AnvilLOD.esm", dependencies: [new("textures", OutputSetReceipt.Read(textures).Stages[0].Files)]);
        Stage("tex", "textures", "textures/lod/tree.dds", "new");
        var plan = OutputManagement.Plan([textures, lod], Folder("package"));
        Assert.False(plan.CanPublish); Assert.Contains(plan.Errors, s => s.Contains("matching textures"));
        Assert.Throws<InvalidDataException>(() => OutputManagement.Publish(plan)); Assert.False(Directory.Exists(Folder("package")));
    }

    [Fact]
    public void PartialWorldReplacementRequiresExpandedGenerationScope()
    {
        string target = Stage("package", "lod", "meshes/terrain/Other/objects/Other.16.0.0.bto");
        string incoming = Stage("new", "lod", "meshes/terrain/World/objects/World.4.0.0.bto");
        Assert.False(OutputManagement.Plan([incoming], target).CanPublish);
        var request = new GenerationPipelineRequest(Lod: new(new(), new(Worldspaces: ["World"]), target, Levels: [LodLevel.Lod4]));
        var scope = GenerationScope.Review(request);
        Assert.True(scope.RequiresExpansion); Assert.Contains("Other", scope.Request.Lod!.Scan.Worldspaces!);
        Assert.Contains(LodLevel.Lod16, scope.Request.Lod.Levels!); Assert.Single(request.Lod!.Scan.Worldspaces!);
        Assert.Throws<InvalidDataException>(() => GenerationPipeline.Run(request, Folder("cache")));
        Assert.False(Directory.Exists(Folder("cache")));
    }

    [Fact]
    public void CancelledOrStalePlansLeaveDestinationUntouched()
    {
        string target = Stage("package", "lod", "AnvilLOD.esm", "old");
        string incoming = Stage("new", "lod", "AnvilLOD.esm", "new");
        var before = OutputSnapshot.Capture(target); var plan = OutputManagement.Plan([incoming], target); Assert.True(plan.CanPublish);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => OutputManagement.Publish(plan, ct: cancel.Token)); before.Verify(target);
        Stage("new", "lod", "AnvilLOD.esm", "changed");
        Assert.Throws<IOException>(() => OutputManagement.Publish(plan)); before.Verify(target);
        Assert.Empty(Directory.GetDirectories(_root, ".pengulod-build-*"));
    }

    [Fact]
    public void RefusesUnownedFilesAndEscapingReceiptPaths()
    {
        string incoming = Stage("new", "lod", "AnvilLOD.esm");
        Put("package", "user.txt", [1]); Assert.False(OutputManagement.Plan([incoming], Folder("package")).CanPublish);
        var receipt = OutputSetReceipt.Read(incoming);
        receipt.Stages[0].Files.Add("../outside.txt", new string('A', 64)); receipt.Write(incoming);
        Assert.False(OutputManagement.Verify([incoming]).Valid); Assert.False(OutputManagement.Plan([incoming], Folder("safe")).CanPublish);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(Folder("package"), "user.txt")));
    }

    [Fact]
    public void CancellationDuringPreparationKeepsThePreviousPackage()
    {
        string target = Stage("package", "lod", "AnvilLOD.esm", "old");
        string incoming = Stage("new", "lod", "AnvilLOD.esm", "new");
        var before = OutputSnapshot.Capture(target); var plan = OutputManagement.Plan([incoming], target);
        using var cancel = new CancellationTokenSource();
        var progress = new InlineProgress(message => { if (message.StartsWith("Preparing package:")) cancel.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => OutputManagement.Publish(plan, progress, cancel.Token));
        before.Verify(target); Assert.Empty(Directory.GetDirectories(_root, ".pengulod-build-*"));
    }

    [Fact]
    public void EqualWorldAndLevelListsCannotHideAMissingWorldLevelPair()
    {
        Stage("package", "lod", "meshes/terrain/A/objects/A.4.0.0.bto");
        Stage("package", "lod", "meshes/terrain/B/objects/B.8.0.0.bto");
        Stage("new", "lod", "meshes/terrain/A/objects/A.8.0.0.bto");
        Stage("new", "lod", "meshes/terrain/B/objects/B.4.0.0.bto");
        Assert.False(OutputManagement.Plan([Folder("new")], Folder("package")).CanPublish);
    }

    [Fact]
    public void LegacyReportsAreNotMistakenForEmptyMeasuredOutput()
    {
        Directory.CreateDirectory(_root); string report = Folder("legacy.json");
        File.WriteAllText(report, "{\"generation\":{\"Triangles\":42}}");
        Assert.Throws<InvalidDataException>(() => OutputCostReport.Read(report));
    }

    [Fact]
    public void ReceiptVerificationRejectsIncompleteAndConflictingOutputs()
    {
        string a = Stage("tex", "textures", "textures/a.dds");
        OutputSetReceipt.Record(a, "textures", "source", OutputSnapshot.Capture(a), complete: false);
        Assert.False(OutputManagement.Verify([a]).Valid);
        string b = Stage("lod", "lod", "textures/a.dds", "other");
        Assert.Contains(OutputManagement.Verify([a, b]).Findings, f => f.Message.Contains("Conflicting"));
    }

    [Fact]
    public void SymlinksAreRejectedWithoutReadingOrChangingTheirTargets()
    {
        if (OperatingSystem.IsWindows()) return;
        string source = Stage("lod", "lod", "AnvilLOD.esm");
        File.CreateSymbolicLink(Path.Combine(source, "link"), "/etc/passwd");
        Assert.False(OutputManagement.Verify([source]).Valid);
        Assert.False(OutputManagement.Plan([source], Folder("package")).CanPublish);
        Assert.Throws<IOException>(() => OutputCostReport.Analyze(source));
    }

    [Fact]
    public void PipelineReceiptsIncludeGeneratedTextureDependenciesAndRetainedCosts()
    {
        using var f = new TerrainFixture();
        string lod = Path.Combine(f.Root, "lod"), cache = Path.Combine(f.Root, "cache");
        var request = new GenerationPipelineRequest(Textures: f.Textures.Request,
            Lod: new(f.Game, new(Worldspaces: ["FixtureWorld"], TreeLod: false, GridObjects: false, DynamicLod: false),
                lod, Generate: true, GrassLod: false, SkseDll: SkseDllChoice.None, CleanOutput: false), Preflight: false);
        var first = GenerationPipeline.Run(request, cache); Assert.True(first.Success, first.Error);
        var receipt = OutputSetReceipt.Read(lod); Assert.Contains(receipt.Stages[0].Dependencies, d => d.Stage == "textures");
        Assert.True(OutputManagement.Verify([lod, f.Textures.Output]).Valid);
        Assert.True(File.Exists(Path.Combine(lod, OutputCostReport.FileName)));
        var second = GenerationPipeline.Run(request, cache); Assert.True(second.Success, second.Error);
        Assert.All(second.Journal.Stages, s => Assert.True(s.Reused));
    }

    private sealed class InlineProgress(Action<string> action) : IProgress<string>
    { public void Report(string value) => action(value); }

    [Fact]
    public void CaseChangedDeploymentCanBeVerifiedAndCopiedWithoutChangingSources()
    {
        if (OperatingSystem.IsWindows()) return;
        string stage = Stage("case", "lod", "AnvilLOD.esm");
        File.Move(Path.Combine(stage, "AnvilLOD.esm"), Path.Combine(stage, "anvillod.esm"));
        File.Move(Path.Combine(stage, OutputSetReceipt.FileName), Path.Combine(stage, OutputSetReceipt.FileName.ToLowerInvariant()));
        Assert.True(OutputManagement.Verify([stage]).Valid);
        var plan = OutputManagement.Plan([stage], Folder("package") + Path.DirectorySeparatorChar);
        Assert.True(plan.CanPublish, plan.Summary()); OutputManagement.Publish(plan);
        Assert.True(OutputManagement.Verify([Folder("package")]).Valid);
        Assert.True(File.Exists(Path.Combine(stage, "anvillod.esm")));
    }

    [Fact]
    public void PreviewCannotWriteItsPlanInsideAnInputOrTheDestination()
    {
        string source = Stage("source", "lod", "AnvilLOD.esm");
        var plan = OutputManagement.Plan([source], Folder("package"));
        Assert.Throws<ArgumentException>(() => plan.Write(Path.Combine(source, "plan.json")));
        Assert.Throws<ArgumentException>(() => plan.Write(Path.Combine(plan.Destination, "plan.json")));
        Assert.False(Directory.Exists(plan.Destination));
    }

    [Fact]
    public void PackageUpdatePreservesDestinationModManagerMetadataAndDetectsChanges()
    {
        string source = Stage("source", "lod", "AnvilLOD.esm"), destination = Folder("package");
        Directory.CreateDirectory(destination);
        string metadata = Path.Combine(destination, "meta.ini");
        File.WriteAllText(metadata, "installation metadata");
        var plan = OutputManagement.Plan([source], destination);
        File.WriteAllText(metadata, "updated installation metadata");
        Assert.Throws<IOException>(() => OutputManagement.Publish(plan));
        OutputManagement.Publish(OutputManagement.Plan([source], destination));
        Assert.Equal("updated installation metadata", File.ReadAllText(metadata));
        Assert.True(OutputManagement.Verify([destination]).Valid);
    }

    [Fact]
    public void ReportExportRejectsSymlinkToInputFile()
    {
        if (OperatingSystem.IsWindows()) return;
        string source = Stage("source", "lod", "AnvilLOD.esm");
        string link = Path.Combine(Folder("source"), "export-link.json");
        File.CreateSymbolicLink(link, Path.Combine(source, "AnvilLOD.esm"));
        Assert.Throws<IOException>(() => OutputManagement.ValidateReportPath(link, [source]));
    }
}
