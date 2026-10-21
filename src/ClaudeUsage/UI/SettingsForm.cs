using System.IO;
using ClaudeUsage.Storage;

namespace ClaudeUsage.UI;

/// <summary>Checks and applies the settings window's fields; kept out of the window so it can be tested.</summary>
public static class SettingsForm
{
    /// <summary>What is wrong with the entered values, or null when they can be saved. Blank fields are fine (defaults).</summary>
    public static string? Validate(string claudePath, string projectsRoot)
    {
        ArgumentNullException.ThrowIfNull(claudePath);
        ArgumentNullException.ThrowIfNull(projectsRoot);

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

        return null;
    }

    /// <summary><paramref name="settings"/> with the entered values; a blank field is stored as null (the default).</summary>
    public static AppSettings Apply(AppSettings settings, string claudePath, string projectsRoot, bool topmost, bool startWithWindows)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(claudePath);
        ArgumentNullException.ThrowIfNull(projectsRoot);

        return settings with
        {
            ClaudePath = NullIfEmpty(Clean(claudePath)),
            ProjectsRoot = NullIfEmpty(Clean(projectsRoot)),
            Topmost = topmost,
            StartWithWindows = startWithWindows,
        };
    }

    private static string Clean(string value) => value.Trim().Trim('"');

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
