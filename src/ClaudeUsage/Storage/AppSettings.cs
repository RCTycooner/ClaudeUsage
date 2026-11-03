namespace ClaudeUsage.Storage;

/// <param name="StartWithWindows">Saved Start-with-Windows choice; null until first saved (see <see cref="StartupSync"/>).</param>
/// <param name="ProjectsRoot">Folder scanned at startup for CLAUDE.md files (the projects menu).</param>
/// <param name="InsightsEmail">Where the weekly /insights report is mailed; null turns the weekly run off.</param>
/// <param name="InsightsSentFor">The weekly reset the last report was mailed for, so each week is sent once.</param>
/// <param name="RefreshMinutes">Minutes between /usage polls; null means <see cref="DefaultRefreshMinutes"/>.</param>
/// <param name="WindowOpen">Whether the widget window was open when last shown or hidden; restored at start.</param>
public sealed record AppSettings(
    bool Topmost = false,
    double? Left = null,
    double? Top = null,
    string? ClaudePath = null,
    bool? StartWithWindows = null,
    string? ProjectsRoot = AppSettings.DefaultProjectsRoot,
    string? InsightsEmail = null,
    DateTimeOffset? InsightsSentFor = null,
    int? RefreshMinutes = null,
    bool WindowOpen = false)
{
    public const string DefaultProjectsRoot = @"D:\";

    public const int DefaultRefreshMinutes = 1;

    public const int MinRefreshMinutes = 1;

    /// <summary>The chart joins samples up to three of these apart (<c>ChartMath.MaxGap</c>); keep the two in step.</summary>
    public const int MaxRefreshMinutes = 5;

    /// <summary><see cref="ProjectsRoot"/>, or the default when the file sets it to null or blank.</summary>
    public string EffectiveProjectsRoot => string.IsNullOrWhiteSpace(ProjectsRoot) ? DefaultProjectsRoot : ProjectsRoot;

    /// <summary><see cref="RefreshMinutes"/> as a poll interval: the default when unset, clamped to the allowed range.</summary>
    public TimeSpan EffectiveRefreshInterval => TimeSpan.FromMinutes(
        RefreshMinutes is int minutes ? Math.Clamp(minutes, MinRefreshMinutes, MaxRefreshMinutes) : DefaultRefreshMinutes);

    /// <summary>
    /// Whether to show the window at start: always unless started with --hidden (logon, or the daily restart, which
    /// goes through the logon task); then only if it was open before, so a restart puts back an open window.
    /// </summary>
    public bool ShowWindowAtStart(bool startHidden) => !startHidden || WindowOpen;
}
