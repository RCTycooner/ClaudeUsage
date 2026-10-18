namespace ClaudeUsage.Usage;

/// <summary>Produces the raw text of Claude Code's <c>/usage</c> output.</summary>
public interface IUsageSource
{
    /// <summary>Returns the usage text. Throws <see cref="UsageSourceException"/> when it cannot be obtained.</summary>
    Task<string> FetchRawAsync(CancellationToken cancellationToken);
}
