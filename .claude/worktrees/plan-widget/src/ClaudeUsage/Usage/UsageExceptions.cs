namespace ClaudeUsage.Usage;

/// <summary>The usage text could not be obtained (exe missing, timeout, error envelope, ...).</summary>
public sealed class UsageSourceException : Exception
{
    public UsageSourceException()
    {
    }

    public UsageSourceException(string message)
        : base(message)
    {
    }

    public UsageSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The (truncated) process output, when there was any.</summary>
    public string? RawOutput { get; init; }
}

/// <summary>The usage text was obtained but none of the expected lines could be parsed.</summary>
public sealed class UsageParseException : Exception
{
    public UsageParseException()
    {
    }

    public UsageParseException(string message)
        : base(message)
    {
    }

    public UsageParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string RawText { get; init; } = "";
}
