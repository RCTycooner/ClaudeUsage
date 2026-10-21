using ClaudeUsage.Storage;
using ClaudeUsage.UI;
using ClaudeUsage.Usage;

namespace ClaudeUsage.Tests;

public sealed class LimitForecastTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 18, 21, 0, 0, TimeSpan.FromHours(2));

    private static UsageSample Week(DateTimeOffset taken, int week) =>
        new(taken, null, week, null, null, s_now.AddDays(2), null);

    [Fact]
    public void Session_extrapolates_the_rate_since_it_started()
    {
        // Started 2 h ago (resets in 3 h), 40 % used: 20 %/h, so 60 % more takes 3 h.
        var bucket = new UsageBucket(40, s_now.AddHours(3));

        Assert.Equal(s_now.AddHours(3), LimitForecast.Session(bucket, s_now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(120)]
    public void Session_without_usage_or_already_full_has_no_estimate(int percent)
    {
        Assert.Null(LimitForecast.Session(new UsageBucket(percent, s_now.AddHours(3)), s_now));
    }

    [Fact]
    public void Session_just_started_or_without_reset_has_no_estimate()
    {
        Assert.Null(LimitForecast.Session(new UsageBucket(5, s_now.AddHours(4.9)), s_now));
        Assert.Null(LimitForecast.Session(new UsageBucket(5, null), s_now));
    }

    [Fact]
    public void Weekly_uses_the_rate_over_the_last_seven_days()
    {
        // 30 % added over 3 days = 10 %/day; 60 % left takes 6 days.
        var history = new[] { Week(s_now.AddDays(-3), 10), Week(s_now.AddDays(-1), 25), Week(s_now, 40) };

        var eta = LimitForecast.Weekly(new UsageBucket(40, s_now.AddDays(2)), history, s => s.Week, s_now);

        Assert.Equal(s_now.AddDays(6), eta);
    }

    [Fact]
    public void Weekly_counts_usage_after_a_reset_and_ignores_older_samples()
    {
        var history = new[]
        {
            Week(s_now.AddDays(-9), 0), // outside the window
            Week(s_now.AddDays(-4), 70),
            Week(s_now.AddDays(-2), 90),
            Week(s_now.AddDays(-1), 10), // reset in between: 10 used since
            Week(s_now, 20),
        };

        var eta = LimitForecast.Weekly(new UsageBucket(20, s_now.AddDays(6)), history, s => s.Week, s_now);

        // 20 + 10 + 10 = 40 % over 4 days = 10 %/day; 80 % left takes 8 days.
        Assert.Equal(s_now.AddDays(8), eta);
    }

    [Fact]
    public void Weekly_without_enough_history_or_usage_has_no_estimate()
    {
        var bucket = new UsageBucket(40, s_now.AddDays(2));

        Assert.Null(LimitForecast.Weekly(bucket, [Week(s_now, 40)], s => s.Week, s_now));
        Assert.Null(LimitForecast.Weekly(bucket, [Week(s_now.AddDays(-1), 40), Week(s_now, 40)], s => s.Week, s_now));
        Assert.Null(LimitForecast.Weekly(bucket, [Week(s_now.AddDays(-1), 10), Week(s_now, 40)], s => s.ModelWeek, s_now));
    }

    [Fact]
    public void Row_shows_estimate_in_red_when_it_comes_before_the_reset()
    {
        var row = new BucketRow("Session", Palette.SessionSeries);

        row.Update(new UsageBucket(40, s_now.AddHours(3)), s_now, s_now.AddHours(2));

        Assert.Equal("resets in 3h 0m", row.Countdown);
        Assert.Equal(" · 100 % in 2h 0m", row.Forecast);
        Assert.Same(Palette.Critical, row.ForecastBrush);
    }

    [Fact]
    public void Row_shows_estimate_muted_when_the_reset_comes_first()
    {
        var row = new BucketRow("Week", Palette.WeekSeries);

        row.Update(new UsageBucket(40, s_now.AddDays(2)), s_now, s_now.AddDays(6));

        Assert.Equal(" · 100 % in 6d 0h", row.Forecast);
        Assert.Same(Palette.TextMuted, row.ForecastBrush);

        row.Update(new UsageBucket(40, s_now.AddDays(2)), s_now);

        Assert.Equal("", row.Forecast);
    }
}
