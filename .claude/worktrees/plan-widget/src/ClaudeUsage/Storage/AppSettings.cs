namespace ClaudeUsage.Storage;

public sealed record AppSettings(bool Topmost = false, double? Left = null, double? Top = null, string? ClaudePath = null);
