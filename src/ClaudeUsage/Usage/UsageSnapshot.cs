namespace ClaudeUsage.Usage;

/// <summary>One reading of Claude Code's <c>/usage</c> output. A bucket is null when its line was missing.</summary>
public sealed record UsageSnapshot(
    DateTimeOffset Taken,
    UsageBucket? Session,
    UsageBucket? Week,
    UsageBucket? FableWeek,
    string RawText);

/// <summary>Percent used as reported (not clamped; may exceed 100) and when the limit resets, if known.</summary>
public sealed record UsageBucket(int Percent, DateTimeOffset? ResetsAt);
