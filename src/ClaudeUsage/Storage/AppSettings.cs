namespace ClaudeUsage.Storage;

/// <param name="StartWithWindows">Saved Start-with-Windows choice; null until first saved (see <see cref="StartupSync"/>).</param>
/// <param name="ProjectsRoot">Folder scanned at startup for CLAUDE.md files (the projects menu).</param>
/// <param name="InsightsEmail">Where the weekly /insights report is mailed; null turns the weekly run off.</param>
/// <param name="InsightsSentFor">The weekly reset the last report was mailed for, so each week is sent once.</param>
public sealed record AppSettings(
    bool Topmost = false,
    double? Left = null,
    double? Top = null,
    string? ClaudePath = null,
    bool? StartWithWindows = null,
    string? ProjectsRoot = AppSettings.DefaultProjectsRoot,
    string? InsightsEmail = null,
    DateTimeOffset? InsightsSentFor = null)
{
    public const string DefaultProjectsRoot = @"D:\";

    /// <summary><see cref="ProjectsRoot"/>, or the default when the file sets it to null or blank.</summary>
    public string EffectiveProjectsRoot => string.IsNullOrWhiteSpace(ProjectsRoot) ? DefaultProjectsRoot : ProjectsRoot;
}
