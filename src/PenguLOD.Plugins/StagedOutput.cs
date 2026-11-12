using System.Runtime.InteropServices;
using PenguLOD.Core.Paths;
using PenguLOD.Core.Pipeline;
using PenguLOD.Core.Incremental;

namespace PenguLOD.Plugins;

/// <summary>Prepare a complete result beside its destination, then publish by directory rename.</summary>
public sealed class StagedOutput : IDisposable
{
    public string Output { get; }
    public string Payload { get; }
    /// <summary>Private scratch space beside <see cref="Payload"/>, removed with it (for example staged inputs).</summary>
    public string Workspace => _workspace;
    public string? PreviousOutput { get; private set; }
    /// <summary>True when publication swapped the folders in one atomic exchange.</summary>
    public bool Exchanged { get; private set; }
    /// <summary>Tests force the two-rename fallback with this.</summary>
    internal static bool DisableExchange;
    private readonly string _workspace;
    private readonly Dictionary<string, (long Length, long Modified)> _original;
    private readonly bool _existed;
    private readonly CancellationToken _ct;
    private bool _preserve;
    private readonly WrittenOutputHashes _written;
    private readonly List<string> _deferredBlocks = [];
    private bool _blocksSeeded;
    private readonly Dictionary<string, string> _expected;
    private readonly IDisposable _lock;
    private readonly int _keepPrevious;

    /// <summary>Workspaces are named for their output, so a sweep never touches another output's build.</summary>
    public static string WorkspacePrefix(string output) =>
        ".pengulod-build-" + OutputSnapshot.HashText(HostPath.ComparisonKey(HostPath.Canonical(output)))[..12] + "-";

    public StagedOutput(string output, bool seedPrevious, IProgress<string>? progress = null, CancellationToken ct = default, bool deferLodBlocks = false, OutputSnapshot? previousHashes = null)
    {
        Output = HostPath.Canonical(output); _ct = ct; _keepPrevious = OutputRetention.KeepPrevious;
        var paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _expected = previousHashes?.Files.ToDictionary(k => k.Key.Replace('\\', '/'), v => v.Value, paths) ?? new(paths);
        _lock = OutputLock.Acquire(Output);
        try
        {
            SweepOrphans(Output, progress);
            _existed = Directory.Exists(Output); _original = Inventory(Output, ct);
            if (!_existed && OutputRetention.PreviousCopies(Output) is [var only])
                progress?.Report($"The output folder is missing, but a previous copy exists: {only}. An earlier publication may have been interrupted. "
                    + $"This run creates a new output; to restore the old one instead, cancel and rename that folder to \"{Path.GetFileName(Output)}\" "
                    + "(PenguLOD clean --input <output> lists previous copies).");
            _workspace = Path.Combine(Path.GetDirectoryName(Output)!, WorkspacePrefix(Output) + Guid.NewGuid().ToString("N"));
            Payload = Path.Combine(_workspace, "output"); Directory.CreateDirectory(Payload);
            _written = new(Payload);
        }
        catch { _lock.Dispose(); throw; }
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
            foreach (var relative in UserOwnedFiles.Existing(Output))
                if (!File.Exists(Path.Combine(Payload, relative))) Seed(relative);
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
        var damaged = new List<PenguLOD.Core.World.QuadKey>();
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
        // User-owned files (meta.ini, PenguLOD.ini) may change while a long run is going on, e.g. by the mod manager.
        if (!Generated(Inventory(Output, _ct)).SequenceEqual(Generated(_original)) || Directory.Exists(Output) != _existed)
            throw new IOException("The destination changed while generation was running. Previous output was preserved: " + Output);
        // Keep the installed copies of user-owned files, including edits made during this run.
        foreach (var relative in UserOwnedFiles.Existing(Output))
        {
            var target = Path.Combine(Payload, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(Output, relative), target, overwrite: true);
        }
        using var timing = WorkTiming.Measure("publication");
        // Cancellation is deferred during the short rename/rollback sequence.
        if (_existed)
        {
            // Names sort by publication time (to the millisecond), which retention relies on.
            string previous = Output + ".previous-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];
            if (TryExchange(Payload, Output))
            {
                // The new output is live. The old one now sits at Payload and is moved beside it.
                Exchanged = true;
                try { Directory.Move(Payload, previous); PreviousOutput = previous; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _preserve = true; PreviousOutput = Payload;
                    progress?.Report($"Published, but the previous output could not be moved beside it and remains in {Payload}: {e.Message}");
                }
            }
            else
            {
                PreviousOutput = previous;
                Directory.Move(Output, PreviousOutput);
                try { Directory.Move(Payload, Output); }
                catch (Exception publication)
                {
                    try { Directory.Move(PreviousOutput, Output); PreviousOutput = null; }
                    catch (Exception rollback)
                    {
                        _preserve = true;
                        throw new IOException($"Publication and rollback failed. Previous output: {PreviousOutput}; new output: {Payload}. {rollback.Message}", publication);
                    }
                    throw;
                }
            }
        }
        else Directory.Move(Payload, Output);
        progress?.Report("Published output: " + Output);
        OutputRetention.Prune(Output, _keepPrevious, progress);
        if (PreviousOutput is not null && !Directory.Exists(PreviousOutput)) PreviousOutput = null;
        if (PreviousOutput is not null) progress?.Report("Previous output retained: " + PreviousOutput);
    }

    private static IEnumerable<KeyValuePair<string, (long, long)>> Generated(Dictionary<string, (long Length, long Modified)> inventory) =>
        inventory.Where(k => !UserOwnedFiles.Contains(k.Key)).OrderBy(k => k.Key, StringComparer.Ordinal);

    /// <summary>Removes build workspaces of this output left by a process that stopped. Callers hold the output lock.</summary>
    private static void SweepOrphans(string output, IProgress<string>? progress)
    {
        string parent = Path.GetDirectoryName(output)!;
        if (!Directory.Exists(parent)) return;
        foreach (var dir in Directory.EnumerateDirectories(parent, WorkspacePrefix(output) + "*"))
        {
            if (new DirectoryInfo(dir).LinkTarget is not null) continue;
            try { Directory.Delete(dir, recursive: true); progress?.Report("Removed an unfinished build workspace: " + dir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { progress?.Report("Could not remove unfinished build workspace " + dir + ": " + e.Message); }
        }
    }

    private const int AtFdCwd = -100;
    private const uint RenameExchange = 2;

    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameAt2(int oldDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
        int newDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath, uint flags);

    /// <summary>
    /// On Linux, swaps two directories in one step, so a crash leaves either the old or the new output in place, never
    /// neither. False when unavailable (other systems, old kernels or filesystems without support).
    /// </summary>
    private static bool TryExchange(string a, string b)
    {
        if (DisableExchange || !OperatingSystem.IsLinux()) return false;
        try { return RenameAt2(AtFdCwd, a, AtFdCwd, b, RenameExchange) == 0; }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { return false; }
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
        _written?.Dispose();
        if (!_preserve && _workspace is not null && Directory.Exists(_workspace))
            try { Directory.Delete(_workspace, true); } catch (IOException) { /* original/published output is independent */ }
        _lock.Dispose();
    }
}
