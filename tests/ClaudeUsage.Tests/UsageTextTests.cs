using ClaudeUsage.Usage;

namespace ClaudeUsage.Tests;

public sealed class UsageTextTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 18, 21, 5, 0, TimeSpan.FromHours(2));

    private static UsageSnapshot Snapshot(int s, int w, int? f) => new(
        s_now,
        new UsageBucket(s, s_now.AddMinutes(4 * 60 + 55)),
        new UsageBucket(w, s_now.AddHours(37)),
        f is { } fv ? new UsageBucket(fv, s_now.AddHours(37)) : null,
        "",
        "Fable");

    [Fact]
    public void Tooltip_lists_all_buckets()
    {
        Assert.Equal("Claude: S 3% · W 17% · F 22%", UsageText.Tooltip(Snapshot(3, 17, 22)));
    }

    [Fact]
    public void Tooltip_skips_missing_bucket_and_clamps()
    {
        Assert.Equal("Claude: S 100% · W 0%", UsageText.Tooltip(Snapshot(130, -5, null)));
    }

    [Fact]
    public void Tooltip_without_data()
    {
        Assert.Equal(UsageText.WaitingTooltip, UsageText.Tooltip(null));
    }

    [Fact]
    public void Tooltip_uses_model_initial_or_M()
    {
        Assert.Equal("Claude: S 3% · W 17% · O 22%", UsageText.Tooltip(Snapshot(3, 17, 22) with { ModelName = "Opus" }));
        Assert.Equal("Claude: S 3% · W 17% · M 22%", UsageText.Tooltip(Snapshot(3, 17, 22) with { ModelName = null }));
    }

    [Fact]
    public void Failed_poll_keeps_last_values_behind_a_marker()
    {
        Assert.Equal(
            UsageText.StalePrefix + "Claude: S 3% · W 17% · F 22%",
            UsageText.Tooltip(Snapshot(3, 17, 22), hasError: true));
    }

    [Fact]
    public void Failed_poll_without_any_data_shows_error()
    {
        Assert.Equal(UsageText.ErrorTooltip, UsageText.Tooltip(null, hasError: true));
    }

    [Fact]
    public void Menu_line_has_percent_and_countdown()
    {
        var snapshot = Snapshot(3, 17, 22);

        Assert.Equal("Session  3%  · resets in 4h 55m", UsageText.MenuLine("Session", snapshot.Session, s_now));
        Assert.Equal("Week  17%  · resets in 1d 13h", UsageText.MenuLine("Week", snapshot.Week, s_now));
    }

    [Fact]
    public void Menu_line_for_missing_bucket()
    {
        Assert.Equal("Fable week  —", UsageText.MenuLine("Fable week", null, s_now));
    }

    [Fact]
    public void Menu_line_without_reset_time()
    {
        Assert.Equal("Session  5%", UsageText.MenuLine("Session", new UsageBucket(5, null), s_now));
    }
}
