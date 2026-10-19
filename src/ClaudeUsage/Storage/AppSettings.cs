namespace ClaudeUsage.Storage;

/// <param name="StartWithWindows">Saved Start-with-Windows choice; null until first saved (see <see cref="StartupSync"/>).</param>
public sealed record AppSettings(
    bool Topmost = false,
    double? Left = null,
    double? Top = null,
    string? ClaudePath = null,
    bool? StartWithWindows = null);
