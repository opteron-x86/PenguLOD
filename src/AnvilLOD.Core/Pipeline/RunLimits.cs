namespace AnvilLOD.Core.Pipeline;

/// <summary>Per-job CPU limit and cancellation, propagated into parallel workers by ExecutionContext.</summary>
public static class RunLimits
{
    private sealed record State(int Threads, CancellationToken Cancellation, string? CacheDirectory);
    private static readonly AsyncLocal<State?> Current = new();

    public static IDisposable Enter(int threads = 0, CancellationToken cancellation = default, string? cacheDirectory = null)
    {
        if (threads < 0) throw new ArgumentOutOfRangeException(nameof(threads));
        var previous = Current.Value;
        Current.Value = new State(threads == 0 ? Math.Max(1, Environment.ProcessorCount - 1) : threads, cancellation, cacheDirectory);
        return new Scope(() => Current.Value = previous);
    }

    public static string? CacheDirectory => Current.Value?.CacheDirectory;
    public static void Check() => Current.Value?.Cancellation.ThrowIfCancellationRequested();
    public static ParallelOptions Parallel(CancellationToken cancellation = default) => new()
    {
        MaxDegreeOfParallelism = Current.Value?.Threads ?? Environment.ProcessorCount,
        CancellationToken = cancellation.CanBeCanceled ? cancellation : Current.Value?.Cancellation ?? default,
    };

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
