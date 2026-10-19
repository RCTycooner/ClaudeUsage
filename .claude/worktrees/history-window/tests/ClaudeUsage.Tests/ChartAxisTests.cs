using System.Globalization;
using ClaudeUsage.Storage;
using ClaudeUsage.UI;

namespace ClaudeUsage.Tests;

/// <summary>Windowing, thinning and time-axis helpers added for the zoomable history chart.</summary>
public sealed class ChartAxisTests
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static UsageSample At(double minutes) => new(s_t0.AddMinutes(minutes), 1, 2, 3, null, null, null);

    private static ChartPoint Point(double minutes, double percent) => new(s_t0.AddMinutes(minutes), percent);

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(0.5, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(10, 3)]
    public void Lower_bound_finds_the_first_sample_at_or_after(double minutes, int expected)
    {
        UsageSample[] samples = [At(0), At(1), At(2)];

        Assert.Equal(expected, ChartMath.LowerBound(samples, s_t0.AddMinutes(minutes)));
    }

    [Fact]
    public void Segment_only_reads_the_window()
    {
        var samples = Enumerable.Range(0, 100).Select(i => At(i)).ToList();

        var only = Assert.Single(ChartMath.Segment(samples, s => s.Session, s_t0.AddMinutes(10), s_t0.AddMinutes(20), ChartMath.MaxGap));

        Assert.Equal(11, only.Count);
        Assert.Equal(s_t0.AddMinutes(10), only[0].Taken);
        Assert.Equal(s_t0.AddMinutes(20), only[^1].Taken);
    }

    [Fact]
    public void Decimate_keeps_first_min_max_last_of_each_slot_in_time_order()
    {
        ChartPoint[] points =
        [
            Point(0, 50), Point(1, 90), Point(2, 10), Point(3, 40), Point(4, 60), // slot 0
            Point(10, 5), Point(11, 6), Point(12, 7), Point(13, 8), Point(14, 9), // slot 1
        ];

        var thinned = ChartMath.Decimate(points, s_t0, TimeSpan.FromMinutes(10));

        Assert.Equal([50d, 90, 10, 60, 5, 9], thinned.Select(p => p.Percent));
        Assert.Equal(thinned.OrderBy(p => p.Taken), thinned);
    }

    [Fact]
    public void Decimate_leaves_short_runs_alone()
    {
        ChartPoint[] points = [Point(0, 1), Point(1, 2), Point(2, 3)];

        Assert.Same(points, ChartMath.Decimate(points, s_t0, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Decimate_with_fine_slots_keeps_every_point()
    {
        var points = Enumerable.Range(0, 20).Select(i => Point(i, i % 7)).ToList();

        Assert.Equal(points, ChartMath.Decimate(points, s_t0, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Decimate_handles_points_before_the_origin()
    {
        var points = Enumerable.Range(-10, 20).Select(i => Point(i, 50)).ToList();

        var thinned = ChartMath.Decimate(points, s_t0, TimeSpan.FromMinutes(10));

        // Two slots ([-10, 0) and [0, 10)), each reduced to its first and last point.
        Assert.Equal(4, thinned.Count);
    }

    [Theory]
    [InlineData(1, TickUnit.Minute, 120)] // 900 px a day: 1 h = 37.5 px is too narrow, 2 h = 75 px fits
    [InlineData(7, TickUnit.Minute, 720)] // 12 h = 64 px
    [InlineData(30, TickUnit.Day, 2)]     // 30 px a day is too narrow for dated day labels
    [InlineData(90, TickUnit.Week, 1)]
    [InlineData(400, TickUnit.Month, 1)]
    public void Step_is_the_finest_that_fits_labels(double days, TickUnit unit, int count)
    {
        var step = ChartMath.ChooseStep(TimeSpan.FromDays(days), 900, minDayWidth: 48, minLabelWidth: 56);

        Assert.Equal(new TickStep(unit, count), step);
    }

    [Fact]
    public void Widget_week_keeps_weekday_ticks()
    {
        Assert.Equal(TickStep.OneDay, ChartMath.ChooseStep(ChartMath.Range, 280, minDayWidth: 22, minLabelWidth: 56));
    }

    [Fact]
    public void Hour_ticks_are_aligned_to_the_step()
    {
        var from = new DateTimeOffset(2026, 10, 18, 10, 20, 0, TimeSpan.Zero);

        var ticks = ChartMath.Ticks(from, from.AddHours(7), TimeZoneInfo.Utc, new TickStep(TickUnit.Minute, 180));

        Assert.Equal([12, 15], ticks.Select(t => t.Hour));
    }

    [Fact]
    public void Week_ticks_fall_on_the_first_day_of_the_week()
    {
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero); // a Thursday

        var ticks = ChartMath.Ticks(from, from.AddDays(30), TimeZoneInfo.Utc, new TickStep(TickUnit.Week, 1), DayOfWeek.Monday);

        Assert.Equal([5, 12, 19, 26], ticks.Select(t => t.Day));
        Assert.All(ticks, t => Assert.Equal(DayOfWeek.Monday, t.DayOfWeek));
    }

    [Fact]
    public void Month_ticks_are_the_firsts()
    {
        var from = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

        var ticks = ChartMath.Ticks(from, from.AddDays(90), TimeZoneInfo.Utc, new TickStep(TickUnit.Month, 1));

        Assert.Equal([9, 10, 11], ticks.Select(t => t.Month));
        Assert.All(ticks, t => Assert.Equal(1, t.Day));
    }

    [Fact]
    public void Two_day_ticks_stay_put_when_the_window_moves()
    {
        var from = new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero);
        var step = new TickStep(TickUnit.Day, 2);

        var a = ChartMath.Ticks(from, from.AddDays(10), TimeZoneInfo.Utc, step);
        var b = ChartMath.Ticks(from.AddDays(1), from.AddDays(11), TimeZoneInfo.Utc, step);

        Assert.Equal(a.Skip(1), b.Take(a.Count - 1));
    }

    [Fact]
    public void Hour_ticks_skip_the_repeated_hour_when_clocks_go_back()
    {
        var brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        var from = new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.FromHours(2));

        var ticks = ChartMath.Ticks(from, from.AddHours(8), brussels, new TickStep(TickUnit.Minute, 60));

        Assert.Equal(ticks.Distinct().OrderBy(t => t), ticks);
    }

    [Fact]
    public void Moment_uses_the_short_time_pattern()
    {
        var t = new DateTimeOffset(2026, 10, 19, 14, 30, 0, TimeSpan.Zero);

        Assert.Equal("Mon 19 Oct 2:30 PM", ChartMath.FormatMoment(t, TimeZoneInfo.Utc, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("ma 19 okt 14:30", ChartMath.FormatMoment(t, TimeZoneInfo.Utc, CultureInfo.GetCultureInfo("nl-BE")));
    }
}
