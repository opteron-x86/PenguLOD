using System.Text.Json;
using PenguLOD.Core.Incremental;
using PenguLOD.Core.Pipeline;
using PenguLOD.Desktop.Models;
using PenguLOD.Desktop.Services;
using PenguLOD.Plugins;
using PenguLOD.Testing;

namespace PenguLOD.Core.Tests;

/// <summary>Publication, receipts, retention and cleanup of stage output folders.</summary>
[Collection("CLI")]
public sealed class OutputLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pengulod-lifecycle-" + Guid.NewGuid().ToString("N"));
    public OutputLifecycleTests() => Directory.CreateDirectory(_root);
    public void Dispose() { StagedOutput.DisableExchange = false; if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string Path_(params string[] parts) => Path.Combine([_root, .. parts]);
    private static void Put(string file, string text) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }

    private static void Publish(string output, string content, IProgress<string>? progress = null)
    {
        using var staged = new StagedOutput(output, seedPrevious: false, progress);
        Put(Path.Combine(staged.Payload, "PenguLOD.esm"), content);
        staged.Commit(progress);
    }

    // 4.1 ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void UserOwnedFilesFollowTheInstalledCopyEvenWhenEditedDuringARun()
    {
        string output = Path_("out");
        Publish(output, "v1");
        Put(Path.Combine(output, "meta.ini"), "installed");
        Put(Path.Combine(output, "SKSE", "Plugins", "PenguLOD.ini"), "user edits");
        using (var staged = new StagedOutput(output, seedPrevious: true))
        {
            Put(Path.Combine(staged.Payload, "SKSE", "Plugins", "PenguLOD.ini"), "generated default");
            // The mod manager and the user change these while the run is going on.
            Put(Path.Combine(output, "meta.ini"), "rewritten by the mod manager");
            Put(Path.Combine(output, "SKSE", "Plugins", "PenguLOD.ini"), "edited again");
            staged.Commit();
        }
        Assert.Equal("rewritten by the mod manager", File.ReadAllText(Path.Combine(output, "meta.ini")));
        Assert.Equal("edited again", File.ReadAllText(Path.Combine(output, "SKSE", "Plugins", "PenguLOD.ini")));

        using var guarded = new StagedOutput(output, seedPrevious: true);
        Put(Path.Combine(output, "PenguLOD.esm"), "changed generated file");
        Assert.Throws<IOException>(() => guarded.Commit());
    }

    [Fact]
    public void ReceiptsAndVerificationIgnoreUserOwnedFiles()
    {
        string output = Path_("lod");
        Put(Path.Combine(output, "PenguLOD.esm"), "plugin");
        Put(Path.Combine(output, "SKSE", "Plugins", "PenguLOD.ini"), "defaults");
        var snapshot = OutputSnapshot.Capture(output);
        Assert.DoesNotContain(snapshot.Files.Keys, UserOwnedFiles.Contains);
        // Receipts written by 0.6.0-alpha.4 hashed the runtime INI as well.
        snapshot.Files["SKSE/Plugins/PenguLOD.ini"] = OutputSnapshot.HashFile(Path.Combine(output, "SKSE", "Plugins", "PenguLOD.ini"));
        new OutputSetReceipt { Stages = [new(StageIds.Lod, "old", "source", DateTimeOffset.UtcNow, OutputSetReceipt.Scope(snapshot.Files.Keys),
            snapshot.Files, [])] }.Write(output);
        Assert.DoesNotContain(OutputSetReceipt.Read(output).Stages[0].Files.Keys, UserOwnedFiles.Contains);
        Put(Path.Combine(output, "SKSE", "Plugins", "PenguLOD.ini"), "edited by the user");
        Put(Path.Combine(output, "meta.ini"), "[General]");
        Assert.True(OutputManagement.Verify([output]).Valid, OutputManagement.Verify([output]).Summary());
        Assert.DoesNotContain(OutputSetReceipt.Record(output, StageIds.Lod, "source", snapshot).Files.Keys, UserOwnedFiles.Contains);
    }

    [Fact]
    public void PackagesKeepTheInstalledRuntimeIniOrTakeTheGeneratedDefault()
    {
        string source = Path_("lod"), package = Path_("package");
        Put(Path.Combine(source, "PenguLOD.esm"), "plugin");
        Put(Path.Combine(source, "SKSE", "Plugins", "PenguLOD.ini"), "generated default");
        OutputSetReceipt.Record(source, StageIds.Lod, "source", OutputSnapshot.Capture(source));
        OutputManagement.Publish(OutputManagement.Plan([source], package));
        Assert.Equal("generated default", File.ReadAllText(Path.Combine(package, "SKSE", "Plugins", "PenguLOD.ini")));
        Put(Path.Combine(package, "SKSE", "Plugins", "PenguLOD.ini"), "user settings");
        var plan = OutputManagement.Plan([source], package); Assert.True(plan.CanPublish, string.Join('\n', plan.Errors));
        OutputManagement.Publish(plan);
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(package, "SKSE", "Plugins", "PenguLOD.ini")));
        Assert.True(OutputManagement.Verify([package]).Valid);
    }

    // 4.2 ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TextureReceiptsArePublishedTogetherWithTheTextures()
    {
        using var f = new TextureFixture(); using var limits = RunLimits.Enter(2);
        string cache = Path.Combine(f.Root, "cache");
        var first = GenerationPipeline.Run(new GenerationPipelineRequest(Textures: f.Request, Preflight: false), cache);
        Assert.True(first.Success, first.Error);
        Assert.True(OutputManagement.Verify([f.Output]).Valid);
        var receipt = File.ReadAllText(Path.Combine(f.Output, OutputSetReceipt.FileName));
        // A failure after the receipt is staged but before publication leaves the previous output and receipt together.
        File.WriteAllText(f.RuleFile, TextureFixture.Tile(@"textures\green.dds", @"textures\lod\other.dds", 0));
        Assert.Throws<InvalidOperationException>(() => TextureGeneration.Run(f.Request, beforePublish: payload =>
        {
            Assert.True(File.Exists(Path.Combine(payload, TextureGenerationManifest.FileName)));
            throw new InvalidOperationException("stopped before publication");
        }));
        Assert.Equal(receipt, File.ReadAllText(Path.Combine(f.Output, OutputSetReceipt.FileName)));
        Assert.True(OutputManagement.Verify([f.Output]).Valid);
        Assert.Empty(Directory.GetDirectories(f.Root, ".pengulod-build-*"));
    }

    // 4.3 ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void LodCheckpointsComeFromTheOutputNotThePlan()
    {
        string output = Path_("lod");
        Put(Path.Combine(output, "meshes", "terrain", "World", "objects", "World.4.0.0.bto"), "block from plan B");
        Assert.Null(GenerationPipeline.PublishedFiles(output, default));
        var snapshot = OutputSetReceipt.Record(output, StageIds.Lod, "source", OutputSnapshot.Capture(output));
        var published = GenerationPipeline.PublishedFiles(output, default)!;
        Assert.Equal(snapshot.Files["meshes/terrain/World/objects/World.4.0.0.bto"], published.Files["meshes/terrain/World/objects/World.4.0.0.bto"]);
    }

    // 4.4 ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AStageThatPublishedIsCompleteEvenIfCancelledAfterwards()
    {
        using var cancel = new CancellationTokenSource();
        string cache = Path_("cache"), grass = Path_("grass"), textures = Path_("textures");
        using var job = new PersistentPipeline(cache, [new("grass", "Grass", grass, []), new("textures", "Textures", textures, ["grass"])], ct: cancel.Token);
        job.Run("grass", "g", () => { Put(Path.Combine(grass, "out.cgid"), "published"); cancel.Cancel(); return "done"; });
        Assert.Equal("Complete", job.Journal.Stages[0].Status);
        Assert.Throws<OperationCanceledException>(() => job.Run("textures", "t", () => "never"));
        Assert.DoesNotContain(job.Journal.Stages[1].Status, new[] { "Running", "Complete" });
    }

    // 4.5 ------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void PublicationKeepsTheConfiguredNumberOfPreviousCopies(int keep)
    {
        string output = Path_("out");
        using (OutputRetention.Enter(keep))
            for (int i = 0; i < 4; i++) { Publish(output, "v" + i); Thread.Sleep(20); }
        var previous = OutputRetention.PreviousCopies(output);
        Assert.Equal(keep, previous.Count);
        Assert.Equal("v3", File.ReadAllText(Path.Combine(output, "PenguLOD.esm")));
        if (keep > 0) Assert.Equal("v2", File.ReadAllText(Path.Combine(previous[0], "PenguLOD.esm")));
    }

    [Fact]
    public void UnfinishedWorkspacesOfThisOutputAreSweptButNotOthers()
    {
        string output = Path_("out"), other = Path_("other");
        string orphan = Path_(StagedOutput.WorkspacePrefix(output) + Guid.NewGuid().ToString("N"));
        string foreign = Path_(StagedOutput.WorkspacePrefix(other) + Guid.NewGuid().ToString("N"));
        Put(Path.Combine(orphan, "output", "stale.bto"), "x"); Put(Path.Combine(foreign, "output", "busy.bto"), "x");
        var messages = new List<string>();
        Publish(output, "v1", new Progress(messages));
        Assert.False(Directory.Exists(orphan)); Assert.True(Directory.Exists(foreign));
        Assert.Contains(messages, m => m.Contains("unfinished build workspace"));
    }

    [Fact]
    public void CleanListsByDefaultAndRemovesWithApply()
    {
        string output = Path_("out");
        using (OutputRetention.Enter(5))
            for (int i = 0; i < 3; i++) { Publish(output, "v" + i); Thread.Sleep(20); }
        string orphan = Path_(StagedOutput.WorkspacePrefix(output) + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(orphan);
        string legacy = Path_(".pengulod-build-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(legacy);
        string recentLegacy = Path_(".pengulod-build-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(recentLegacy);
        Directory.SetLastWriteTimeUtc(legacy, DateTime.UtcNow.AddDays(-2));
        var items = OutputCleanup.Find(output, keepPrevious: 1);
        Assert.Equal(2, OutputRetention.PreviousCopies(output).Count);
        Assert.Equal(3, items.Count);   // one previous copy beyond the newest, the orphan and the stale legacy workspace
        Assert.DoesNotContain(items, i => i.Path == recentLegacy);

        Assert.Equal(0, PenguLOD.Cli.Program.Main(["clean", "--input", output]));
        Assert.True(Directory.Exists(orphan));
        Assert.Equal(0, PenguLOD.Cli.Program.Main(["clean", "--input", output, "--apply"]));
        Assert.False(Directory.Exists(orphan)); Assert.False(Directory.Exists(legacy)); Assert.True(Directory.Exists(recentLegacy));
        Assert.Single(OutputRetention.PreviousCopies(output));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(output, "PenguLOD.esm")));
        Assert.Equal(2, PenguLOD.Cli.Program.Main(["clean"]));
    }

    [Fact]
    public void RunFoldersAndTerrainTileCachesArePruned()
    {
        string runs = Path_("runs");
        for (int i = 0; i < 5; i++) { var dir = Directory.CreateDirectory(Path.Combine(runs, "run" + i)); dir.LastWriteTimeUtc = DateTime.UtcNow.AddMinutes(i); }
        Assert.Equal(3, FolderRetention.Prune(runs, 2));
        Assert.Equal(["run3", "run4"], Directory.GetDirectories(runs).Select(d => Path.GetFileName(d)).Order().ToArray());

        string tiles = Path_("terrain-tiles"), current = Path.Combine(tiles, new string('A', 64)), old = Path.Combine(tiles, new string('B', 64));
        string pending = Path.Combine(current, "entry.pending-" + Guid.NewGuid().ToString("N")), unrelated = Path.Combine(tiles, "notes");
        foreach (var dir in new[] { current, old, pending, unrelated }) Directory.CreateDirectory(dir);
        Assert.Equal(2, TerrainTileCache.Sweep(tiles, current, null));
        Assert.True(Directory.Exists(current)); Assert.False(Directory.Exists(old)); Assert.False(Directory.Exists(pending)); Assert.True(Directory.Exists(unrelated));
    }

    [Fact]
    public void RetentionIsAPresetAndCliSetting()
    {
        using var f = new TextureFixture();
        var s = new DesktopSettings { SourceMode = "Data folder / Vortex", GameFolder = f.Data, PluginsFile = f.Plugins,
            TextureGenOutput = Path.Combine(f.Root, "textures"), KeepPreviousOutputs = 3 };
        Assert.Equal(3, RequestFactory.Stages(s, textures: true).KeepPrevious);
        s.KeepPreviousOutputs = 1.5m; Assert.Throws<ArgumentException>(() => RequestFactory.Stages(s, textures: true));
        Assert.Equal(OutputRetention.Default, new GenerationPipelineRequest().KeepPrevious);
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputRetention.Enter(-1));
        Assert.Equal(2, PenguLOD.Cli.Program.Main(["clean", "--input", Path_("x"), "--keep-previous", "-1"]));
    }

    // 4.6 ------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PublicationSwapsAtomicallyOnLinuxAndFallsBackToTwoRenames(bool disableExchange)
    {
        string output = Path_("out");
        Publish(output, "v1");
        StagedOutput.DisableExchange = disableExchange;
        using (var staged = new StagedOutput(output, seedPrevious: false))
        {
            Put(Path.Combine(staged.Payload, "PenguLOD.esm"), "v2");
            staged.Commit();
            Assert.Equal(OperatingSystem.IsLinux() && !disableExchange, staged.Exchanged);
            Assert.NotNull(staged.PreviousOutput);
            Assert.Equal("v1", File.ReadAllText(Path.Combine(staged.PreviousOutput!, "PenguLOD.esm")));
        }
        Assert.Equal("v2", File.ReadAllText(Path.Combine(output, "PenguLOD.esm")));
        Assert.Empty(Directory.GetDirectories(_root, ".pengulod-build-*"));
    }

    [Fact]
    public void AMissingOutputWithOnePreviousCopyIsReported()
    {
        string output = Path_("out");
        Publish(output, "v1"); Publish(output, "v2");
        Directory.Delete(output, true);   // as if a process stopped between the two renames
        var messages = new List<string>();
        using (new StagedOutput(output, seedPrevious: false, new Progress(messages))) { }
        Assert.Contains(messages, m => m.Contains("previous copy exists") && m.Contains("PenguLOD clean"));
    }

    // 4.7 ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void LoadedBuildManifestsCompareKeysWithoutCase()
    {
        string output = Path_("lod");
        var manifest = new BuildManifest(); manifest.Quads["Tamriel.4.0.0"] = "hash"; manifest.Trees["Tamriel"] = "trees";
        manifest.Save(output);
        var loaded = BuildManifest.LoadOrEmpty(output);
        Assert.True(loaded.Quads.ContainsKey("tamriel.4.0.0")); Assert.True(loaded.Trees.ContainsKey("TAMRIEL"));
    }

    [Fact]
    public void RelativePresetPathsResolveAgainstThePresetFolder()
    {
        string folder = Path_("presets"); Directory.CreateDirectory(folder);
        string preset = Path.Combine(folder, "preset.json");
        SettingsStore.Write(preset, new DesktopSettings { OutputFolder = "Output" });
        var settings = SettingsStore.ReadPreset(preset);
        Assert.Equal(Path.Combine(folder, "Output"), RequestFactory.PathValue(settings.OutputFolder, settings));
        Assert.Equal(Path.Combine(folder, "Output"), RequestFactory.PathValue(settings.Snapshot().OutputFolder, settings.Snapshot()));
        Assert.Null(SettingsStore.Read(preset).BaseFolder);
    }

    [Fact]
    public void WrittenBytesAreHashedOnce()
    {
        string folder = Path_("payload"); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "a.bin"); byte[] bytes = [1, 2, 3]; File.WriteAllBytes(file, bytes);
        using var scope = new WrittenOutputHashes(folder);
        string hash = WrittenOutputHashes.RecordAndHash(file, bytes);
        Assert.Equal(OutputSnapshot.HashFile(file), hash);
        Assert.True(WrittenOutputHashes.TryGet(file, out var recorded)); Assert.Equal(hash, recorded);
    }

    private sealed class Progress(List<string> messages) : IProgress<string>
    { public void Report(string value) { lock (messages) messages.Add(value); } }
}
