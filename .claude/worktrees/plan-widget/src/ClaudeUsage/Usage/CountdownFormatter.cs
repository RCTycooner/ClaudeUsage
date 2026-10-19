namespace ClaudeUsage.Usage;

/// <summary>Formats the time until a reset as "in 1d 14h", "in 3h 12m", "in 45m" or "now".</summary>
public static class CountdownFormatter
{
    public static string Format(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var remaining = resetsAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "now";
        }

        long totalMinutes = Math.Max(1, (long)remaining.TotalMinutes);
        long days = totalMinutes / (24 * 60);
        long hours = totalMinutes / 60 % 24;
        long minutes = totalMinutes % 60;

        if (days > 0)
        {
            return $"in {days}d {hours}h";
        }

        return hours > 0 ? $"in {hours}h {minutes}m" : $"in {minutes}m";
    }

    /// <summary>Null-tolerant variant; returns null when the reset time is unknown.</summary>
    public static string? Format(DateTimeOffset? resetsAt, DateTimeOffset now) =>
        resetsAt is { } r ? Format(r, now) : null;
}
