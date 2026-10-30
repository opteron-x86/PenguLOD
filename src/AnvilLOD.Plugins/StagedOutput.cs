using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Pipeline;
using AnvilLOD.Core.Incremental;

namespace AnvilLOD.Plugins;

/// <summary>Prepare a complete result beside its destination, then publish by directory rename.</summary>
public sealed class StagedOutput : IDisposable
{
    public string Output { get; }
    public string Payload { get; }
    public string? PreviousOutput { get; private set; }
    private readonly string _workspace;
    private readonly Dictionary<string, (long Length, long Modified)> _original;
    private readonly bool _existed;
    private readonly CancellationToken _ct;
    private bool _preserve;
    private readonly WrittenOutputHashes _written;
    private readonly List<string> _deferredBlocks = [];
    private bool _blocksSeeded;
    private readonly Dictionary<string, string> _expected;

    public StagedOutput(string output, bool seedPrevious, IProgress<string>? progress = null, CancellationToken ct = default, bool deferLodBlocks = false, OutputSnapshot? previousHashes = null)
    {
        Output = HostPath.Canonical(output); _ct = ct;
        var paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _expected = previousHashes?.Files.ToDictionary(k => k.Key.Replace('\\', '/'), v => v.Value, paths) ?? new(paths);
        _existed = Directory.Exists(Output); _original = Inventory(Output, ct);
        _workspace = Path.Combine(Path.GetDirectoryName(Output)!, ".pengulod-build-" + Guid.NewGuid().ToString("N"));
        Payload = Path.Combine(_workspace, "output"); Directory.CreateDirectory(Payload);
        _written = new(Payload);
        try
        {
            using var timing = WorkTiming.Measure("preparation");
            if (seedPrevious && _existed)
            {
                progress?.Report($"Preparing an isolated incremental build from {_original.Count:N0} previous files...");
                foreach (var relative in _original.Keys)
                {
                    ct.ThrowIfCancellationRequested();
                    var parts = relative.Replace('\\', '/').Split('/');
                    if (deferLodBlocks && parts.Length == 5 && parts[0].Equals("meshes", StringComparison.OrdinalIgnoreCase)
                        && parts[1].Equals("terrain", StringComparison.OrdinalIgnoreCase) && parts[3].Equals("objects", StringComparison.OrdinalIgnoreCase)
                        && parts[4].EndsWith(".bto", StringComparison.OrdinalIgnoreCase))
                        _deferredBlocks.Add(relative);
                    else Seed(relative);
                }
            }
            else if (File.Exists(Path.Combine(Output, "meta.ini"))) Seed("meta.ini");
            _blocksSeeded = _deferredBlocks.Count == 0;
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Keep unchanged and out-of-scope blocks; avoid copying blocks that will be replaced or removed.</summary>
    public BuildPlan SeedLodBlocks(BuildPlan plan, IProgress<string>? progress = null)
    {
        if (_blocksSeeded) return plan;
        using var timing = WorkTiming.Measure("staging-copy");
        var discard = plan.Rebuild.Select(q => q.RelativePath).Concat(plan.StaleFiles)
            .Select(p => p.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long avoided = 0; int kept = 0;
        var unchanged = plan.Unchanged.ToDictionary(q => q.RelativePath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        var damaged = new List<AnvilLOD.Core.World.QuadKey>();
        foreach (var relative in _deferredBlocks)
        {
            _ct.ThrowIfCancellationRequested();
            if (discard.Contains(relative.Replace('\\', '/'))) avoided += _original[relative].Length;
            else
            {
                Seed(relative);
                string target = Path.Combine(Payload, relative), key = relative.Replace('\\', '/');
                if (_expected.TryGetValue(key, out var expected) && WrittenOutputHashes.TryGet(target, out var actual)
                    && !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    if (!unchanged.TryGetValue(key, out var quad))
                        throw new IOException("A previous LOD block outside the selected rebuild scope was changed: " + relative + ". Include its worldspace/level in the next generation.");
                    File.Delete(target); damaged.Add(quad);
                    progress?.Report("Rebuilding changed previous LOD block: " + relative);
                }
                else kept++;
            }
        }
        _blocksSeeded = true;
        progress?.Report($"Incremental staging: kept {kept:N0} blocks; avoided copying {avoided / 1048576.0:N0} MiB scheduled for rebuild/removal.");
        return damaged.Count == 0 ? plan : new([.. plan.Rebuild, .. damaged], plan.Unchanged.Except(damaged).ToArray(), plan.StaleFiles);
    }

    private void Seed(string relative)
    {
        var target = Path.Combine(Payload, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        WrittenOutputHashes.Copy(Path.Combine(Output, relative), target, _ct);
    }

    public void Commit(IProgress<string>? progress = null)
    {
        _ct.ThrowIfCancellationRequested();
        if (!_blocksSeeded) throw new InvalidOperationException("Deferred LOD blocks must be resolved before publication.");
        if (!Inventory(Output, _ct).OrderBy(k => k.Key).SequenceEqual(_original.OrderBy(k => k.Key)) || Directory.Exists(Output) != _existed)
            throw new IOException("The destination changed while generation was running. Previous output was preserved: " + Output);
        using var timing = WorkTiming.Measure("publication");
        // Cancellation is deferred during the short rename/rollback sequence.
        if (_existed)
        {
            PreviousOutput = Output + ".previous-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
            Directory.Move(Output, PreviousOutput);
        }
        try { Directory.Move(Payload, Output); }
        catch (Exception publication)
        {
            try { if (PreviousOutput is not null) Directory.Move(PreviousOutput, Output); }
            catch (Exception rollback)
            {
                _preserve = true;
                throw new IOException($"Publication and rollback failed. Previous output: {PreviousOutput}; new output: {Payload}. {rollback.Message}", publication);
            }
            throw;
        }
        progress?.Report("Published output: " + Output);
        if (PreviousOutput is not null) progress?.Report("Previous output retained: " + PreviousOutput);
    }

    private static Dictionary<string, (long Length, long Modified)> Inventory(string root, CancellationToken ct)
    {
        var result = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        if (new DirectoryInfo(root).LinkTarget is not null) throw new IOException("Output must not be a symbolic link: " + root);
        if (!Directory.Exists(root)) return result;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var dir))
            foreach (var path in Directory.EnumerateFileSystemEntries(dir))
            {
                ct.ThrowIfCancellationRequested();
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                if (info.LinkTarget is not null) throw new IOException("Output contains a symbolic link: " + path);
                if (info is DirectoryInfo) pending.Push(path);
                else result.Add(Path.GetRelativePath(root, path), (((FileInfo)info).Length, info.LastWriteTimeUtc.Ticks));
            }
        return result;
    }

    public void Dispose()
    {
        _written.Dispose();
        if (!_preserve && Directory.Exists(_workspace))
            try { Directory.Delete(_workspace, true); } catch (IOException) { /* original/published output is independent */ }
    }
}
