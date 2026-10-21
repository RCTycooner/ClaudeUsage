using ClaudeUsage.Storage;

namespace ClaudeUsage.Usage;

/// <summary>
/// Linear estimates of when a bucket reaches 100 %: the session from its average rate since it started, the week
/// buckets from their average rate over the last 7 days of history. Null when there is too little to go on, nothing
/// is being used, or the bucket is already full.
/// </summary>
public static class LimitForecast
{
    public static readonly TimeSpan SessionLength = TimeSpan.FromHours(5);

    public static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);

    /// <summary>Shortest span a rate is taken over; a few minutes in, one burst would extrapolate wildly.</summary>
    public static readonly TimeSpan MinSpan = TimeSpan.FromMinutes(15);

    /// <summary>The session started <see cref="SessionLength"/> before its reset; usage since then sets the rate.</summary>
    public static DateTimeOffset? Session(UsageBucket? bucket, DateTimeOffset now)
    {
        if (bucket is not { ResetsAt: { } reset } || bucket.Percent is <= 0 or >= 100)
        {
            return null;
        }

        var elapsed = now - (reset - SessionLength);
        return elapsed < MinSpan ? null : Eta(bucket.Percent, bucket.Percent / elapsed.TotalHours, now);
    }

    /// <summary>
    /// Rate = usage added over the samples of the last 7 days / the time they span. Increases between consecutive
    /// samples are summed; across a reset (a drop) the new value counts as used since the reset.
    /// </summary>
    public static DateTimeOffset? Weekly(
        UsageBucket? bucket, IReadOnlyList<UsageSample> history, Func<UsageSample, int?> percentOf, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(percentOf);
        if (bucket is null || bucket.Percent >= 100)
        {
            return null;
        }

        var from = now - WeeklyWindow;
        DateTimeOffset? first = null, last = null;
        int? previous = null;
        int used = 0;
        foreach (var sample in history)
        {
            if (sample.Taken < from || sample.Taken > now || percentOf(sample) is not { } p)
            {
                continue;
            }

            if (previous is { } prev)
            {
                used += p >= prev ? p - prev : p;
            }

            first ??= sample.Taken;
            last = sample.Taken;
            previous = p;
        }

        if (first is not { } f || last is not { } l || l - f < MinSpan || used <= 0)
        {
            return null;
        }

        return Eta(bucket.Percent, used / (l - f).TotalHours, now);
    }

    private static DateTimeOffset? Eta(int percent, double perHour, DateTimeOffset now)
    {
        double hours = (100 - percent) / perHour;
        // Beyond a year the estimate means nothing (and DateTimeOffset could overflow).
        return hours is > 0 and < 24 * 365 ? now + TimeSpan.FromHours(hours) : null;
    }
}
