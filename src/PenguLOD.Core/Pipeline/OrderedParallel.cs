using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace PenguLOD.Core.Pipeline;

/// <summary>
/// A parallel loop that fails as the sequential loop would: every item before the first failing one still runs, and
/// that first failure is thrown as itself, not wrapped. Which item fails first then does not depend on timing.
/// </summary>
public static class OrderedParallel
{
    /// <param name="worker">Creates the state one worker thread keeps for all the items it runs.</param>
    public static void For<TWorker>(int count, Func<TWorker> worker, Action<int, TWorker> body, CancellationToken ct = default)
    {
        var failures = new ConcurrentDictionary<int, ExceptionDispatchInfo>();
        try
        {
            Parallel.For(0, count, RunLimits.Parallel(ct), worker, (index, loop, state) =>
            {
                // Break leaves the items below the failing one running; those above it are not started.
                if (loop.LowestBreakIteration is { } first && index > first) return state;
                try { body(index, state); }
                catch (Exception e) when (e is not OperationCanceledException) { failures[index] = ExceptionDispatchInfo.Capture(e); loop.Break(); }
                return state;
            }, _ => { });
        }
        catch (AggregateException e) when (e.Flatten().InnerExceptions is var inner && inner.All(i => i is OperationCanceledException))
        { ExceptionDispatchInfo.Capture(inner[0]).Throw(); }
        if (!failures.IsEmpty) failures[failures.Keys.Min()].Throw();
    }
}
