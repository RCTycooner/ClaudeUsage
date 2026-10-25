namespace ClaudeUsage.Insights;

/// <summary>The weekly report could not be made or mailed (CLI failed, no report written, Outlook refused, ...).</summary>
public sealed class InsightsException : Exception
{
    public InsightsException()
    {
    }

    public InsightsException(string message)
        : base(message)
    {
    }

    public InsightsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The (truncated) process output, when there was any.</summary>
    public string? RawOutput { get; init; }
}
