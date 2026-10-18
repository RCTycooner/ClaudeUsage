using ClaudeUsage.Storage;
using ClaudeUsage.UI;
using ClaudeUsage.Usage;

namespace ClaudeUsage.Tests;

public sealed class MainViewModelTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 18, 21, 5, 0, TimeSpan.FromHours(2));

    private static UsageSnapshot Snapshot(int s, int w, int? f) => new(
        s_now.AddSeconds(-12),
        new UsageBucket(s, s_now.AddMinutes(4 * 60 + 55)),
        new UsageBucket(w, s_now.AddHours(38)),
        f is { } fv ? new UsageBucket(fv, null) : null,
        "raw");

    [Theory]
    [InlineData(-5, UsageLevel.Ok)]
    [InlineData(0, UsageLevel.Ok)]
    [InlineData(69, UsageLevel.Ok)]
    [InlineData(70, UsageLevel.Warning)]
    [InlineData(89, UsageLevel.Warning)]
    [InlineData(90, UsageLevel.Critical)]
    [InlineData(130, UsageLevel.Critical)]
    public void Level_thresholds(int percent, UsageLevel expected)
    {
        Assert.Equal(expected, Palette.LevelFor(percent));
    }

    [Fact]
    public void Brushes_follow_levels()
    {
        Assert.Same(Palette.Ok, Palette.BrushForPercent(10));
        Assert.Same(Palette.Warning, Palette.BrushForPercent(75));
        Assert.Same(Palette.Critical, Palette.BrushForPercent(95));
        Assert.True(Palette.Ok.IsFrozen);
    }

    [Fact]
    public void Row_formats_percent_bar_and_countdown()
    {
        var row = new BucketRow("Week", Palette.WeekSeries);

        row.Update(new UsageBucket(130, s_now.AddHours(38)), s_now);

        Assert.Equal("100 %", row.PercentText);
        Assert.Equal(100, row.BarValue);
        Assert.Same(Palette.Critical, row.BarBrush);
        Assert.Equal("resets in 1d 14h", row.Countdown);
    }

    [Fact]
    public void Missing_bucket_shows_a_dash()
    {
        var row = new BucketRow("Fable week", Palette.ModelSeries);
        row.Update(new UsageBucket(50, null), s_now);

        row.Update(null, s_now);

        Assert.Equal("—", row.PercentText);
        Assert.Equal(0, row.BarValue);
        Assert.Equal("", row.Countdown);
    }

    [Fact]
    public void Rows_and_footer_reflect_the_latest_snapshot()
    {
        var state = new UsageState();
        var vm = new MainViewModel(state, () => s_now);

        state.SetSnapshot(Snapshot(3, 17, null), []);

        Assert.Equal(["Session", "Week", "Model week"], vm.Rows.Select(r => r.Label));
        Assert.Equal("3 %", vm.Session.PercentText);
        Assert.Equal("resets in 4h 55m", vm.Session.Countdown);
        Assert.Equal("—", vm.ModelWeek.PercentText);
        Assert.Equal("updated 12 s ago", vm.FooterText);
        Assert.False(vm.IsError);
        Assert.Equal("", vm.FooterHint);
    }

    [Fact]
    public void Model_row_is_labelled_with_the_model_name()
    {
        var state = new UsageState();
        var vm = new MainViewModel(state, () => s_now);

        state.SetSnapshot(Snapshot(3, 17, 40) with { ModelName = "Opus" }, []);

        Assert.Equal("Opus week", vm.ModelWeek.Label);
        Assert.Equal("40 %", vm.ModelWeek.PercentText);
    }

    [Fact]
    public void Countdowns_and_footer_advance_on_refresh_without_new_data()
    {
        var now = s_now;
        var state = new UsageState();
        var vm = new MainViewModel(state, () => now);
        state.SetSnapshot(Snapshot(3, 17, 22), []);

        now = now.AddMinutes(5);
        vm.Refresh();

        Assert.Equal("updated 5 min ago", vm.FooterText);
        Assert.Equal("resets in 4h 50m", vm.Session.Countdown);
        Assert.Equal(now, vm.Now);
    }

    [Fact]
    public void Failure_shows_error_and_copies_raw_text()
    {
        var state = new UsageState();
        string? copied = null;
        var vm = new MainViewModel(state, () => s_now, text =>
        {
            copied = text;
            return true;
        });
        state.SetSnapshot(Snapshot(3, 17, 22), []);

        state.SetFailure(new UsagePollFailure(s_now, "Could not parse /usage output.\nmore", "garbled", new InvalidOperationException()));

        Assert.True(vm.IsError);
        Assert.True(vm.CanCopy);
        Assert.Equal("Could not parse /usage output.", vm.FooterText);
        Assert.Equal(FooterFormat.CopyHint, vm.FooterHint);
        Assert.Equal("3 %", vm.Session.PercentText);

        vm.CopyRawText();

        Assert.Equal("garbled", copied);
        Assert.Equal(FooterFormat.Copied, vm.FooterHint);
    }

    [Fact]
    public void Failed_copy_says_so()
    {
        var state = new UsageState();
        var vm = new MainViewModel(state, () => s_now, _ => false);
        state.SetFailure(new UsagePollFailure(s_now, "bad", "raw", new InvalidOperationException()));

        vm.CopyRawText();

        Assert.Equal(FooterFormat.CopyFailed, vm.FooterHint);
    }

    [Fact]
    public void Failure_without_raw_text_cannot_copy()
    {
        var state = new UsageState();
        var vm = new MainViewModel(state, () => s_now, _ => throw new InvalidOperationException("must not copy"));
        state.SetFailure(new UsagePollFailure(s_now, "claude.exe not found", null, new InvalidOperationException()));

        vm.CopyRawText();

        Assert.False(vm.CanCopy);
        Assert.Equal("claude.exe not found", vm.FooterText);
        Assert.Equal("", vm.FooterHint);
    }

    [Fact]
    public void Samples_track_history()
    {
        var state = new UsageState();
        var vm = new MainViewModel(state, () => s_now);
        var history = new[] { UsageSample.FromSnapshot(Snapshot(3, 17, 22)) };

        state.SetSnapshot(Snapshot(3, 17, 22), history);

        Assert.Same(history, vm.Samples);
    }

    [Theory]
    [InlineData(0, "updated 0 s ago")]
    [InlineData(59, "updated 59 s ago")]
    [InlineData(60, "updated 1 min ago")]
    [InlineData(3599, "updated 59 min ago")]
    [InlineData(7200, "updated 2 h ago")]
    [InlineData(90000, "updated 1 d ago")]
    [InlineData(-30, "updated 0 s ago")]
    public void Footer_age(int secondsAgo, string expected)
    {
        Assert.Equal(expected, FooterFormat.Updated(s_now.AddSeconds(-secondsAgo), s_now));
    }

    [Fact]
    public void Footer_without_data_is_waiting()
    {
        Assert.Equal(FooterFormat.Waiting, FooterFormat.Updated(null, s_now));
    }

    [Fact]
    public void Synthetic_history_covers_the_week_with_gaps()
    {
        var samples = SnapshotRenderer.SyntheticHistory(s_now);

        Assert.True(samples.Count > 5000);
        Assert.All(samples, s => Assert.InRange(s.Taken, s_now - ChartMath.Range, s_now));
        Assert.True(ChartMath.Segment(samples, s => s.Week, s_now - ChartMath.Range, s_now, ChartMath.MaxGap).Count >= 3);
    }
}
