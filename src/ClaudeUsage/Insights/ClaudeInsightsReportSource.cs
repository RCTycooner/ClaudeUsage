using System.IO;
using ClaudeUsage.Usage;

namespace ClaudeUsage.Insights;

/// <summary>Makes the /insights report and returns the path of the HTML file.</summary>
public interface IInsightsReportSource
{
    /// <summary>Throws <see cref="InsightsException"/> when no report could be made.</summary>
    Task<string> CreateReportAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Runs <c>claude -p "/insights" --no-session-persistence --strict-mcp-config</c>. Unlike /usage this calls the model
/// (it analyses recent sessions), so it is slow and uses tokens. The command writes
/// <c>usage-data\report-&lt;timestamp&gt;.html</c> (and <c>report.html</c>) under the Claude config folder; the
/// newest one written during the run is the result.
/// </summary>
public sealed class ClaudeInsightsReportSource : IInsightsReportSource
{
    private const int MaxDetailChars = 500;
    private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(30);

    /// <summary>No MCP servers, as for /usage: the report reads local session files only.</summary>
    private static readonly string[] s_arguments = ["-p", "/insights", "--no-session-persistence", "--strict-mcp-config"];

    private readonly string _workingDirectory;
    private volatile string? _claudePathOverride;

    /// <param name="workingDirectory">Folder to run in (the app's data folder, so no project config is picked up).</param>
    /// <param name="claudePathOverride">Explicit path to claude.exe (or claude.cmd) from settings, or null.</param>
    public ClaudeInsightsReportSource(string workingDirectory, string? claudePathOverride)
    {
        _workingDirectory = workingDirectory;
        _claudePathOverride = claudePathOverride;
    }

    /// <summary>The settings override; a change applies from the next run.</summary>
    public string? ClaudePathOverride
    {
        get => _claudePathOverride;
        set => _claudePathOverride = value;
    }

    /// <summary><c>%CLAUDE_CONFIG_DIR%\usage-data</c>, or <c>%USERPROFILE%\.claude\usage-data</c> by default.</summary>
    public static string ReportFolder =>
        Path.Combine(
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configDir
                ? configDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
            "usage-data");

    public async Task<string> CreateReportAsync(CancellationToken cancellationToken)
    {
        var exe = ClaudeCliUsageSource.ResolveExe(_claudePathOverride)
            ?? throw new InsightsException($"{ClaudeCliUsageSource.ExeName} not found.");

        // A little slack: file times and the clock can disagree by a tick or so.
        var startedUtc = DateTime.UtcNow.AddSeconds(-2);
        ClaudeRunResult run;
        try
        {
            run = await ClaudeCliUsageSource.RunAsync(exe, _workingDirectory, s_arguments, s_timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (UsageSourceException ex)
        {
            throw new InsightsException(ex.Message, ex) { RawOutput = ex.RawOutput };
        }

        return FindReport(ReportFolder, startedUtc)
            ?? throw new InsightsException(
                $"/insights wrote no report to {ReportFolder} (exit code {run.ExitCode}).")
            { RawOutput = Truncate($"{run.Stdout.Trim()}\n{run.Stderr.Trim()}".Trim()) };
    }

    /// <summary>
    /// The newest <c>report*.html</c> in <paramref name="folder"/> written at or after <paramref name="sinceUtc"/>,
    /// preferring the timestamped copy over <c>report.html</c> (which the next run overwrites). Null if none.
    /// </summary>
    public static string? FindReport(string folder, DateTime sinceUtc)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles("report*.html")
                .Where(f => f.LastWriteTimeUtc >= sinceUtc)
                .OrderByDescending(f => !f.Name.Equals("report.html", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Truncate(string text) => text.Length > MaxDetailChars ? text[..MaxDetailChars] + "..." : text;
}
