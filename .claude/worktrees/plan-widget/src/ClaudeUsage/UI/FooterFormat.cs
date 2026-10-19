using System.Globalization;

namespace ClaudeUsage.UI;

/// <summary>Footer strings: how long ago the last good reading was taken, and error lines.</summary>
public static class FooterFormat
{
    public const string Waiting = "waiting for first update…";

    public const string Copied = "copied";

    public const string CopyFailed = "clipboard is busy, try again";

    /// <summary>"updated 12 s ago", "updated 3 min ago", "updated 2 h ago", "updated 1 d ago".</summary>
    public static string Updated(DateTimeOffset? lastUpdated, DateTimeOffset now)
    {
        if (lastUpdated is not { } last)
        {
            return Waiting;
        }

        var age = now - last;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        string ago = age.TotalSeconds < 60 ? Ago((int)age.TotalSeconds, "s")
            : age.TotalMinutes < 60 ? Ago((int)age.TotalMinutes, "min")
            : age.TotalHours < 24 ? Ago((int)age.TotalHours, "h")
            : Ago((int)age.TotalDays, "d");
        return "updated " + ago;

        static string Ago(int n, string unit) => string.Create(CultureInfo.InvariantCulture, $"{n} {unit} ago");
    }

    public const string CopyHint = "click to copy";

    /// <summary>First line of an error message, for the single-line footer (the tooltip has the full text).</summary>
    public static string Error(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        int nl = message.IndexOfAny(['\r', '\n']);
        return (nl >= 0 ? message[..nl] : message).Trim();
    }
}
