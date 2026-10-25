using ClaudeUsage.Storage;

namespace ClaudeUsage.UI;

/// <summary>One plotted value: when it was taken and the percentage, already clamped to 0–100.</summary>
public readonly record struct ChartPoint(DateTimeOffset Taken, double Percent);

/// <summary>Pure helpers behind <see cref="UsageChart"/>: windowing, gap segmentation, thinning, axis mapping, ticks.</summary>
public static class ChartMath
{
    /// <summary>
    /// Consecutive samples further apart than this are not joined (machine asleep, app closed). Three of the longest
    /// poll interval (<see cref="AppSettings.MaxRefreshMinutes"/>), so a slow or failed poll does not break the line.
    /// </summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(15);

    /// <summary>The widget's chart shows this much history, ending now.</summary>
    public static readonly TimeSpan Range = TimeSpan.FromDays(7);

    /// <summary>Tick steps from fine to coarse; <see cref="ChooseStep"/> takes the first one that leaves room for labels.</summary>
    public static readonly IReadOnlyList<TickStep> Steps =
    [
        new(TickUnit.Minute, 15),
        new(TickUnit.Minute, 30),
        new(TickUnit.Minute, 60),
        new(TickUnit.Minute, 120),
        new(TickUnit.Minute, 180),
        new(TickUnit.Minute, 360),
        new(TickUnit.Minute, 720),
        new(TickUnit.Day, 1),
        new(TickUnit.Day, 2),
        new(TickUnit.Week, 1),
        new(TickUnit.Month, 1),
        new(TickUnit.Month, 3),
    ];

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
        for (int i = LowerBound(samples, from); i < samples.Count && samples[i].Taken <= to; i++)
        {
            var sample = samples[i];
            if (value(sample) is not { } v)
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

    /// <summary>Number of samples in [from, to] that carry at least one value. Samples must be ordered oldest first.</summary>
    public static int CountPlottable(IReadOnlyList<UsageSample> samples, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(samples);
        int count = 0;
        for (int i = LowerBound(samples, from); i < samples.Count && samples[i].Taken <= to; i++)
        {
            var s = samples[i];
            if (s.Session is not null || s.Week is not null || s.ModelWeek is not null)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Index of the first sample taken at or after <paramref name="t"/>. Samples must be ordered oldest first.</summary>
    public static int LowerBound(IReadOnlyList<UsageSample> samples, DateTimeOffset t)
    {
        ArgumentNullException.ThrowIfNull(samples);
        int lo = 0, hi = samples.Count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (samples[mid].Taken < t)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>
    /// Thins a run for drawing: points are grouped into <paramref name="bucket"/>-wide time slots counted from
    /// <paramref name="origin"/>, and each slot keeps only its first, lowest, highest and last point, in time order.
    /// With one slot per pixel the line looks the same, but months of one-per-poll samples stay cheap to draw.
    /// </summary>
    public static IReadOnlyList<ChartPoint> Decimate(IReadOnlyList<ChartPoint> points, DateTimeOffset origin, TimeSpan bucket)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (bucket <= TimeSpan.Zero || points.Count <= 4)
        {
            return points;
        }

        var result = new List<ChartPoint>();
        int start = 0;
        while (start < points.Count)
        {
            long slot = Slot(points[start]);
            int end = start + 1, min = start, max = start;
            for (; end < points.Count && Slot(points[end]) == slot; end++)
            {
                if (points[end].Percent < points[min].Percent)
                {
                    min = end;
                }
                else if (points[end].Percent > points[max].Percent)
                {
                    max = end;
                }
            }

            // Indices in time order; SortedSet drops the duplicates when first/min/max/last coincide.
            foreach (int i in new SortedSet<int> { start, min, max, end - 1 })
            {
                result.Add(points[i]);
            }

            start = end;
        }

        return result;

        long Slot(ChartPoint p) => (long)Math.Floor((p.Taken - origin).Ticks / (double)bucket.Ticks);
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

    /// <summary>"Mon 19 Oct 14:30" in <paramref name="zone"/>, with the culture's short time ("2:30 PM" where usual).</summary>
    public static string FormatMoment(DateTimeOffset t, TimeZoneInfo zone, IFormatProvider culture)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(t, zone);
        // "t" only means short time as a whole format string; inside a custom one it is the AM/PM designator.
        return local.ToString("ddd d MMM", culture) + " " + local.ToString("t", culture);
    }

    /// <summary>Local midnights in <paramref name="zone"/> strictly after <paramref name="from"/> and not after <paramref name="to"/>.</summary>
    public static IReadOnlyList<DateTimeOffset> DayTicks(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone) =>
        Ticks(from, to, zone, new TickStep(TickUnit.Day, 1));

    /// <summary>
    /// The finest step whose ticks are at least <paramref name="minDayWidth"/> apart for single days (their labels
    /// sit centred between midnights, so short weekday names fit) or <paramref name="minLabelWidth"/> apart otherwise.
    /// </summary>
    public static TickStep ChooseStep(TimeSpan span, double width, double minDayWidth, double minLabelWidth)
    {
        if (span <= TimeSpan.Zero || width <= 0)
        {
            return Steps[^1];
        }

        foreach (var step in Steps)
        {
            double pixels = step.Approximate.Ticks / (double)span.Ticks * width;
            if (pixels >= (step == TickStep.OneDay ? minDayWidth : minLabelWidth))
            {
                return step;
            }
        }

        return Steps[^1];
    }

    /// <summary>
    /// Local wall-clock ticks in <paramref name="zone"/> strictly after <paramref name="from"/> and not after
    /// <paramref name="to"/>, aligned so they stay put while the view scrolls: minutes to multiples of the step
    /// since midnight, days to the day number, weeks to <paramref name="firstDayOfWeek"/>, months to the year.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> Ticks(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo zone,
        TickStep step,
        DayOfWeek firstDayOfWeek = DayOfWeek.Monday)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (step.Count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        var ticks = new List<DateTimeOffset>();
        var localFrom = TimeZoneInfo.ConvertTime(from, zone).DateTime;
        var t = step.Unit == TickUnit.Month ? new DateTime(localFrom.Year, localFrom.Month, 1) : localFrom.Date;
        while (true)
        {
            var tick = new DateTimeOffset(t, zone.GetUtcOffset(t));
            if (tick > to)
            {
                return ticks;
            }

            bool aligned = step.Unit switch
            {
                TickUnit.Day => (t - DateTime.MinValue).Days % step.Count == 0,
                TickUnit.Week => t.DayOfWeek == firstDayOfWeek,
                TickUnit.Month => (t.Month - 1) % step.Count == 0,
                _ => true,
            };

            // The ordering check drops the repeated wall-clock hour when clocks go back.
            if (aligned && tick > from && (ticks.Count == 0 || tick > ticks[^1]))
            {
                ticks.Add(tick);
            }

            t = step.Unit switch
            {
                TickUnit.Minute => t.AddMinutes(step.Count),
                TickUnit.Month => t.AddMonths(1),
                _ => t.AddDays(1),
            };
        }
    }
}

public enum TickUnit
{
    Minute,
    Day,
    Week,
    Month,
}

/// <summary>Spacing of x-axis ticks: <see cref="Count"/> of <see cref="Unit"/>.</summary>
public readonly record struct TickStep(TickUnit Unit, int Count)
{
    public static readonly TickStep OneDay = new(TickUnit.Day, 1);

    /// <summary>Nominal length, for choosing a step; real ticks follow the calendar (DST, month lengths).</summary>
    public TimeSpan Approximate => Unit switch
    {
        TickUnit.Minute => TimeSpan.FromMinutes(Count),
        TickUnit.Day => TimeSpan.FromDays(Count),
        TickUnit.Week => TimeSpan.FromDays(7 * Count),
        _ => TimeSpan.FromDays(30.4 * Count),
    };
}
