using ClaudeUsage.Usage;

namespace ClaudeUsage.Tests;

public sealed class UsageOutputParserTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 18, 21, 5, 0, TimeSpan.FromHours(2));
    private static readonly TimeZoneInfo s_brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "usage-2026-10-18.txt"));

    [Fact]
    public void Parses_all_three_buckets_from_real_output()
    {
        var text = Fixture();

        var snapshot = UsageOutputParser.Parse(text, s_now, TimeZoneInfo.Utc);

        Assert.Equal(s_now, snapshot.Taken);
        Assert.Equal(text, snapshot.RawText);
        Assert.Equal(new UsageBucket(3, new DateTimeOffset(2026, 10, 19, 2, 0, 0, TimeSpan.FromHours(2))), snapshot.Session);
        Assert.Equal(new UsageBucket(17, new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(2))), snapshot.Week);
        Assert.Equal(new UsageBucket(22, new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(2))), snapshot.ModelWeek);
        Assert.Equal(TimeSpan.FromHours(2), snapshot.Session!.ResetsAt!.Value.Offset);
        Assert.Equal("Fable", snapshot.ModelName);
        Assert.Equal("Fable week", snapshot.ModelWeekLabel);
    }

    [Theory]
    [InlineData("Opus", "Opus week")]
    [InlineData("Fable 5.1", "Fable 5.1 week")]
    [InlineData("  Sonnet   4  ", "Sonnet 4 week")]
    public void Per_model_week_takes_any_model_name(string model, string label)
    {
        var snapshot = UsageOutputParser.Parse(
            "Current week (all models): 17% used · resets Oct 20, 10am (Europe/Brussels)\n" +
            $"Current week ({model}): 31% used · resets Oct 20, 10am (Europe/Brussels)\n",
            s_now);

        Assert.Equal(17, snapshot.Week!.Percent);
        Assert.Equal(31, snapshot.ModelWeek!.Percent);
        Assert.Equal(label, snapshot.ModelWeekLabel);
    }

    [Fact]
    public void Unknown_extra_lines_are_ignored()
    {
        var text = Fixture().Replace(
            "Current week (Fable): 22% used · resets Oct 20, 10am (Europe/Brussels)",
            "Current week (Fable): 22% used · resets Oct 20, 10am (Europe/Brussels)\n" +
            "Current week (Opus): 64% used · resets Oct 20, 10am (Europe/Brussels)\n" +
            "Current month (all models): 9% used",
            StringComparison.Ordinal);

        var snapshot = UsageOutputParser.Parse(text, s_now);

        Assert.Equal(3, snapshot.Session!.Percent);
        Assert.Equal(17, snapshot.Week!.Percent);
        Assert.Equal(22, snapshot.ModelWeek!.Percent);
        Assert.Equal("Fable", snapshot.ModelName);
    }

    [Fact]
    public void Model_week_alone_is_enough()
    {
        var snapshot = UsageOutputParser.Parse("Current week (Opus): 5% used", s_now);

        Assert.Null(snapshot.Session);
        Assert.Null(snapshot.Week);
        Assert.Equal(new UsageBucket(5, null), snapshot.ModelWeek);
    }

    [Fact]
    public void Disappeared_buckets_are_reported_once()
    {
        var full = UsageOutputParser.Parse(Fixture(), s_now);
        var withoutModel = full with { ModelWeek = null, ModelName = null };

        Assert.Equal(["Fable week"], UsageSnapshot.DisappearedBuckets(full, withoutModel));
        Assert.Empty(UsageSnapshot.DisappearedBuckets(withoutModel, withoutModel));
        Assert.Empty(UsageSnapshot.DisappearedBuckets(null, withoutModel));
        Assert.Equal(["Session", "Week"], UsageSnapshot.DisappearedBuckets(full, full with { Session = null, Week = null }));
    }

    [Fact]
    public void Missing_line_gives_null_bucket()
    {
        var text = string.Join('\n', Fixture().Split('\n').Where(l => !l.Contains("(Fable)", StringComparison.Ordinal)));

        var snapshot = UsageOutputParser.Parse(text, s_now);

        Assert.NotNull(snapshot.Session);
        Assert.NotNull(snapshot.Week);
        Assert.Null(snapshot.ModelWeek);
    }

    [Fact]
    public void No_zone_suffix_uses_default_zone()
    {
        var snapshot = UsageOutputParser.Parse("Current session: 3% used · resets Oct 19, 2am", s_now, s_brussels);

        Assert.Equal(new DateTimeOffset(2026, 10, 19, 2, 0, 0, TimeSpan.FromHours(2)), snapshot.Session!.ResetsAt);
    }

    [Fact]
    public void Unknown_zone_falls_back_to_default_zone()
    {
        var snapshot = UsageOutputParser.Parse(
            "Current session: 3% used · resets Oct 19, 2am (Not/AZone)", s_now, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 10, 19, 2, 0, 0, TimeSpan.Zero), snapshot.Session!.ResetsAt);
    }

    [Theory]
    [InlineData("Oct 19, 2:30pm", 19, 14, 30)]
    [InlineData("Oct 19, 12am", 19, 0, 0)]
    [InlineData("Oct 19, 12pm", 19, 12, 0)]
    [InlineData("Oct 20, 10am", 20, 10, 0)]
    [InlineData("Oct 19 2:30 PM", 19, 14, 30)]
    public void Parses_time_formats(string when, int day, int hour, int minute)
    {
        var snapshot = UsageOutputParser.Parse(
            $"Current week (all models): 17% used · resets {when} (Europe/Brussels)", s_now);

        Assert.Equal(new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(2)), snapshot.Week!.ResetsAt);
    }

    [Fact]
    public void Uses_winter_offset_after_dst_ends()
    {
        // DST in Europe ends on 2026-10-25 at 03:00 CEST.
        var snapshot = UsageOutputParser.Parse(
            "Current week (all models): 17% used · resets Oct 26, 10am (Europe/Brussels)", s_now);

        Assert.Equal(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(1)), snapshot.Week!.ResetsAt);
    }

    [Fact]
    public void Year_rolls_over_when_date_is_in_the_past()
    {
        var now = new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.FromHours(1));

        var snapshot = UsageOutputParser.Parse("Current session: 40% used · resets Jan 1, 2am (Europe/Brussels)", now);

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 2, 0, 0, TimeSpan.FromHours(1)), snapshot.Session!.ResetsAt);
    }

    [Fact]
    public void Just_passed_reset_on_new_year_is_previous_year()
    {
        var now = new DateTimeOffset(2027, 1, 1, 0, 30, 0, TimeSpan.FromHours(1));

        var snapshot = UsageOutputParser.Parse("Current session: 99% used · resets Dec 31, 11pm (Europe/Brussels)", now);

        Assert.Equal(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.FromHours(1)), snapshot.Session!.ResetsAt);
    }

    [Fact]
    public void Feb_29_resolves_to_the_next_leap_year_occurrence()
    {
        var now = new DateTimeOffset(2027, 2, 27, 12, 0, 0, TimeSpan.FromHours(1));

        var snapshot = UsageOutputParser.Parse("Current week (all models): 1% used · resets Feb 29, 10am (Europe/Brussels)", now);

        Assert.Equal(new DateTimeOffset(2028, 2, 29, 10, 0, 0, TimeSpan.FromHours(1)), snapshot.Week!.ResetsAt);
    }

    [Fact]
    public void Recent_past_reset_keeps_current_year()
    {
        // A few hours stale is still this year (the 1-day grace in the year rule).
        var snapshot = UsageOutputParser.Parse("Current session: 3% used · resets Oct 18, 6pm (Europe/Brussels)", s_now);

        Assert.Equal(new DateTimeOffset(2026, 10, 18, 18, 0, 0, TimeSpan.FromHours(2)), snapshot.Session!.ResetsAt);
    }

    [Fact]
    public void Percent_over_100_is_stored_as_is()
    {
        var snapshot = UsageOutputParser.Parse("Current session: 105% used · resets Oct 19, 2am (Europe/Brussels)", s_now);

        Assert.Equal(105, snapshot.Session!.Percent);
    }

    [Fact]
    public void Line_without_reset_gives_null_reset()
    {
        var snapshot = UsageOutputParser.Parse("Current session: 0% used", s_now);

        Assert.Equal(new UsageBucket(0, null), snapshot.Session);
    }

    [Fact]
    public void Tolerates_crlf_and_extra_spacing()
    {
        var snapshot = UsageOutputParser.Parse(
            "  Current  week  (Fable) :  22 %  used  ·  resets  Oct 20, 10am  (Europe/Brussels)  \r\n", s_now);

        Assert.Equal(new UsageBucket(22, new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(2))), snapshot.ModelWeek);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Not logged in. Run claude /login.")]
    [InlineData("Current session: lots used")]
    public void Garbage_throws(string text)
    {
        var ex = Assert.Throws<UsageParseException>(() => UsageOutputParser.Parse(text, s_now));
        Assert.Equal(text, ex.RawText);
    }

    [Fact]
    public void ToZoned_handles_spring_forward_gap()
    {
        // 2027-03-28 02:30 does not exist in Brussels; it becomes 03:30 CEST.
        var result = UsageOutputParser.ToZoned(new DateTime(2027, 3, 28, 2, 30, 0), s_brussels);

        Assert.Equal(new DateTimeOffset(2027, 3, 28, 3, 30, 0, TimeSpan.FromHours(2)), result);
    }
}
