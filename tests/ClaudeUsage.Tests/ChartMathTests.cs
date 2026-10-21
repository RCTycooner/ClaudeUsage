using ClaudeUsage.Storage;
using ClaudeUsage.UI;

namespace ClaudeUsage.Tests;

public sealed class ChartMathTests
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 18, 12, 0, 0, TimeSpan.FromHours(2));

    private static UsageSample At(double minutes, int? session, int? week = 10, int? fable = 20) =>
        new(s_t0.AddMinutes(minutes), session, week, fable, null, null, null);

    private static IReadOnlyList<IReadOnlyList<ChartPoint>> SessionSegments(params UsageSample[] samples) =>
        ChartMath.Segment(samples, s => s.Session, s_t0.AddDays(-7), s_t0.AddDays(7), ChartMath.MaxGap);

    [Fact]
    public void Consecutive_samples_form_one_segment()
    {
        var segments = SessionSegments(At(0, 1), At(1, 2), At(2, 3));

        var only = Assert.Single(segments);
        Assert.Equal([1d, 2d, 3d], only.Select(p => p.Percent));
    }

    [Fact]
    public void Gap_longer_than_fifteen_minutes_breaks_the_line()
    {
        var segments = SessionSegments(At(0, 1), At(5, 2), At(20.5, 3), At(25.5, 4));

        Assert.Equal(2, segments.Count);
        Assert.Equal(2, segments[0].Count);
        Assert.Equal(2, segments[1].Count);
    }

    [Fact]
    public void Gap_of_exactly_fifteen_minutes_is_joined()
    {
        Assert.Single(SessionSegments(At(0, 1), At(15, 2)));
    }

    [Fact]
    public void Null_values_are_skipped_without_breaking_a_short_run()
    {
        var segments = SessionSegments(At(0, 1), At(1, null), At(2, 3));

        var only = Assert.Single(segments);
        Assert.Equal([1d, 3d], only.Select(p => p.Percent));
    }

    [Fact]
    public void Nulls_spanning_more_than_the_gap_break_the_line()
    {
        var segments = SessionSegments(At(0, 1), At(5, null), At(10, null), At(15, null), At(20, 4));

        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void Values_are_clamped_to_0_100()
    {
        var segments = SessionSegments(At(0, -4), At(1, 130));

        Assert.Equal([0d, 100d], Assert.Single(segments).Select(p => p.Percent));
    }

    [Fact]
    public void Samples_outside_the_window_are_dropped()
    {
        var samples = new[] { At(-10, 1), At(0, 2), At(1, 3), At(10, 4) };

        var segments = ChartMath.Segment(samples, s => s.Session, s_t0, s_t0.AddMinutes(5), ChartMath.MaxGap);

        Assert.Equal([2d, 3d], Assert.Single(segments).Select(p => p.Percent));
    }

    [Fact]
    public void Empty_history_has_no_segments()
    {
        Assert.Empty(SessionSegments());
    }

    [Fact]
    public void Count_plottable_ignores_empty_samples_and_out_of_range()
    {
        var samples = new[] { At(-100, 1), At(0, null, null, null), At(1, null, null, 5), At(2, 3) };

        Assert.Equal(2, ChartMath.CountPlottable(samples, s_t0.AddMinutes(-1), s_t0.AddMinutes(10)));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(84, 20 + 140)]
    [InlineData(168, 20 + 280)]
    public void Map_x_spans_the_week_left_to_right(int hours, double expected)
    {
        var from = s_t0;
        var to = s_t0.AddDays(7);

        Assert.Equal(expected, ChartMath.MapX(s_t0.AddHours(hours), from, to, 20, 280), 6);
    }

    [Fact]
    public void Map_x_with_empty_range_pins_to_the_right_edge()
    {
        Assert.Equal(300, ChartMath.MapX(s_t0, s_t0, s_t0, 20, 280));
    }

    [Theory]
    [InlineData(100, 5)]
    [InlineData(0, 95)]
    [InlineData(50, 50)]
    [InlineData(150, 5)]
    [InlineData(-20, 95)]
    public void Map_y_puts_100_at_top_and_clamps(double percent, double expected)
    {
        Assert.Equal(expected, ChartMath.MapY(percent, 5, 90), 6);
    }

    [Fact]
    public void Day_ticks_are_local_midnights_inside_the_range()
    {
        var from = new DateTimeOffset(2026, 10, 11, 21, 5, 0, TimeSpan.Zero);
        var to = from.AddDays(7);

        var ticks = ChartMath.DayTicks(from, to, TimeZoneInfo.Utc);

        Assert.Equal(7, ticks.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), ticks[0]);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero), ticks[^1]);
    }

    [Fact]
    public void Day_ticks_exclude_a_midnight_at_the_left_edge()
    {
        var from = new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

        var ticks = ChartMath.DayTicks(from, from.AddDays(7), TimeZoneInfo.Utc);

        Assert.Equal(7, ticks.Count);
        Assert.Equal(from.AddDays(1), ticks[0]);
        Assert.Equal(from.AddDays(7), ticks[^1]);
    }

    [Fact]
    public void Day_ticks_follow_daylight_saving_changes()
    {
        var brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        var from = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(2));

        var ticks = ChartMath.DayTicks(from, from.AddDays(7), brussels);

        // DST ends on 2026-10-25: midnights before are +02:00, after are +01:00.
        Assert.All(ticks, t => Assert.Equal(TimeSpan.Zero, TimeZoneInfo.ConvertTime(t, brussels).TimeOfDay));
        Assert.Equal(TimeSpan.FromHours(2), ticks[0].Offset);
        Assert.Equal(TimeSpan.FromHours(1), ticks[^1].Offset);
    }
}
