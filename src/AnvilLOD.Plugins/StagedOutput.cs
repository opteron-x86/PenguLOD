using AnvilLOD.Core.Paths;
using AnvilLOD.Core.Pipeline;

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

    public StagedOutput(string output, bool seedPrevious, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Output = HostPath.Canonical(output); _ct = ct;
        _existed = Directory.Exists(Output); _original = Inventory(Output, ct);
        _workspace = Path.Combine(Path.GetDirectoryName(Output)!, ".pengulod-build-" + Guid.NewGuid().ToString("N"));
        Payload = Path.Combine(_workspace, "output"); Directory.CreateDirectory(Payload);
        try
        {
            using var timing = WorkTiming.Measure("preparation");
            if (seedPrevious && _existed)
            {
                progress?.Report($"Preparing an isolated incremental build from {_original.Count:N0} previous files...");
                foreach (var relative in _original.Keys)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = Path.Combine(Payload, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(Path.Combine(Output, relative), target);
                }
            }
            else if (File.Exists(Path.Combine(Output, "meta.ini"))) File.Copy(Path.Combine(Output, "meta.ini"), Path.Combine(Payload, "meta.ini"));
        }
        catch { Dispose(); throw; }
    }

    public void Commit(IProgress<string>? progress = null)
    {
        _ct.ThrowIfCancellationRequested();
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
        if (!_preserve && Directory.Exists(_workspace))
            try { Directory.Delete(_workspace, true); } catch (IOException) { /* original/published output is independent */ }
    }
}
