using ClaudeUsage.Usage;

namespace ClaudeUsage.Insights;

/// <summary>
/// When the weekly /insights report is due: from <see cref="Delay"/> after the weekly (all models) limit resets, for
/// <see cref="Window"/>. The report is for the week that just ended, so it is keyed by the reset that ended it.
/// </summary>
public static class InsightsSchedule
{
    /// <summary>How long after the weekly reset the report is made.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);

    /// <summary>How long after <see cref="Delay"/> a missed report is still made (the widget may start late).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>Weekly resets are 168 hours apart (fixed in UTC, not wall time: unaffected by DST).</summary>
    public static readonly TimeSpan Week = TimeSpan.FromDays(7);

    /// <summary>
    /// /usage rounds the reset time differently from poll to poll (08:59, then 09:00), so resets this close are the
    /// same reset. Far below <see cref="Week"/>, so two weeks never match.
    /// </summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromDays(1);

    /// <summary>Whether two reported reset times are the same weekly reset (see <see cref="Tolerance"/>).</summary>
    public static bool SameReset(DateTimeOffset a, DateTimeOffset b) => (a - b).Duration() < Tolerance;

    /// <summary>
    /// The weekly reset that most recently passed at <paramref name="now"/>, or null when the reset time is unknown.
    /// Right after a reset /usage may still report it for a moment; after that it reports the next one.
    /// </summary>
    public static DateTimeOffset? LastReset(DateTimeOffset now, UsageBucket? week)
    {
        if (week?.ResetsAt is not { } reported)
        {
            return null;
        }

        return reported <= now ? reported : reported - Week;
    }

    /// <summary>
    /// The weekly reset to send a report for at <paramref name="now"/>, or null when none is due: no address set, the
    /// reset time is unknown, <paramref name="now"/> is outside the window after it, or it was already sent for.
    /// </summary>
    public static DateTimeOffset? DueReset(DateTimeOffset now, UsageBucket? week, string? email, DateTimeOffset? sentFor)
    {
        if (string.IsNullOrWhiteSpace(email) || LastReset(now, week) is not { } reset)
        {
            return null;
        }

        var since = now - reset;
        if (since < Delay || since >= Delay + Window || (sentFor is { } sent && SameReset(sent, reset)))
        {
            return null;
        }

        return reset;
    }
}
