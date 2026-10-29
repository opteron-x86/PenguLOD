using System.Diagnostics;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins;
using AnvilLOD.Testing;
using Mutagen.Bethesda;

namespace AnvilLOD.Core.Tests;

public sealed class GrassGenerationTests
{
    private sealed class Progress(Action<string> report) : IProgress<string> { public void Report(string s) => report(s); }

    [Fact]
    public void AutoRejectionHonorsNgioAndArgumentsKeepSpacesAsSingleValues()
    {
        using var f = new GrassFixture();
        var input = new GrassGenerationInput("/data with spaces", "/plugins list.txt", "/ini folder", "/GrassControl.ini", "source", "all", "hash");
        var args = GrassGeneration.Arguments(f.Request, input, f.Output);
        Assert.DoesNotContain("--reject", args); Assert.Contains("/data with spaces", args);
        Assert.Contains("--write-empty-cells", args);
        var cpu = GrassGeneration.Arguments(f.Request with { Rejection = "cpu" }, input, f.Output);
        Assert.Equal("cpu", cpu[cpu.IndexOf("--reject") + 1]);
    }

    [Fact]
    public void ValidatesCompleteFormatAndRejectsUnsafeBlocksTruncationAndTrailingData()
    {
        Assert.Equal(new GrassCacheInfo(1, 1), GrassCacheValidation.Inspect(new MemoryStream(GrassFixture.Cache())));
        Assert.Equal(new GrassCacheInfo(0, 0), GrassCacheValidation.Inspect(new MemoryStream(new byte[4])));
        Assert.Throws<InvalidDataException>(() => GrassCacheValidation.Inspect(new MemoryStream(GrassFixture.Cache(8193))));
        Assert.Throws<InvalidDataException>(() => GrassCacheValidation.Inspect(new MemoryStream(GrassFixture.Cache(1, 12))));
        Assert.Throws<EndOfStreamException>(() => GrassCacheValidation.Inspect(new MemoryStream(GrassFixture.Cache()[..^1])));
        Assert.Throws<InvalidDataException>(() => GrassCacheValidation.Inspect(new MemoryStream([.. GrassFixture.Cache(), 7])));
    }

    [Fact]
    public void PublishesFreshCacheKeepsPreviousAndDoesNotMixOldInstalledCells()
    {
        using var f = new GrassFixture(); using var limits = RunLimits.Enter(2);
        var first = GrassGeneration.Run(f.Request);
        Assert.Equal(2, first.Files); Assert.Equal(1, first.Instances); Assert.Equal(1, first.EmptyFiles);
        var manifest = GrassGenerationManifest.Read(f.Output, true)!; manifest.Verify(f.Output);
        File.WriteAllText(Path.Combine(f.Output, "meta.ini"), "[General]\nnotes=keep me");
        var next = GrassGeneration.Run(f.Request);
        Assert.NotNull(next.PreviousOutput); Assert.True(File.Exists(Path.Combine(next.PreviousOutput!, GrassGenerationManifest.FileName)));
        Assert.Contains("keep me", File.ReadAllText(Path.Combine(f.Output, "meta.ini")));
        TextureFixture.Put(f.Textures.Data, @"grass\Tamrielx0099y0099.cgid", GrassFixture.Cache());
        using var game = GameContext.Open(f.Textures.Game);
        var assets = game.BuildAssets();
        var textures = TextureGeneration.Run(f.Textures.Request);
        Assert.True(textures.Published);
        TextureGenerationManifest.Read(f.Textures.Output, true)!.Attach(assets, f.Textures.Output);
        manifest.Attach(assets, f.Output, game);
        Assert.False(assets.Exists(@"grass\Tamrielx0099y0099.cgid"));
        Assert.False(assets.TryOpen(@"grass\Tamrielx0099y0099.cgid", out _));
        Assert.Null(assets.Fingerprint(@"grass\Tamrielx0099y0099.cgid"));
        Assert.Equal(2, assets.EnumeratePaths(@"grass\").Count());
        Assert.Equal("AnvilLOD texture generation", assets.SourceOf(@"textures\lod\atlas.dds"));
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("corrupt")]
    public void FailureOrInvalidCachePreservesPreviousOutput(string behavior)
    {
        using var f = new GrassFixture(); using var limits = RunLimits.Enter(2);
        GrassGeneration.Run(f.Request);
        var before = File.ReadAllText(Path.Combine(f.Output, GrassGenerationManifest.FileName));
        f.SetBehavior(behavior);
        Assert.ThrowsAny<Exception>(() => GrassGeneration.Run(f.Request));
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.Output, GrassGenerationManifest.FileName)));
        Assert.Empty(Directory.EnumerateDirectories(f.Root, ".anvillod-grass-build-*"));
    }

    [Fact]
    public void CancellationStopsChildProcessAndLeavesPreviousCacheIntact()
    {
        using var f = new GrassFixture(); using var limits = RunLimits.Enter(2);
        GrassGeneration.Run(f.Request); f.SetBehavior("wait");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watch = Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => GrassGeneration.Run(f.Request,
            new Progress(line => { if (line == "FAKE_READY") cancellation.Cancel(); }), cancellation.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8));
        GrassGenerationManifest.Read(f.Output, true)!.Verify(f.Output);
        Assert.Empty(Directory.EnumerateDirectories(f.Root, ".anvillod-grass-build-*"));
    }

    [Fact]
    public void RejectsUnownedOrModifiedOutputAndPreviewOverFullCache()
    {
        using var f = new GrassFixture(); using var limits = RunLimits.Enter(2);
        Directory.CreateDirectory(f.Output); File.WriteAllText(Path.Combine(f.Output, "mine.txt"), "keep");
        Assert.ThrowsAny<Exception>(() => GrassGeneration.Run(f.Request));
        File.Delete(Path.Combine(f.Output, "mine.txt")); GrassGeneration.Run(f.Request);
        string manifest = File.ReadAllText(Path.Combine(f.Output, GrassGenerationManifest.FileName));
        // Existing-output protection is checked by the cancellable worker under its output lock.
        Assert.Throws<ArgumentException>(() => GrassGeneration.Run(f.Request with { Preview = true, Worldspaces = "Tamriel" }));
        Assert.Equal(manifest, File.ReadAllText(Path.Combine(f.Output, GrassGenerationManifest.FileName)));
        Assert.Empty(Directory.GetDirectories(f.Root, ".anvillod-grass-build-*"));
        File.AppendAllText(Path.Combine(f.Output, "grass", "Tamrielx0000y0000.cgid"), "changed");
        Assert.Throws<IOException>(() => GrassGeneration.Run(f.Request));
    }

    [Fact]
    public void PreviewCannotBeUsedForLodAndGrassOutputCannotBeClearedByLodOrTextures()
    {
        using var f = new GrassFixture(); using var limits = RunLimits.Enter(2);
        GrassGeneration.Run(f.Request);
        Assert.Throws<ArgumentException>(() => ScanPipeline.Run(new ScanRequest(f.Textures.Game, new ScanOptions(), OutputFolder: f.Output, Generate: true, CleanOutput: true)));
        Assert.Throws<ArgumentException>(() => TextureGeneration.Run(f.Textures.Request with { OutputFolder = f.Output }));
        var manifest = GrassGenerationManifest.Read(f.Output, true)!;
        manifest.Preview = true; manifest.Save(f.Output);
        Assert.Throws<InvalidOperationException>(() => GrassGenerationManifest.Read(f.Output, true));
        Assert.Throws<InvalidOperationException>(() => ScanPipeline.Run(new ScanRequest(f.Textures.Game, new ScanOptions(), GeneratedGrassFolder: f.Output)));
    }

    [Fact]
    public void ChangedLoadOrderIsRejectedBeforeCleaningLodOutput()
    {
        using var f = new GrassFixture(); using var limits = RunLimits.Enter(2);
        GrassGeneration.Run(f.Request);
        var manifest = GrassGenerationManifest.Read(f.Output, true)!;
        manifest.LoadOrderFingerprint = "older order"; manifest.Save(f.Output);
        string lod = Path.Combine(f.Root, "LOD"); Directory.CreateDirectory(lod);
        File.WriteAllText(Path.Combine(lod, "AnvilLOD.log"), "keep previous");
        // A stale load order must be reported before reading even a corrupt cache payload.
        File.AppendAllText(Path.Combine(f.Output, "grass", "Tamrielx0000y0000.cgid"), "changed");
        var lines = new List<string>();
        Assert.Throws<InvalidOperationException>(() => ScanPipeline.Run(new ScanRequest(f.Textures.Game, new ScanOptions(),
            OutputFolder: lod, Generate: true, CleanOutput: true, GeneratedGrassFolder: f.Output), new Progress(lines.Add)));
        Assert.DoesNotContain(lines, line => line.StartsWith("Verifying generated grass cache:"));
        Assert.Equal("keep previous", File.ReadAllText(Path.Combine(lod, "AnvilLOD.log")));
    }

    [Fact]
    public void Grass_verification_reports_progress_and_cancels_before_scan_or_output_changes()
    {
        using var f = new GrassFixture(); using var limits = RunLimits.Enter(2);
        GrassGeneration.Run(f.Request);
        var manifest = GrassGenerationManifest.Read(f.Output, true)!;
        var lines = new List<string>();
        manifest.Verify(f.Output, new Progress(lines.Add));
        Assert.StartsWith("Verifying generated grass cache: 0/2 files", lines.First());
        Assert.StartsWith("Verified generated grass cache: 2 files", lines.Last());

        string lod = Path.Combine(f.Root, "LOD"); Directory.CreateDirectory(lod);
        File.WriteAllText(Path.Combine(lod, "AnvilLOD.log"), "keep previous");
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => ScanPipeline.Run(new ScanRequest(f.Textures.Game, new ScanOptions(),
            OutputFolder: lod, Generate: true, CleanOutput: true, GeneratedGrassFolder: f.Output),
            new Progress(line => { if (line.StartsWith("Verifying generated grass cache:")) cancellation.Cancel(); }), cancellation.Token));
        Assert.Equal("keep previous", File.ReadAllText(Path.Combine(lod, "AnvilLOD.log")));

        File.AppendAllText(Path.Combine(f.Output, "grass", "Tamrielx0000y0000.cgid"), "changed");
        Assert.Throws<IOException>(() => manifest.Verify(f.Output));
    }

    [Fact]
    public void VirtualInputRespectsPriorityCasingIniSelectionAndPreservesSources()
    {
        using var f = new GrassFixture(); using var limits = RunLimits.Enter(2);
        var instance = Path.Combine(f.Root, "Fluorine");
        var profile = Path.Combine(instance, "profiles", "Selected"); Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(instance, "ModOrganizer.ini"), $"[General]\ngamePath={Path.GetDirectoryName(f.Textures.Data)}\nselected_profile=Selected\n");
        File.WriteAllText(Path.Combine(profile, "modlist.txt"), "+High\n+Low\n-Disabled\n");
        File.WriteAllText(Path.Combine(profile, "plugins.txt"), "");
        File.WriteAllText(Path.Combine(profile, "settings.ini"), "[General]\nLocalSettings=true\n");
        File.WriteAllText(Path.Combine(profile, "skyrim.ini"), "[Grass]\niMinGrassSize=72\n");
        foreach (var (mod, value) in new[] { ("Low", "low"), ("High", "high"), ("Disabled", "disabled") })
        {
            TextureFixture.Put(Path.Combine(instance, "mods", mod), @"Meshes\Trees\TestTree.nif", System.Text.Encoding.UTF8.GetBytes(value));
            TextureFixture.Put(Path.Combine(instance, "mods", mod), @"SKSE\Plugins\GrassControl.ini", System.Text.Encoding.UTF8.GetBytes(value));
        }
        var overwrite = Path.Combine(instance, "overwrite");
        TextureFixture.Put(overwrite, @"SKSE\Plugins\GrassControl.ini", "overwrite"u8.ToArray());
        var req = f.Request with { Game = new GameContextOptions(Mode: GameSourceMode.Mo2Instance, Mo2InstanceFolder: instance) };
        var work = Path.Combine(f.Root, "inputs"); var input = GrassGeneration.PrepareInput(req, work, null, default);
        var mesh = Path.Combine(input.Data, "meshes", "trees", "testtree.nif");
        Assert.Equal("high", File.ReadAllText(mesh));
        if (!OperatingSystem.IsWindows()) Assert.NotNull(new FileInfo(mesh).LinkTarget);
        Assert.Equal("overwrite", File.ReadAllText(input.Ngio));
        Assert.Contains("iMinGrassSize=72", File.ReadAllText(Path.Combine(input.Inis, "Skyrim.ini")));
        Directory.Delete(work, true);
        Assert.Equal("high", File.ReadAllText(Path.Combine(instance, "mods", "High", "Meshes", "Trees", "TestTree.nif")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StagedInputCanBeRemovedWithoutChangingSourceAttributes(bool readOnly)
    {
        using var fixture = new TextureFixture();
        string source = Path.Combine(fixture.Root, "source.nif"), destination = Path.Combine(fixture.Root, "staged.nif");
        File.WriteAllText(source, "read-only input");
        var original = File.GetAttributes(source);
        File.SetAttributes(source, readOnly ? original | FileAttributes.ReadOnly : original);
        var attributes = File.GetAttributes(source);
        try
        {
            int copies = 0;
            GrassGeneration.StageInputFile(source, destination, default, () => copies++);
            Assert.Equal("read-only input", File.ReadAllText(destination));
            if (OperatingSystem.IsWindows() && readOnly) Assert.Equal(1, copies);
            File.Delete(destination);
            Assert.Equal("read-only input", File.ReadAllText(source));
            Assert.Equal(attributes, File.GetAttributes(source));
        }
        finally { File.SetAttributes(source, original); }
    }

    [Fact]
    public void InputCopyFallbackPreservesContentAndRejectsOverwriteOrCancelledWork()
    {
        using var fixture = new TextureFixture();
        string source = Path.Combine(fixture.Root, "Source forêt.bsa"), destination = Path.Combine(fixture.Root, "copy.bsa");
        byte[] content = new byte[2 * 1024 * 1024 + 17];
        new Random(8).NextBytes(content);
        File.WriteAllBytes(source, content);
        GrassGeneration.CopyInputFile(source, destination, default);
        Assert.Equal(content, File.ReadAllBytes(destination));
        Assert.Throws<IOException>(() => GrassGeneration.CopyInputFile(source, destination, default));
        Assert.Equal(content, File.ReadAllBytes(source));
        string cancelled = Path.Combine(fixture.Root, "cancelled.bsa");
        Assert.Throws<OperationCanceledException>(() => GrassGeneration.CopyInputFile(source, cancelled, new CancellationToken(true)));
        Assert.False(File.Exists(cancelled));
    }
}
