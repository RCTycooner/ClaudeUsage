using ClaudeUsage.Usage;

namespace ClaudeUsage.Tests;

public sealed class UsagePollerTests
{
    private const string GoodText = "Current session: 3% used · resets Oct 19, 2am (Europe/Brussels)";

    private static readonly DateTimeOffset s_now = new(2026, 10, 18, 21, 5, 0, TimeSpan.FromHours(2));

    /// <summary>Runs event callbacks inline; stands in for the WPF dispatcher.</summary>
    private static Task InlineInvoke(Action action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }

    private static UsagePoller CreatePoller(IUsageSource source) =>
        new(source, InlineInvoke, TimeSpan.FromHours(1), () => s_now);

    [Fact]
    public async Task RefreshNowAsync_joins_the_poll_in_progress_instead_of_starting_another()
    {
        var source = new GatedSource(GoodText);
        using var poller = CreatePoller(source);
        var updates = 0;
        poller.Updated += (_, _) => Interlocked.Increment(ref updates);

        var first = poller.RefreshNowAsync();
        await source.Started.WaitAsync(TimeSpan.FromSeconds(5));
        var second = poller.RefreshNowAsync();

        Assert.Same(first, second);
        source.Release();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, source.Calls);
        Assert.Equal(1, updates);
    }

    [Fact]
    public async Task A_new_refresh_after_completion_polls_again()
    {
        var source = new GatedSource(GoodText);
        source.Release();
        using var poller = CreatePoller(source);

        await poller.RefreshNowAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await poller.RefreshNowAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Successful_poll_raises_Updated_with_parsed_snapshot()
    {
        var source = new GatedSource(GoodText);
        source.Release();
        using var poller = CreatePoller(source);
        UsageSnapshot? snapshot = null;
        poller.Updated += (_, s) => snapshot = s;

        await poller.RefreshNowAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot.Session!.Percent);
        Assert.Equal(s_now, snapshot.Taken);
    }

    [Fact]
    public async Task Unparsable_output_raises_Failed_with_raw_text()
    {
        var source = new GatedSource("Not logged in. Run claude /login.");
        source.Release();
        using var poller = CreatePoller(source);
        UsagePollFailure? failure = null;
        poller.Failed += (_, f) => failure = f;

        await poller.RefreshNowAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(failure);
        Assert.Equal("Not logged in. Run claude /login.", failure.RawText);
        Assert.IsType<UsageParseException>(failure.Error);
    }

    [Fact]
    public async Task Source_exception_raises_Failed_with_its_raw_output()
    {
        var source = new GatedSource(new UsageSourceException("claude.exe exited with code 1") { RawOutput = "boom" });
        source.Release();
        using var poller = CreatePoller(source);
        UsagePollFailure? failure = null;
        poller.Failed += (_, f) => failure = f;

        await poller.RefreshNowAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(failure);
        Assert.Equal("claude.exe exited with code 1", failure.Message);
        Assert.Equal("boom", failure.RawText);
    }

    [Fact]
    public async Task Dispose_cancels_the_in_flight_poll_and_waits_for_it()
    {
        var source = new GatedSource(GoodText);
        var poller = CreatePoller(source);
        var events = 0;
        poller.Updated += (_, _) => events++;
        poller.Failed += (_, _) => events++;

        var poll = poller.RefreshNowAsync();
        await source.Started.WaitAsync(TimeSpan.FromSeconds(5));
        poller.Dispose();

        // The source honours its token, so Dispose returned only after the poll ended.
        Assert.True(poll.IsCompleted);
        Assert.True(source.SawCancellation);
        Assert.Equal(0, events);
    }

    [Fact]
    public async Task No_events_after_Dispose_even_if_the_source_ignores_cancellation()
    {
        var source = new GatedSource(GoodText, honourCancellation: false);
        var poller = CreatePoller(source);
        var events = 0;
        poller.Updated += (_, _) => events++;
        poller.Failed += (_, _) => events++;

        var poll = poller.RefreshNowAsync();
        await source.Started.WaitAsync(TimeSpan.FromSeconds(5));
        _ = Task.Delay(200).ContinueWith(_ => source.Release(), TaskScheduler.Default);
        poller.Dispose();
        await poll.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, events);
        await poller.RefreshNowAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, events);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Changing_the_interval_applies_to_the_running_timer()
    {
        var source = new CountingSource(GoodText);
        using var poller = CreatePoller(source);

        poller.Start();
        await source.WaitForCallsAsync(1);

        // Started with an hour between polls; a second poll only comes if the new interval took effect.
        poller.Interval = TimeSpan.FromMilliseconds(50);
        await source.WaitForCallsAsync(2);

        Assert.Equal(TimeSpan.FromMilliseconds(50), poller.Interval);
    }

    /// <summary>Fake source that answers straight away and counts its calls.</summary>
    private sealed class CountingSource(string text) : IUsageSource
    {
        private int _calls;

        public async Task WaitForCallsAsync(int calls)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref _calls) < calls)
            {
                Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {calls} poll(s).");
                await Task.Delay(10);
            }
        }

        public Task<string> FetchRawAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(text);
        }
    }

    /// <summary>Fake source whose fetch blocks until <see cref="Release"/> is called.</summary>
    private sealed class GatedSource : IUsageSource
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string? _text;
        private readonly Exception? _error;
        private readonly bool _honourCancellation;
        private int _calls;

        public GatedSource(string text, bool honourCancellation = true)
        {
            _text = text;
            _honourCancellation = honourCancellation;
        }

        public GatedSource(Exception error)
        {
            _error = error;
            _honourCancellation = true;
        }

        public Task Started => _started.Task;

        public int Calls => Volatile.Read(ref _calls);

        public bool SawCancellation { get; private set; }

        public void Release() => _gate.TrySetResult();

        public async Task<string> FetchRawAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            _started.TrySetResult();
            try
            {
                await (_honourCancellation ? _gate.Task.WaitAsync(cancellationToken) : _gate.Task);
            }
            catch (OperationCanceledException)
            {
                SawCancellation = true;
                throw;
            }

            return _error is null ? _text! : throw _error;
        }
    }
}
