using ClaudeUsage.Usage;

namespace ClaudeUsage.Insights;

/// <summary>When the weekly /insights report is due: in the hour before the weekly (all models) limit resets.</summary>
public static class InsightsSchedule
{
    /// <summary>How long before the weekly reset the report is made.</summary>
    public static readonly TimeSpan Lead = TimeSpan.FromHours(1);

    /// <summary>
    /// The weekly reset to send a report for at <paramref name="now"/>, or null when none is due: no address set, the
    /// reset time is unknown, <paramref name="now"/> is outside the hour before it, or it was already sent for.
    /// </summary>
    public static DateTimeOffset? DueReset(DateTimeOffset now, UsageBucket? week, string? email, DateTimeOffset? sentFor)
    {
        if (string.IsNullOrWhiteSpace(email) || week?.ResetsAt is not { } reset)
        {
            return null;
        }

        if (now < reset - Lead || now >= reset || sentFor == reset)
        {
            return null;
        }

        return reset;
    }
}
