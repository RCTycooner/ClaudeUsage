using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeUsage.Usage;

/// <summary>Pure parser for the text Claude Code's <c>/usage</c> command prints.</summary>
public static partial class UsageOutputParser
{
    private static readonly string[] s_months =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>
    /// Parses the session, week (all models) and per-model week lines. The per-model line is whichever
    /// "Current week (&lt;name&gt;)" line comes first that is not "all models"; its name ends up in
    /// <see cref="UsageSnapshot.ModelName"/>. Further per-model lines are ignored. Missing lines give null buckets;
    /// throws <see cref="UsageParseException"/> only when none of the three lines is found.
    /// </summary>
    /// <param name="text">The usage text.</param>
    /// <param name="now">Used for year inference of the reset dates and as the snapshot time.</param>
    /// <param name="defaultZone">Zone to assume when a line has no (or an unknown) zone suffix; defaults to local.</param>
    public static UsageSnapshot Parse(string text, DateTimeOffset now, TimeZoneInfo? defaultZone = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        defaultZone ??= TimeZoneInfo.Local;

        UsageBucket? session = null, week = null, modelWeek = null;
        string? modelName = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var match = LineRegex().Match(rawLine.Trim());
            if (!match.Success)
            {
                continue;
            }

            var bucket = ParseBucket(match, now, defaultZone);
            if (!match.Groups["model"].Success)
            {
                session ??= bucket;
                continue;
            }

            var model = WhitespaceRegex().Replace(match.Groups["model"].Value.Trim(), " ");
            if (string.Equals(model, "all models", StringComparison.OrdinalIgnoreCase))
            {
                week ??= bucket;
            }
            else if (modelWeek is null && model.Length > 0)
            {
                modelWeek = bucket;
                modelName = model;
            }
        }

        if (session is null && week is null && modelWeek is null)
        {
            throw new UsageParseException("Could not find any usage lines in the /usage output.") { RawText = text };
        }

        return new UsageSnapshot(now, session, week, modelWeek, text, modelName);
    }

    /// <summary>Converts a wall-clock time in <paramref name="zone"/> to an absolute time, DST-aware.</summary>
    /// <remarks>
    /// DST assumptions (the CLI prints wall times only, so these are guesses): a time skipped by a spring-forward
    /// transition is moved forward by one hour, which assumes the usual 1-hour shift; an ambiguous time in the
    /// repeated fall-back hour is taken as the first occurrence (still on summer time), i.e. the earlier instant,
    /// so a countdown never overshoots the real reset.
    /// </remarks>
    public static DateTimeOffset ToZoned(DateTime wall, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall))
        {
            // Skipped by a spring-forward transition: the clock jumps past it.
            wall = wall.AddHours(1);
        }

        var offset = zone.IsAmbiguousTime(wall)
            ? zone.GetAmbiguousTimeOffsets(wall).Max() // earlier of the two instants (still on summer time)
            : zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset);
    }

    private static UsageBucket ParseBucket(Match match, DateTimeOffset now, TimeZoneInfo defaultZone)
    {
        int percent = int.Parse(match.Groups["pct"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        DateTimeOffset? resetsAt = null;
        if (match.Groups["when"].Success)
        {
            var zone = ResolveZone(match.Groups["tz"].Success ? match.Groups["tz"].Value : null, defaultZone);
            resetsAt = ParseReset(match.Groups["when"].Value, now, zone);
        }

        return new UsageBucket(percent, resetsAt);
    }

    private static TimeZoneInfo ResolveZone(string? id, TimeZoneInfo fallback)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return fallback;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return fallback;
        }
    }

    /// <summary>Parses "Oct 19, 2am" / "Oct 19, 2:30pm" / "2am" as a wall time in <paramref name="zone"/>.</summary>
    private static DateTimeOffset? ParseReset(string value, DateTimeOffset now, TimeZoneInfo zone)
    {
        var m = ResetRegex().Match(value.Trim());
        if (!m.Success)
        {
            return null;
        }

        int hour = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        int minute = m.Groups["min"].Success ? int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture) : 0;
        if (m.Groups["ap"].Success)
        {
            if (hour is < 1 or > 12)
            {
                return null;
            }

            bool pm = char.ToUpperInvariant(m.Groups["ap"].Value[0]) == 'P';
            hour = (hour % 12) + (pm ? 12 : 0);
        }

        if (hour > 23 || minute > 59)
        {
            return null;
        }

        var nowInZone = TimeZoneInfo.ConvertTime(now, zone);

        if (!m.Groups["mon"].Success)
        {
            // Time only: the next occurrence of that wall time.
            var today = nowInZone.Date.AddHours(hour).AddMinutes(minute);
            var candidate = ToZoned(today, zone);
            return candidate < now ? ToZoned(today.AddDays(1), zone) : candidate;
        }

        int month = Array.IndexOf(s_months, m.Groups["mon"].Value[..3].ToLowerInvariant()) + 1;
        int day = int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture);
        if (month == 0 || day < 1)
        {
            return null;
        }

        // No year in the text: try last, this and next year and take the earliest that is at most a day in the
        // past (a reset just passed is still reported for a while). Handles both directions around New Year:
        // "Jan 1" seen on Dec 31 is next year, "Dec 31, 11pm" seen just after midnight on Jan 1 is last year.
        int thisYear = nowInZone.Year;
        var earliestAllowed = now.AddDays(-1);
        DateTimeOffset? best = null;
        for (int year = thisYear - 1; year <= thisYear + 1; year++)
        {
            if (day > DateTime.DaysInMonth(year, month))
            {
                continue;
            }

            var candidate = ToZoned(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified), zone);
            if (candidate >= earliestAllowed && (best is null || candidate < best))
            {
                best = candidate;
            }
        }

        return best;
    }

    // "Current session: 3% used · resets Oct 19, 2am (Europe/Brussels)"; the reset part and zone are optional.
    // Week lines carry a model in parentheses: "Current week (all models)", "Current week (Fable)", ...
    [GeneratedRegex(
        @"^Current\s+(?:session|week\s*\((?<model>[^)]+)\))\s*:\s*(?<pct>\d+)\s*%\s*used" +
        @"(?:\s*[·•,\-]?\s*resets?\s+(?<when>.+?)(?:\s*\((?<tz>[^()]+)\))?)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();

    [GeneratedRegex(
        @"^(?:(?<mon>[A-Za-z]{3,9})\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?\s*,?\s*(?:at\s+)?)?" +
        @"(?<h>\d{1,2})(?::(?<min>\d{2}))?\s*(?<ap>[ap]\.?m\.?)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResetRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
