using System.Globalization;
using System.IO;
using ClaudeUsage.Usage;

namespace ClaudeUsage.Insights;

/// <summary>
/// Once a week, shortly after the weekly limit resets, makes the /insights report and mails it (see
/// <see cref="InsightsSchedule"/>). Checked after every usage poll; call on one thread (the UI thread in the app).
/// A failed attempt is retried after <see cref="RetryDelay"/> (while the report is still due), not on every poll; a
/// report made by an attempt whose mail failed is reused, so /insights is not rerun for that.
/// </summary>
public sealed class InsightsMailer : IDisposable
{
    /// <summary>How long after a failed attempt the next one is made.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

    private readonly IInsightsReportSource _reports;
    private readonly IMailSender _mail;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CancellationTokenSource _cts = new();
    private DateTimeOffset? _attempted;
    private DateTimeOffset _retryAt;
    private string? _report;
    private Task? _running;

    // The weekly bucket last seen for the current and the previous reset: once a week has reset, /usage only shows
    // the new one, so the week the report is for is remembered from earlier polls.
    private UsageBucket? _week;
    private UsageBucket? _weekBefore;

    public InsightsMailer(IInsightsReportSource reports, IMailSender mail, Func<DateTimeOffset>? clock = null)
    {
        _reports = reports;
        _mail = mail;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>
    /// Makes and mails the report when one is due for <paramref name="snapshot"/>'s weekly reset. Returns that reset
    /// once mailed (the caller stores it as <c>InsightsSentFor</c>), or null when nothing was due. Throws
    /// <see cref="InsightsException"/> when the report or the mail failed, and <see cref="OperationCanceledException"/>
    /// after <see cref="Dispose"/>.
    /// </summary>
    public async Task<DateTimeOffset?> SendIfDueAsync(UsageSnapshot snapshot, string? email, DateTimeOffset? sentFor)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Remember(snapshot.Week);
        var now = _clock();
        if (InsightsSchedule.DueReset(now, snapshot.Week, email, sentFor) is not { } reset)
        {
            return null;
        }

        bool retry = _attempted is { } attempted && InsightsSchedule.SameReset(attempted, reset);
        if (retry && now < _retryAt)
        {
            // In progress, already mailed, or failed too recently.
            return null;
        }

        if (!retry)
        {
            _report = null;
        }

        _attempted = reset;
        _retryAt = DateTimeOffset.MaxValue;
        try
        {
            if (_report is not { } report || !File.Exists(report))
            {
                var token = _cts.Token;
                var creating = _reports.CreateReportAsync(token);
                _running = creating;
                report = _report = await creating;
                token.ThrowIfCancellationRequested();
            }

            await _mail.SendAsync(email!.Trim(), Subject(reset), Body(EndedWeek(reset), reset), report);
        }
        catch (InsightsException)
        {
            _retryAt = _clock() + RetryDelay;
            throw;
        }

        return reset;
    }

    private void Remember(UsageBucket? week)
    {
        if (week?.ResetsAt is not { } reset)
        {
            return;
        }

        if (_week?.ResetsAt is { } current && !InsightsSchedule.SameReset(current, reset))
        {
            _weekBefore = _week;
        }

        _week = week;
    }

    /// <summary>The last bucket seen for the week that ended at <paramref name="reset"/>; null if none was seen.</summary>
    private UsageBucket? EndedWeek(DateTimeOffset reset) =>
        new[] { _week, _weekBefore }.FirstOrDefault(w => w?.ResetsAt is { } r && InsightsSchedule.SameReset(r, reset));

    /// <summary>
    /// Cancels a run in progress, which kills its claude.exe, and waits up to <see cref="UsagePoller.DisposeWait"/>
    /// for that to happen so the child is gone before the app exits.
    /// </summary>
    public void Dispose()
    {
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _running?.Wait(UsagePoller.DisposeWait);
        }
        catch (AggregateException)
        {
            // Cancelled or failed; either way it is over.
        }

        // The CTS is not disposed: the run may still observe its token while it unwinds.
    }

    public static string Subject(DateTimeOffset reset) =>
        $"Claude Code insights, week to {reset.ToLocalTime().ToString("ddd d MMM", CultureInfo.CurrentCulture)}";

    /// <param name="week">The week that ended at <paramref name="reset"/>, as last seen; null leaves out the percentage.</param>
    public static string Body(UsageBucket? week, DateTimeOffset reset)
    {
        string ended = reset.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture);
        string used = week is null ? "" : $" {week.Percent}% of the weekly limit used (all models).";
        return $"Your weekly Claude Code /insights report is attached. Open it in a browser to read it.\r\n\r\n"
            + $"Week ended {ended}:{used} The limit has reset.\r\n";
    }
}
