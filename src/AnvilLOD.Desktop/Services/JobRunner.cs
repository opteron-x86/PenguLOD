using System.Diagnostics;
using AnvilLOD.Core.Pipeline;

namespace AnvilLOD.Desktop.Services;

public sealed record JobResult<T>(T? Value, string Folder, TimeSpan Elapsed, Exception? Error, bool Cancelled)
{
    public bool Success => Error is null && !Cancelled;
}

/// <summary>One job at a time. Cancellation is cooperative; disposal never races a running worker.</summary>
public sealed class JobRunner
{
    private CancellationTokenSource? _cancellation;
    public bool IsRunning => _cancellation is not null;
    public void Cancel() => _cancellation?.Cancel();

    public async Task<JobResult<T>> Run<T>(string title, string cache, int threads,
        Func<IProgress<string>, CancellationToken, T> work, Action<string> message)
    {
        if (IsRunning) throw new InvalidOperationException("A job is already running.");
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        string folder = Path.Combine(cache, "runs", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        var watch = Stopwatch.StartNew();
        try
        {
            return await Task.Run(() =>
            {
                Directory.CreateDirectory(folder);
                using var writer = new StreamWriter(Path.Combine(folder, "run.log")) { AutoFlush = true };
                var progress = new SynchronousProgress(line => { lock (writer) writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}"); message(line); });
                progress.Report(title);
                using var scope = RunLimits.Enter(threads, cancellation.Token, cache);
                T result = work(progress, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                return new JobResult<T>(result, folder, watch.Elapsed, null, false);
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { return new(default, folder, watch.Elapsed, null, true); }
        catch (Exception ex) { return new(default, folder, watch.Elapsed, ex, false); }
        finally { _cancellation = null; }
    }

    private sealed class SynchronousProgress(Action<string> write) : IProgress<string>
    { public void Report(string value) => write(value); }
}
