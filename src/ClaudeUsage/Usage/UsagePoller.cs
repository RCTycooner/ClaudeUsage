using System.Windows.Threading;

namespace ClaudeUsage.Usage;

/// <summary>Why a poll failed. <see cref="RawText"/> holds the unparsable output, when there was some.</summary>
public sealed record UsagePollFailure(DateTimeOffset At, string Message, string? RawText, Exception Error);

/// <summary>
/// Polls an <see cref="IUsageSource"/> every minute (first poll immediately) on a background task.
/// Polls never overlap. <see cref="Updated"/> and <see cref="Failed"/> are raised on the dispatcher thread.
/// </summary>
public sealed class UsagePoller : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    private readonly IUsageSource _source;
    private readonly Dispatcher _dispatcher;
    private readonly TimeSpan _interval;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();
    private Task? _inFlight;
    private Task? _loop;

    public UsagePoller(IUsageSource source, Dispatcher dispatcher, TimeSpan? interval = null, Func<DateTimeOffset>? clock = null)
    {
        _source = source;
        _dispatcher = dispatcher;
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

    public void Dispose()
    {
        // Cancelling also kills a claude.exe that is still running. The CTS is not disposed: background
        // tasks may still observe its token while they unwind.
        _cts.Cancel();
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
            await _dispatcher.InvokeAsync(() =>
            {
                if (snapshot is not null)
                {
                    Updated?.Invoke(this, snapshot);
                }
                else if (failure is not null)
                {
                    Failed?.Invoke(this, failure);
                }
            }, DispatcherPriority.Normal, token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
