namespace ClaudeUsage.Usage;

/// <summary>
/// One reading of Claude Code's <c>/usage</c> output. A bucket is null when its line was missing.
/// <see cref="ModelWeek"/> is the per-model week ("Current week (Fable)"); <see cref="ModelName"/> is the name in
/// the parentheses, or null when unknown (e.g. history written before the name was stored).
/// </summary>
public sealed record UsageSnapshot(
    DateTimeOffset Taken,
    UsageBucket? Session,
    UsageBucket? Week,
    UsageBucket? ModelWeek,
    string RawText,
    string? ModelName = null)
{
    public const string DefaultModelWeekLabel = "Model week";

    /// <summary>"Fable week", "Opus week", ...; "Model week" when the name is unknown.</summary>
    public string ModelWeekLabel => ModelWeekLabelFor(ModelName);

    public static string ModelWeekLabelFor(string? modelName) =>
        string.IsNullOrWhiteSpace(modelName) ? DefaultModelWeekLabel : modelName.Trim() + " week";

    /// <summary>
    /// Labels of the buckets that <paramref name="previous"/> had and <paramref name="current"/> lacks. Comparing
    /// consecutive snapshots reports each disappearance once, on the poll where it happens.
    /// </summary>
    public static IReadOnlyList<string> DisappearedBuckets(UsageSnapshot? previous, UsageSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is null)
        {
            return [];
        }

        var gone = new List<string>();
        if (previous.Session is not null && current.Session is null)
        {
            gone.Add("Session");
        }

        if (previous.Week is not null && current.Week is null)
        {
            gone.Add("Week");
        }

        if (previous.ModelWeek is not null && current.ModelWeek is null)
        {
            gone.Add(previous.ModelWeekLabel);
        }

        return gone;
    }
}

/// <summary>Percent used as reported (not clamped; may exceed 100) and when the limit resets, if known.</summary>
public sealed record UsageBucket(int Percent, DateTimeOffset? ResetsAt);
