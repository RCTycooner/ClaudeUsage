namespace ClaudeUsage.Storage;

/// <summary>
/// Runs file writes one at a time, in order, on the thread pool, so callers (the UI thread) never block on disk.
/// One instance per file keeps that file to a single writer. A failing write is swallowed and never stops the queue.
/// </summary>
public sealed class SerialWriter
{
    /// <summary>How long <see cref="Flush"/> waits by default (app exit).</summary>
    public static readonly TimeSpan DefaultFlushWait = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>Queues <paramref name="write"/> to run after every write queued before it.</summary>
    public void Enqueue(Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_gate)
        {
            _tail = _tail.ContinueWith(_ => Run(write), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// <summary>Waits until every write queued so far has finished; false if <paramref name="timeout"/> ran out first.</summary>
    public bool Flush(TimeSpan? timeout = null)
    {
        Task tail;
        lock (_gate)
        {
            tail = _tail;
        }

        return tail.Wait(timeout ?? DefaultFlushWait);
    }

    private static void Run(Action write)
    {
        try
        {
            write();
        }
#pragma warning disable CA1031 // A background write must never crash the app or break the queue; the callers handle expected I/O errors.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Swallowed: there is nowhere safe to report it (the logger itself writes through this queue).
        }
    }
}
