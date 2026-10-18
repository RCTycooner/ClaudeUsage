using ClaudeUsage.Storage;

namespace ClaudeUsage.UI;

/// <summary>One plotted value: when it was taken and the percentage, already clamped to 0–100.</summary>
public readonly record struct ChartPoint(DateTimeOffset Taken, double Percent);

/// <summary>Pure helpers behind <see cref="UsageChart"/>: windowing, gap segmentation, axis mapping, day ticks.</summary>
public static class ChartMath
{
    /// <summary>Consecutive samples further apart than this are not joined (machine asleep, app closed).</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(5);

    /// <summary>The chart shows this much history, ending now.</summary>
    public static readonly TimeSpan Range = TimeSpan.FromDays(7);

    /// <summary>
    /// Splits one series into continuous runs. Samples outside [from, to] and null values are skipped; a new run
    /// starts whenever two consecutive kept points are more than <paramref name="maxGap"/> apart. Values are clamped
    /// to 0–100. Samples must be ordered oldest first.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ChartPoint>> Segment(
        IReadOnlyList<UsageSample> samples,
        Func<UsageSample, int?> value,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan maxGap)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(value);

        var segments = new List<IReadOnlyList<ChartPoint>>();
        List<ChartPoint>? current = null;
        foreach (var sample in samples)
        {
            if (sample.Taken < from || sample.Taken > to || value(sample) is not { } v)
            {
                continue;
            }

            if (current is null || sample.Taken - current[^1].Taken > maxGap)
            {
                current = [];
                segments.Add(current);
            }

            current.Add(new ChartPoint(sample.Taken, Math.Clamp(v, 0, 100)));
        }

        return segments;
    }

    /// <summary>Number of samples in [from, to] that carry at least one value.</summary>
    public static int CountPlottable(IReadOnlyList<UsageSample> samples, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return samples.Count(s => s.Taken >= from && s.Taken <= to
            && (s.Session is not null || s.Week is not null || s.FableWeek is not null));
    }

    /// <summary>Maps a time to an x coordinate: <paramref name="from"/> is the left edge, <paramref name="to"/> the right.</summary>
    public static double MapX(DateTimeOffset t, DateTimeOffset from, DateTimeOffset to, double left, double width)
    {
        double span = (to - from).Ticks;
        return span <= 0 ? left + width : left + ((t - from).Ticks / span * width);
    }

    /// <summary>Maps a percentage to a y coordinate: 100 % at <paramref name="top"/>, 0 % at top + height. Clamped.</summary>
    public static double MapY(double percent, double top, double height) =>
        top + (height * (1 - (Math.Clamp(percent, 0, 100) / 100)));

    /// <summary>Local midnights in <paramref name="zone"/> strictly after <paramref name="from"/> and not after <paramref name="to"/>.</summary>
    public static IReadOnlyList<DateTimeOffset> DayTicks(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var ticks = new List<DateTimeOffset>();
        var day = TimeZoneInfo.ConvertTime(from, zone).Date.AddDays(1);
        while (true)
        {
            var midnight = new DateTimeOffset(day, zone.GetUtcOffset(day));
            if (midnight > to)
            {
                return ticks;
            }

            if (midnight > from)
            {
                ticks.Add(midnight);
            }

            day = day.AddDays(1);
        }
    }
}
