using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeUsage.Usage;

/// <summary>Pure parser for the text Claude Code's <c>/usage</c> command prints.</summary>
public static partial class UsageOutputParser
{
    private static readonly string[] s_months =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>
    /// Parses the session / week / Fable-week lines. Missing lines give null buckets; throws
    /// <see cref="UsageParseException"/> only when none of the three lines is found.
    /// </summary>
    /// <param name="text">The usage text.</param>
    /// <param name="now">Used for year inference of the reset dates and as the snapshot time.</param>
    /// <param name="defaultZone">Zone to assume when a line has no (or an unknown) zone suffix; defaults to local.</param>
    public static UsageSnapshot Parse(string text, DateTimeOffset now, TimeZoneInfo? defaultZone = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        defaultZone ??= TimeZoneInfo.Local;

        UsageBucket? session = null, week = null, fable = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var match = LineRegex().Match(rawLine.Trim());
            if (!match.Success)
            {
                continue;
            }

            var bucket = ParseBucket(match, now, defaultZone);
            var label = WhitespaceRegex().Replace(match.Groups["label"].Value, "").ToUpperInvariant();
            switch (label)
            {
                case "SESSION":
                    session ??= bucket;
                    break;
                case "WEEK(ALLMODELS)":
                    week ??= bucket;
                    break;
                case "WEEK(FABLE)":
                    fable ??= bucket;
                    break;
            }
        }

        if (session is null && week is null && fable is null)
        {
            throw new UsageParseException("Could not find any usage lines in the /usage output.") { RawText = text };
        }

        return new UsageSnapshot(now, session, week, fable, text);
    }

    /// <summary>Converts a wall-clock time in <paramref name="zone"/> to an absolute time, DST-aware.</summary>
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

        // Current year; if that is more than a day in the past, it means next year.
        int thisYear = nowInZone.Year;
        for (int year = thisYear; year <= thisYear + 1; year++)
        {
            if (day > DateTime.DaysInMonth(year, month))
            {
                continue;
            }

            var result = ToZoned(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified), zone);
            if (result >= now.AddDays(-1) || year == thisYear + 1)
            {
                return result;
            }
        }

        return null;
    }

    // "Current session: 3% used · resets Oct 19, 2am (Europe/Brussels)"; the reset part and zone are optional.
    [GeneratedRegex(
        @"^Current\s+(?<label>session|week\s*\(\s*all\s+models\s*\)|week\s*\(\s*Fable\s*\))\s*:\s*(?<pct>\d+)\s*%\s*used" +
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
