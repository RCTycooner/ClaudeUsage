namespace ClaudeUsage.Storage;

/// <param name="StartWithWindows">Saved Start-with-Windows choice; null until first saved (see <see cref="StartupSync"/>).</param>
/// <param name="ProjectsRoot">Folder scanned at startup for CLAUDE.md files (the projects menu).</param>
public sealed record AppSettings(
    bool Topmost = false,
    double? Left = null,
    double? Top = null,
    string? ClaudePath = null,
    bool? StartWithWindows = null,
    string? ProjectsRoot = AppSettings.DefaultProjectsRoot)
{
    public const string DefaultProjectsRoot = @"D:\";

    /// <summary><see cref="ProjectsRoot"/>, or the default when the file sets it to null or blank.</summary>
    public string EffectiveProjectsRoot => string.IsNullOrWhiteSpace(ProjectsRoot) ? DefaultProjectsRoot : ProjectsRoot;
}
