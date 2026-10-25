using ClaudeUsage.Insights;
using ClaudeUsage.Storage;
using ClaudeUsage.Usage;

namespace ClaudeUsage.Tests;

public sealed class InsightsTests : IDisposable
{
    private const string Email = "me@example.com";

    private static readonly DateTimeOffset s_reset = new(2026, 10, 30, 10, 0, 0, TimeSpan.FromHours(1));

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ClaudeUsageTests", Guid.NewGuid().ToString("N"));

    public InsightsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static UsageSnapshot Snapshot(DateTimeOffset? weekResets) =>
        new(s_reset.AddDays(-1), Session: null, Week: new UsageBucket(63, weekResets), ModelWeek: null, RawText: "");

    [Theory]
    [InlineData(-60)]
    [InlineData(-30)]
    [InlineData(-1)]
    public void DueReset_is_the_reset_in_the_hour_before_it(int minutesBefore)
    {
        var week = new UsageBucket(50, s_reset);

        Assert.Equal(s_reset, InsightsSchedule.DueReset(s_reset.AddMinutes(minutesBefore), week, Email, sentFor: null));
    }

    [Theory]
    [InlineData(-61)]
    [InlineData(-24 * 60)]
    [InlineData(0)]
    [InlineData(5)]
    public void DueReset_is_null_outside_that_hour(int minutesBefore)
    {
        var week = new UsageBucket(50, s_reset);

        Assert.Null(InsightsSchedule.DueReset(s_reset.AddMinutes(minutesBefore), week, Email, sentFor: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DueReset_is_null_without_an_address(string? email)
    {
        Assert.Null(InsightsSchedule.DueReset(s_reset.AddMinutes(-30), new UsageBucket(50, s_reset), email, sentFor: null));
    }

    [Fact]
    public void DueReset_is_null_when_already_sent_for_that_reset_even_in_another_offset()
    {
        var now = s_reset.AddMinutes(-30);
        var week = new UsageBucket(50, s_reset);

        Assert.Null(InsightsSchedule.DueReset(now, week, Email, sentFor: s_reset.ToUniversalTime()));
        Assert.Equal(s_reset, InsightsSchedule.DueReset(now, week, Email, sentFor: s_reset.AddDays(-7)));
    }

    [Fact]
    public void DueReset_is_null_when_the_reset_time_is_unknown()
    {
        Assert.Null(InsightsSchedule.DueReset(s_reset, new UsageBucket(50, null), Email, sentFor: null));
        Assert.Null(InsightsSchedule.DueReset(s_reset, week: null, Email, sentFor: null));
    }

    [Fact]
    public async Task SendIfDueAsync_runs_the_report_and_mails_it_once()
    {
        var reports = new FakeReports(@"C:\data\report-2026-10-30-090500.html");
        var mail = new FakeMail();
        using var mailer = new InsightsMailer(reports, mail, () => s_reset.AddMinutes(-55));

        var sent = await mailer.SendIfDueAsync(Snapshot(s_reset), $" {Email} ", sentFor: null);
        var again = await mailer.SendIfDueAsync(Snapshot(s_reset), Email, sentFor: null);

        Assert.Equal(s_reset, sent);
        Assert.Null(again);
        Assert.Equal(1, reports.Calls);
        var message = Assert.Single(mail.Sent);
        Assert.Equal(Email, message.To);
        Assert.Equal(@"C:\data\report-2026-10-30-090500.html", message.Attachment);
        Assert.Contains("63%", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendIfDueAsync_does_nothing_without_an_address()
    {
        var reports = new FakeReports("x.html");
        using var mailer = new InsightsMailer(reports, new FakeMail(), () => s_reset.AddMinutes(-30));

        Assert.Null(await mailer.SendIfDueAsync(Snapshot(s_reset), email: null, sentFor: null));
        Assert.Equal(0, reports.Calls);
    }

    [Fact]
    public async Task SendIfDueAsync_does_not_retry_a_failed_week_on_the_next_poll()
    {
        var reports = new FakeReports(error: new InsightsException("no report"));
        var mail = new FakeMail();
        using var mailer = new InsightsMailer(reports, mail, () => s_reset.AddMinutes(-30));

        await Assert.ThrowsAsync<InsightsException>(() => mailer.SendIfDueAsync(Snapshot(s_reset), Email, sentFor: null));
        Assert.Null(await mailer.SendIfDueAsync(Snapshot(s_reset), Email, sentFor: null));

        Assert.Equal(1, reports.Calls);
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public void FindReport_prefers_the_newest_timestamped_report_written_since_the_start()
    {
        var start = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
        Touch("report-2026-10-23-090000.html", start.AddDays(-7));
        Touch("report.html", start.AddMinutes(2));
        Touch("report-2026-10-30-090100.html", start.AddMinutes(1));
        Touch("report-2026-10-30-090200.html", start.AddMinutes(2));

        Assert.Equal(Path.Combine(_folder, "report-2026-10-30-090200.html"), ClaudeInsightsReportSource.FindReport(_folder, start));
    }

    [Fact]
    public void FindReport_falls_back_to_report_html_and_ignores_older_files()
    {
        var start = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
        Touch("report-2026-10-23-090000.html", start.AddDays(-7));

        Assert.Null(ClaudeInsightsReportSource.FindReport(_folder, start));

        Touch("report.html", start.AddMinutes(1));
        Assert.Equal(Path.Combine(_folder, "report.html"), ClaudeInsightsReportSource.FindReport(_folder, start));
    }

    [Fact]
    public void FindReport_is_null_for_a_missing_folder()
    {
        Assert.Null(ClaudeInsightsReportSource.FindReport(Path.Combine(_folder, "missing"), DateTime.UtcNow));
    }

    [Fact]
    public void Settings_round_trip_the_insights_fields()
    {
        var store = new SettingsStore(_folder);
        var saved = new AppSettings(InsightsEmail: Email, InsightsSentFor: s_reset);

        store.Save(saved);

        Assert.Equal(saved, store.Load());
        store.Dispose();
    }

    private void Touch(string name, DateTime writtenUtc)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, "<html></html>");
        File.SetLastWriteTimeUtc(path, writtenUtc);
    }

    private sealed class FakeReports(string? path = null, Exception? error = null) : IInsightsReportSource
    {
        public int Calls { get; private set; }

        public Task<string> CreateReportAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return error is null ? Task.FromResult(path!) : Task.FromException<string>(error);
        }
    }

    private sealed class FakeMail : IMailSender
    {
        public List<(string To, string Subject, string Body, string Attachment)> Sent { get; } = [];

        public Task SendAsync(string recipient, string subject, string body, string attachmentPath)
        {
            Sent.Add((recipient, subject, body, attachmentPath));
            return Task.CompletedTask;
        }
    }
}
