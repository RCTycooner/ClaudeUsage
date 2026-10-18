using System.Globalization;
using System.Text;

namespace ClaudeUsage.Usage;

/// <summary>Short display strings for the tray tooltip and menu. Percentages are clamped to 0-100 for display only.</summary>
public static class UsageText
{
    public const int MaxTooltipLength = 127;

    public const string ErrorTooltip = "Claude usage: error — see window";

    /// <summary>Prefix for the tooltip when the last poll failed but older values are still shown.</summary>
    public const string StalePrefix = "⚠ last poll failed — ";

    public const string WaitingTooltip = "Claude usage: waiting for data";

    public static int DisplayPercent(int percent) => Math.Clamp(percent, 0, 100);

    /// <summary>
    /// "Claude: S 3% · W 17% · F 22%" (the last key is the model's initial, "M" when unknown); missing buckets are
    /// left out. With <paramref name="hasError"/> the last known values stay, behind <see cref="StalePrefix"/>;
    /// <see cref="ErrorTooltip"/> only when there were never any values.
    /// </summary>
    public static string Tooltip(UsageSnapshot? snapshot, bool hasError = false)
    {
        if (snapshot is null)
        {
            return hasError ? ErrorTooltip : WaitingTooltip;
        }

        var parts = new List<string>(3);
        Add(parts, "S", snapshot.Session);
        Add(parts, "W", snapshot.Week);
        var modelKey = string.IsNullOrWhiteSpace(snapshot.ModelName)
            ? "M"
            : char.ToUpperInvariant(snapshot.ModelName.Trim()[0]).ToString();
        Add(parts, modelKey, snapshot.ModelWeek);
        return Truncate((hasError ? StalePrefix : "") + "Claude: " + string.Join(" · ", parts));

        static void Add(List<string> parts, string key, UsageBucket? bucket)
        {
            if (bucket is not null)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{key} {DisplayPercent(bucket.Percent)}%"));
            }
        }
    }

    /// <summary>"Session  3%  · resets in 4h 55m"; "Session  —" when the bucket is missing.</summary>
    public static string MenuLine(string label, UsageBucket? bucket, DateTimeOffset now)
    {
        if (bucket is null)
        {
            return label + "  —";
        }

        var sb = new StringBuilder()
            .Append(label)
            .Append("  ")
            .Append(DisplayPercent(bucket.Percent).ToString(CultureInfo.InvariantCulture))
            .Append('%');
        if (CountdownFormatter.Format(bucket.ResetsAt, now) is { } countdown)
        {
            sb.Append("  · resets ").Append(countdown);
        }

        return sb.ToString();
    }

    public static string Truncate(string text) => text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;
}
