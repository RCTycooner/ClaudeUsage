using System.Globalization;
using ClaudeUsage.Storage;
using ClaudeUsage.UI;
using ClaudeUsage.Usage;

namespace ClaudeUsage.Tests;

public sealed class ChartViewportTests
{
    private static readonly DateTimeOffset s_end = new(2026, 10, 19, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset s_start = s_end.AddDays(-30);

    private static ChartViewport Week() => new(s_start, s_end, TimeSpan.FromDays(7));

    [Fact]
    public void Starts_live_at_the_end()
    {
        var v = Week();

        Assert.Equal(s_end, v.End);
        Assert.Equal(TimeSpan.FromDays(7), v.Span);
        Assert.True(v.IsLive);
    }

    [Fact]
    public void Pan_is_clamped_to_the_extent()
    {
        var v = Week();

        v.Pan(TimeSpan.FromDays(-100));
        Assert.Equal(s_start, v.Start);
        Assert.False(v.IsLive);

        v.Pan(TimeSpan.FromDays(100));
        Assert.Equal(s_end, v.End);
        Assert.True(v.IsLive);
    }

    [Fact]
    public void Zoom_keeps_the_anchor_time_in_place()
    {
        var v = Week();
        v.Pan(TimeSpan.FromDays(-10));
        var pinned = v.Start + (v.Span * 0.25);

        v.Zoom(0.5, 0.25);

        Assert.Equal(TimeSpan.FromDays(3.5), v.Span);
        Assert.Equal(pinned, v.Start + (v.Span * 0.25));
    }

    [Fact]
    public void Zoom_is_limited_to_one_hour_and_the_whole_extent()
    {
        var v = Week();

        v.Zoom(1e-6);
        Assert.Equal(ChartViewport.MinSpan, v.Span);
        Assert.False(v.CanZoomIn);

        v.Zoom(1e6);
        Assert.Equal(TimeSpan.FromDays(30), v.Span);
        Assert.Equal(s_start, v.Start);
        Assert.False(v.CanZoomOut);
    }

    [Fact]
    public void Zooming_out_near_the_end_stays_live()
    {
        var v = Week();

        v.Zoom(2, 0.5);

        Assert.True(v.IsLive);
        Assert.Equal(TimeSpan.FromDays(14), v.Span);
    }

    [Fact]
    public void Live_window_follows_new_data()
    {
        var v = Week();

        v.SetExtent(s_start, s_end.AddHours(1));

        Assert.Equal(s_end.AddHours(1), v.End);
        Assert.True(v.IsLive);
    }

    [Fact]
    public void Scrolled_back_window_stays_put_when_data_arrives()
    {
        var v = Week();
        v.Pan(TimeSpan.FromDays(-5));
        var start = v.Start;

        v.SetExtent(s_start, s_end.AddHours(1));

        Assert.Equal(start, v.Start);
        Assert.False(v.IsLive);
    }

    [Fact]
    public void Show_last_and_show_all()
    {
        var v = Week();
        v.Pan(TimeSpan.FromDays(-5));

        v.ShowLast(TimeSpan.FromDays(1));
        Assert.Equal(TimeSpan.FromDays(1), v.Span);
        Assert.True(v.IsLive);

        v.ShowLast(TimeSpan.FromDays(365));
        Assert.Equal(TimeSpan.FromDays(30), v.Span);

        v.ShowLast(TimeSpan.FromDays(1));
        v.ShowAll();
        Assert.Equal(s_start, v.Start);
        Assert.Equal(s_end, v.End);
    }

    [Fact]
    public void Extent_shorter_than_the_minimum_span_still_works()
    {
        var v = new ChartViewport(s_end.AddMinutes(-10), s_end, TimeSpan.FromDays(7));

        Assert.Equal(ChartViewport.MinSpan, v.Span);
        Assert.Equal(s_end, v.End);
        Assert.True(v.IsLive);
    }

    [Fact]
    public void History_view_model_covers_all_samples_and_follows_new_polls()
    {
        var now = s_end;
        var samples = new List<UsageSample> { Sample(now.AddDays(-20)), Sample(now.AddMinutes(-1)) };
        var state = new UsageState();
        state.SetSnapshot(samples[^1].ToSnapshot(), samples);
        var main = new MainViewModel(state, () => now);
        var history = new HistoryViewModel(main);

        Assert.Equal(TimeSpan.FromDays(7), history.ViewSpan);
        Assert.Equal(now, history.ViewEnd);
        Assert.Equal(13 * 24, history.ScrollMaximum, 6); // 20 days of extent minus the 7 shown, in hours
        Assert.Equal(13 * 24, history.ScrollValue, 6);

        history.ShowAll();
        Assert.Equal(TimeSpan.FromDays(20), history.ViewSpan);

        history.ShowLast(TimeSpan.FromDays(1));
        now = now.AddMinutes(15);
        state.SetSnapshot(samples[^1].ToSnapshot(), samples);
        Assert.Equal(now, history.ViewEnd);
        Assert.True(history.IsLive);

        history.Pan(-1);
        now = now.AddMinutes(15);
        state.SetSnapshot(samples[^1].ToSnapshot(), samples);
        Assert.Equal(now.AddMinutes(-15).AddDays(-1), history.ViewEnd);
        Assert.False(history.IsLive);

        history.GoLive();
        Assert.Equal(now, history.ViewEnd);
        history.Detach();
    }

    [Fact]
    public void History_view_model_extent_is_at_least_a_week()
    {
        var state = new UsageState();
        var main = new MainViewModel(state, () => s_end);
        var history = new HistoryViewModel(main);

        history.ShowAll();

        Assert.Equal(TimeSpan.FromDays(7), history.ViewSpan);
        Assert.Equal(0, history.ScrollMaximum, 6);
    }

    [Theory]
    [InlineData(30, "30 min")]
    [InlineData(90, "1.5 h")]
    [InlineData(36 * 60, "36 h")]
    [InlineData(7 * 24 * 60, "7 days")]
    [InlineData(84 * 60, "3.5 days")]
    public void Span_text(int minutes, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, HistoryViewModel.FormatSpan(TimeSpan.FromMinutes(minutes)));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static UsageSample Sample(DateTimeOffset taken) => new(taken, 10, 20, 30, null, null, null);
}
