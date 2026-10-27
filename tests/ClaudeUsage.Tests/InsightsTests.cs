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

    private static readonly DateTimeOffset s_next = s_reset + InsightsSchedule.Week;

    private static UsageSnapshot Snapshot(DateTimeOffset? weekResets, int percent = 63) =>
        new(s_reset.AddDays(-1), Session: null, Week: new UsageBucket(percent, weekResets), ModelWeek: null, RawText: "");

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(64)]
    public void DueReset_is_the_reset_that_passed_from_five_minutes_to_an_hour_after_it(int minutesAfter)
    {
        var now = s_reset.AddMinutes(minutesAfter);

        // After a reset /usage reports the next one, or briefly still the one that just passed.
        Assert.Equal(s_reset, InsightsSchedule.DueReset(now, new UsageBucket(0, s_next), Email, sentFor: null));
        Assert.Equal(s_reset, InsightsSchedule.DueReset(now, new UsageBucket(100, s_reset), Email, sentFor: null));
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(65)]
    [InlineData(24 * 60)]
    public void DueReset_is_null_outside_that_window(int minutesAfter)
    {
        var week = new UsageBucket(50, minutesAfter < 0 ? s_reset : s_next);

        Assert.Null(InsightsSchedule.DueReset(s_reset.AddMinutes(minutesAfter), week, Email, sentFor: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DueReset_is_null_without_an_address(string? email)
    {
        Assert.Null(InsightsSchedule.DueReset(s_reset.AddMinutes(30), new UsageBucket(50, s_next), email, sentFor: null));
    }

    [Fact]
    public void DueReset_is_null_when_already_sent_for_that_reset_even_in_another_offset()
    {
        var now = s_reset.AddMinutes(30);
        var week = new UsageBucket(50, s_next);

        Assert.Null(InsightsSchedule.DueReset(now, week, Email, sentFor: s_reset.ToUniversalTime()));
        Assert.Equal(s_reset, InsightsSchedule.DueReset(now, week, Email, sentFor: s_reset.AddDays(-7)));
    }

    [Fact]
    public void DueReset_treats_a_reset_rounded_a_minute_differently_as_already_sent()
    {
        // /usage reports the same reset as 9:59 on one poll and 10:00 on the next.
        var now = s_reset.AddMinutes(30);

        Assert.Null(InsightsSchedule.DueReset(now, new UsageBucket(0, s_next), Email, sentFor: s_reset.AddMinutes(-1)));
        Assert.Null(InsightsSchedule.DueReset(now, new UsageBucket(0, s_next.AddMinutes(-1)), Email, sentFor: s_reset));
    }

    [Fact]
    public void DueReset_is_null_when_the_reset_time_is_unknown()
    {
        Assert.Null(InsightsSchedule.DueReset(s_reset.AddMinutes(30), new UsageBucket(50, null), Email, sentFor: null));
        Assert.Null(InsightsSchedule.DueReset(s_reset.AddMinutes(30), week: null, Email, sentFor: null));
    }

    [Fact]
    public async Task SendIfDueAsync_runs_the_report_and_mails_it_once()
    {
        var reports = new FakeReports(@"C:\data\report-2026-10-30-100500.html");
        var mail = new FakeMail();
        using var mailer = new InsightsMailer(reports, mail, () => s_reset.AddMinutes(6));

        var sent = await mailer.SendIfDueAsync(Snapshot(s_next, 0), $" {Email} ", sentFor: null);
        var again = await mailer.SendIfDueAsync(Snapshot(s_next, 0), Email, sentFor: null);

        Assert.Equal(s_reset, sent);
        Assert.Null(again);
        Assert.Equal(1, reports.Calls);
        var message = Assert.Single(mail.Sent);
        Assert.Equal(Email, message.To);
        Assert.Equal(@"C:\data\report-2026-10-30-100500.html", message.Attachment);
    }

    [Fact]
    public async Task SendIfDueAsync_mails_once_while_the_reported_reset_flips_by_a_minute()
    {
        var reports = new FakeReports("x.html");
        var mail = new FakeMail();
        var now = s_reset.AddMinutes(5);
        using var mailer = new InsightsMailer(reports, mail, () => now);
        DateTimeOffset? sentFor = null;

        for (int poll = 0; poll < 10; poll++, now = now.AddMinutes(1))
        {
            var reported = poll % 2 == 0 ? s_next : s_next.AddMinutes(-1);
            sentFor = await mailer.SendIfDueAsync(Snapshot(reported, 0), Email, sentFor) ?? sentFor;
        }

        Assert.Equal(1, reports.Calls);
        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task SendIfDueAsync_reports_the_percentage_of_the_week_that_ended()
    {
        var mail = new FakeMail();
        var now = s_reset.AddMinutes(-2);
        using var mailer = new InsightsMailer(new FakeReports("x.html"), mail, () => now);

        Assert.Null(await mailer.SendIfDueAsync(Snapshot(s_reset.AddMinutes(-1), 97), Email, sentFor: null));
        now = s_reset.AddMinutes(5);
        await mailer.SendIfDueAsync(Snapshot(s_next, 0), Email, sentFor: null);

        var message = Assert.Single(mail.Sent);
        Assert.Contains("97%", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendIfDueAsync_leaves_out_the_percentage_when_the_ended_week_was_not_seen()
    {
        var mail = new FakeMail();
        using var mailer = new InsightsMailer(new FakeReports("x.html"), mail, () => s_reset.AddMinutes(10));

        await mailer.SendIfDueAsync(Snapshot(s_next, 0), Email, sentFor: null);

        var message = Assert.Single(mail.Sent);
        Assert.DoesNotContain("%", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendIfDueAsync_does_nothing_without_an_address()
    {
        var reports = new FakeReports("x.html");
        using var mailer = new InsightsMailer(reports, new FakeMail(), () => s_reset.AddMinutes(30));

        Assert.Null(await mailer.SendIfDueAsync(Snapshot(s_next), email: null, sentFor: null));
        Assert.Equal(0, reports.Calls);
    }

    [Fact]
    public async Task SendIfDueAsync_retries_a_failed_week_after_the_retry_delay_not_on_every_poll()
    {
        var reports = new FakeReports(Path.Combine(_folder, "report.html"), new InsightsException("no report"), failures: 1);
        var mail = new FakeMail();
        var now = s_reset.AddMinutes(5);
        using var mailer = new InsightsMailer(reports, mail, () => now);

        await Assert.ThrowsAsync<InsightsException>(() => mailer.SendIfDueAsync(Snapshot(s_next), Email, sentFor: null));
        now += InsightsMailer.RetryDelay - TimeSpan.FromMinutes(1);
        Assert.Null(await mailer.SendIfDueAsync(Snapshot(s_next.AddMinutes(-1)), Email, sentFor: null));
        Assert.Equal(1, reports.Calls);

        now += TimeSpan.FromMinutes(1);
        Assert.Equal(s_reset, await mailer.SendIfDueAsync(Snapshot(s_next), Email, sentFor: null));
        now += InsightsMailer.RetryDelay;
        Assert.Null(await mailer.SendIfDueAsync(Snapshot(s_next), Email, sentFor: null));

        Assert.Equal(2, reports.Calls);
        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task SendIfDueAsync_retries_a_failed_mail_with_the_report_already_made()
    {
        string report = Path.Combine(_folder, "report.html");
        File.WriteAllText(report, "<html></html>");
        var reports = new FakeReports(report);
        var mail = new FakeMail(failures: 1);
        var now = s_reset.AddMinutes(5);
        using var mailer = new InsightsMailer(reports, mail, () => now);

        await Assert.ThrowsAsync<InsightsException>(() => mailer.SendIfDueAsync(Snapshot(s_next), Email, sentFor: null));
        now += InsightsMailer.RetryDelay;
        Assert.Equal(s_reset, await mailer.SendIfDueAsync(Snapshot(s_next), Email, sentFor: null));

        Assert.Equal(1, reports.Calls);
        Assert.Equal(report, Assert.Single(mail.Sent).Attachment);
    }

    [Fact]
    public async Task SendIfDueAsync_stops_retrying_once_the_report_is_no_longer_due()
    {
        var reports = new FakeReports(error: new InsightsException("no report"));
        var now = s_reset.AddMinutes(5);
        using var mailer = new InsightsMailer(reports, new FakeMail(), () => now);

        for (; now < s_reset.AddHours(3); now += TimeSpan.FromMinutes(1))
        {
            try
            {
                await mailer.SendIfDueAsync(Snapshot(s_next), Email, sentFor: null);
            }
            catch (InsightsException)
            {
                // Expected for each attempt.
            }
        }

        // Due for an hour from 5 minutes after the reset, with an attempt every 15 minutes.
        Assert.Equal(4, reports.Calls);
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

    /// <summary>Fails with <paramref name="error"/> on the first <paramref name="failures"/> calls (all, by default).</summary>
    private sealed class FakeReports(string? path = null, Exception? error = null, int failures = int.MaxValue)
        : IInsightsReportSource
    {
        public int Calls { get; private set; }

        public Task<string> CreateReportAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return error is null || Calls > failures ? Task.FromResult(path!) : Task.FromException<string>(error);
        }
    }

    /// <summary>Fails the first <paramref name="failures"/> sends.</summary>
    private sealed class FakeMail(int failures = 0) : IMailSender
    {
        private int _calls;

        public List<(string To, string Subject, string Body, string Attachment)> Sent { get; } = [];

        public Task SendAsync(string recipient, string subject, string body, string attachmentPath)
        {
            if (++_calls <= failures)
            {
                return Task.FromException(new InsightsException("Outlook is not running"));
            }

            Sent.Add((recipient, subject, body, attachmentPath));
            return Task.CompletedTask;
        }
    }
}
