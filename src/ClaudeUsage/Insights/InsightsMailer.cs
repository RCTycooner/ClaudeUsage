using System.Globalization;
using ClaudeUsage.Usage;

namespace ClaudeUsage.Insights;

/// <summary>
/// Once a week, in the hour before the weekly limit resets, makes the /insights report and mails it (see
/// <see cref="InsightsSchedule"/>). Checked after every usage poll; call on one thread (the UI thread in the app).
/// Each reset is tried at most once per app run, so a failing CLI or Outlook does not rerun /insights on every poll.
/// </summary>
public sealed class InsightsMailer : IDisposable
{
    private readonly IInsightsReportSource _reports;
    private readonly IMailSender _mail;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CancellationTokenSource _cts = new();
    private DateTimeOffset? _attempted;
    private Task? _running;

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
        if (InsightsSchedule.DueReset(_clock(), snapshot.Week, email, sentFor) is not { } reset || reset == _attempted)
        {
            return null;
        }

        _attempted = reset;
        var token = _cts.Token;
        var creating = _reports.CreateReportAsync(token);
        _running = creating;
        var report = await creating;
        token.ThrowIfCancellationRequested();
        await _mail.SendAsync(email!.Trim(), Subject(reset), Body(snapshot.Week!, reset), report);
        return reset;
    }

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

    public static string Body(UsageBucket week, DateTimeOffset reset)
    {
        ArgumentNullException.ThrowIfNull(week);
        string resets = reset.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture);
        return $"Your weekly Claude Code /insights report is attached. Open it in a browser to read it.\r\n\r\n"
            + $"This week: {week.Percent}% of the weekly limit used (all models). The limit resets {resets}.\r\n";
    }
}
