using ClaudeUsage.Usage;

namespace ClaudeUsage.Tests;

public sealed class CountdownFormatterTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 18, 21, 5, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(3 * 60 + 12, "in 3h 12m")]
    [InlineData(38 * 60 + 5, "in 1d 14h")]
    [InlineData(45, "in 45m")]
    [InlineData(60, "in 1h 0m")]
    [InlineData(24 * 60, "in 1d 0h")]
    [InlineData(0, "now")]
    [InlineData(-30, "now")]
    public void Formats_minutes(int minutes, string expected)
    {
        Assert.Equal(expected, CountdownFormatter.Format(s_now.AddMinutes(minutes), s_now));
    }

    [Fact]
    public void Under_a_minute_shows_one_minute()
    {
        Assert.Equal("in 1m", CountdownFormatter.Format(s_now.AddSeconds(20), s_now));
    }

    [Fact]
    public void Seconds_are_truncated()
    {
        Assert.Equal("in 3h 12m", CountdownFormatter.Format(s_now.AddMinutes(192).AddSeconds(59), s_now));
    }

    [Fact]
    public void Null_reset_gives_null()
    {
        Assert.Null(CountdownFormatter.Format((DateTimeOffset?)null, s_now));
    }

    [Fact]
    public void Works_across_offsets()
    {
        var reset = new DateTimeOffset(2026, 10, 18, 20, 0, 0, TimeSpan.Zero); // 22:00 CEST

        Assert.Equal("in 55m", CountdownFormatter.Format(reset, s_now));
    }
}
