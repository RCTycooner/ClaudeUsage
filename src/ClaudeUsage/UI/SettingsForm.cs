using System.Globalization;
using System.IO;
using System.Net.Mail;
using ClaudeUsage.Storage;

namespace ClaudeUsage.UI;

/// <summary>Checks and applies the settings window's fields; kept out of the window so it can be tested.</summary>
public static class SettingsForm
{
    /// <summary>What is wrong with the entered values, or null when they can be saved. Blank fields are fine (defaults).</summary>
    public static string? Validate(string claudePath, string projectsRoot, string insightsEmail, string refreshMinutes)
    {
        ArgumentNullException.ThrowIfNull(claudePath);
        ArgumentNullException.ThrowIfNull(projectsRoot);
        ArgumentNullException.ThrowIfNull(insightsEmail);
        ArgumentNullException.ThrowIfNull(refreshMinutes);

        string path = Clean(claudePath);
        if (path.Length > 0 && !File.Exists(Environment.ExpandEnvironmentVariables(path)))
        {
            return $"Claude CLI not found: {path}";
        }

        string root = Clean(projectsRoot);
        if (root.Length > 0 && !Directory.Exists(root))
        {
            return $"Projects folder not found: {root}";
        }

        // A bare address only: no display name ("Me <me@x.com>") and no list.
        string email = insightsEmail.Trim();
        if (email.Length > 0 && !(MailAddress.TryCreate(email, out var address) && address.Address == email))
        {
            return $"Not a valid email address: {email}";
        }

        string minutes = refreshMinutes.Trim();
        if (minutes.Length > 0 && ParseMinutes(minutes) is not (>= AppSettings.MinRefreshMinutes and <= AppSettings.MaxRefreshMinutes))
        {
            return $"Refresh interval must be a whole number of minutes from {AppSettings.MinRefreshMinutes} to {AppSettings.MaxRefreshMinutes}: {minutes}";
        }

        return null;
    }

    /// <summary><paramref name="settings"/> with the entered values; a blank field is stored as null (the default).</summary>
    public static AppSettings Apply(
        AppSettings settings,
        string claudePath,
        string projectsRoot,
        string insightsEmail,
        string refreshMinutes,
        bool topmost,
        bool startWithWindows)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(claudePath);
        ArgumentNullException.ThrowIfNull(projectsRoot);
        ArgumentNullException.ThrowIfNull(insightsEmail);
        ArgumentNullException.ThrowIfNull(refreshMinutes);

        return settings with
        {
            ClaudePath = NullIfEmpty(Clean(claudePath)),
            ProjectsRoot = NullIfEmpty(Clean(projectsRoot)),
            InsightsEmail = NullIfEmpty(insightsEmail.Trim()),
            RefreshMinutes = ParseMinutes(refreshMinutes.Trim()),
            Topmost = topmost,
            StartWithWindows = startWithWindows,
        };
    }

    private static string Clean(string value) => value.Trim().Trim('"');

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static int? ParseMinutes(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) ? minutes : null;
}
