using System.Text;
using AnvilLOD.Core.Incremental;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.World;
using AnvilLOD.Plugins;

namespace AnvilLOD.Core.Tests;

public sealed class StagingOptimizationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pengulod-stage-" + Guid.NewGuid().ToString("N"));
    private string Put(string relative, string value)
    {
        var file = Path.Combine(_root, relative.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, value); return file;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void DeferredSeedingPreservesUnchangedAndUnselectedBlocksWithoutCopyingReplacedBlocks()
    {
        var rebuild = new QuadKey("World", LodLevel.Lod4, 0, 0);
        var keep = new QuadKey("World", LodLevel.Lod4, 4, 0);
        var stale = new QuadKey("World", LodLevel.Lod4, 8, 0);
        var other = new QuadKey("Other", LodLevel.Lod4, 0, 0);
        foreach (var q in new[] { rebuild, keep, stale, other }) Put(q.RelativePath, "old");
        Put("custom.txt", "preserved");
        using var stage = new StagedOutput(_root, true, deferLodBlocks: true);
        Assert.Empty(Directory.GetFiles(stage.Payload, "*.bto", SearchOption.AllDirectories));
        Assert.Throws<InvalidOperationException>(() => stage.Commit());
        stage.SeedLodBlocks(new([rebuild], [keep], [stale.RelativePath]));
        Assert.False(File.Exists(BuildManifest.FullPath(stage.Payload, rebuild.RelativePath)));
        Assert.False(File.Exists(BuildManifest.FullPath(stage.Payload, stale.RelativePath)));
        Assert.Equal("old", File.ReadAllText(BuildManifest.FullPath(stage.Payload, keep.RelativePath)));
        Assert.Equal("old", File.ReadAllText(BuildManifest.FullPath(stage.Payload, other.RelativePath)));
        Assert.Equal("preserved", File.ReadAllText(Path.Combine(stage.Payload, "custom.txt")));
        Assert.True(WrittenOutputHashes.TryGet(BuildManifest.FullPath(stage.Payload, keep.RelativePath), out var hash));
        Assert.Equal(OutputSnapshot.HashText("old"), hash);
        // An abandoned build never changes the published source.
        Assert.Equal("old", File.ReadAllText(BuildManifest.FullPath(_root, rebuild.RelativePath)));
        Assert.Equal("old", File.ReadAllText(BuildManifest.FullPath(_root, stale.RelativePath)));
    }

    [Fact]
    public void ChangedPreviousBlockIsRebuiltEvenWhenLengthAndTimestampMatch()
    {
        var quad = new QuadKey("World", LodLevel.Lod4, 0, 0);
        string file = Put(quad.RelativePath, "original");
        var expected = OutputSnapshot.Capture(_root); var time = File.GetLastWriteTimeUtc(file);
        File.WriteAllText(file, "modified"); File.SetLastWriteTimeUtc(file, time);
        using var stage = new StagedOutput(_root, true, deferLodBlocks: true, previousHashes: expected);
        var plan = stage.SeedLodBlocks(new([], [quad], []));
        Assert.Equal(quad, Assert.Single(plan.Rebuild)); Assert.Empty(plan.Unchanged);
        Assert.False(File.Exists(BuildManifest.FullPath(stage.Payload, quad.RelativePath)));
        Assert.Equal("modified", File.ReadAllText(file));
    }

    [Fact]
    public void FreshWriteHashesAreScopedAndLaterEditsFallBackToReadingContents()
    {
        var file = Put("mesh.bto", "new file");
        using (var scope = new WrittenOutputHashes(_root))
        {
            WrittenOutputHashes.Record(file, Encoding.UTF8.GetBytes("new file"));
            Assert.True(WrittenOutputHashes.TryGet(file, out var hash)); Assert.Equal(OutputSnapshot.HashText("new file"), hash);
            Assert.Equal(hash, OutputSnapshot.Capture(_root).Files["mesh.bto"]);
            File.WriteAllText(file, "changed"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
            Assert.False(WrittenOutputHashes.TryGet(file, out _));
            Assert.Equal(OutputSnapshot.HashText("changed"), OutputSnapshot.Capture(_root).Files["mesh.bto"]);
        }
        Assert.False(WrittenOutputHashes.TryGet(file, out _));
        var snapshot = OutputSnapshot.Capture(_root); var time = File.GetLastWriteTimeUtc(file);
        File.WriteAllText(file, "corrupt"); File.SetLastWriteTimeUtc(file, time);
        Assert.Throws<IOException>(() => snapshot.Verify(_root));
    }

    [Fact]
    public void CancelledCopyAndLinkedOutputCannotBePublished()
    {
        Put("file.bin", new string('a', 1024 * 1024));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => new StagedOutput(_root, true, ct: cancel.Token));
        Assert.Equal(1024 * 1024, new FileInfo(Path.Combine(_root, "file.bin")).Length);
        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(Path.Combine(_root, "linked.bin"), Path.Combine(_root, "file.bin"));
            Assert.Throws<IOException>(() => new StagedOutput(_root, true));
        }
    }
}
