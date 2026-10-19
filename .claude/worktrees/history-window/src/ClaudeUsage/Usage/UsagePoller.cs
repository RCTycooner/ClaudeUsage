using System.Windows.Threading;

namespace ClaudeUsage.Usage;

/// <summary>Why a poll failed. <see cref="RawText"/> holds the unparsable output, when there was some.</summary>
public sealed record UsagePollFailure(DateTimeOffset At, string Message, string? RawText, Exception Error);

/// <summary>
/// Polls an <see cref="IUsageSource"/> every minute (first poll immediately) on a background task.
/// Polls never overlap. <see cref="Updated"/> and <see cref="Failed"/> are raised through the injected invoker
/// (the WPF dispatcher in the app), and never after <see cref="Dispose"/>.
/// </summary>
public sealed class UsagePoller : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    /// <summary>How long <see cref="Dispose"/> waits for an in-flight poll (and its child process) to wind down.</summary>
    public static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(2);

    private readonly IUsageSource _source;
    private readonly Func<Action, CancellationToken, Task> _invoke;
    private readonly TimeSpan _interval;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();
    private Task? _inFlight;
    private Task? _loop;

    public UsagePoller(IUsageSource source, Dispatcher dispatcher, TimeSpan? interval = null, Func<DateTimeOffset>? clock = null)
        : this(source, DispatcherInvoker(dispatcher), interval, clock)
    {
    }

    /// <param name="source">Where the usage text comes from.</param>
    /// <param name="invoke">Runs an action on the thread that should see the events (and completes when it ran).</param>
    /// <param name="interval">Time between polls.</param>
    /// <param name="clock">Clock for snapshot and failure times.</param>
    public UsagePoller(IUsageSource source, Func<Action, CancellationToken, Task> invoke, TimeSpan? interval = null, Func<DateTimeOffset>? clock = null)
    {
        _source = source;
        _invoke = invoke;
        _interval = interval ?? DefaultInterval;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public event EventHandler<UsageSnapshot>? Updated;

    public event EventHandler<UsagePollFailure>? Failed;

    public void Start()
    {
        lock (_gate)
        {
            _loop ??= Task.Run(() => RunAsync(_cts.Token));
        }
    }

    /// <summary>Polls now, or joins the poll already in progress.</summary>
    public Task RefreshNowAsync()
    {
        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false } running)
            {
                return running;
            }

            _inFlight = Task.Run(() => PollOnceAsync(_cts.Token));
            return _inFlight;
        }
    }

    /// <summary>
    /// Cancels polling (which kills a claude.exe that is still running) and waits up to <see cref="DisposeWait"/> for
    /// the in-flight poll to finish, so the child is gone before the app exits.
    /// </summary>
    public void Dispose()
    {
        Task? inFlight;
        lock (_gate)
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            _cts.Cancel();
            inFlight = _inFlight;
        }

        try
        {
            inFlight?.Wait(DisposeWait);
        }
        catch (AggregateException)
        {
            // PollOnceAsync handles its own errors; anything left is not worth failing shutdown over.
        }

        // The CTS is not disposed: background tasks may still observe its token while they unwind.
    }

    private static Func<Action, CancellationToken, Task> DispatcherInvoker(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return (action, token) => dispatcher.InvokeAsync(action, DispatcherPriority.Normal, token).Task;
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(_interval);
            do
            {
                await RefreshNowAsync().ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task PollOnceAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        UsageSnapshot? snapshot = null;
        UsagePollFailure? failure = null;
        try
        {
            var raw = await _source.FetchRawAsync(token).ConfigureAwait(false);
            snapshot = UsageOutputParser.Parse(raw, _clock());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (UsageSourceException ex)
        {
            failure = new UsagePollFailure(_clock(), ex.Message, ex.RawOutput, ex);
        }
        catch (UsageParseException ex)
        {
            failure = new UsagePollFailure(_clock(), "Could not parse /usage output.", ex.RawText, ex);
        }
#pragma warning disable CA1031 // A poll must never take down the timer loop; anything unexpected is reported as a failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            failure = new UsagePollFailure(_clock(), $"Unexpected error: {ex.Message}", null, ex);
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await _invoke(() =>
            {
                // Checked again on the target thread: Dispose may have run while this was queued.
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (snapshot is not null)
                {
                    Updated?.Invoke(this, snapshot);
                }
                else if (failure is not null)
                {
                    Failed?.Invoke(this, failure);
                }
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
