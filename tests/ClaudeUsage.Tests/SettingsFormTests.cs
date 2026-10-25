using ClaudeUsage.Storage;
using ClaudeUsage.UI;

namespace ClaudeUsage.Tests;

public sealed class SettingsFormTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ClaudeUsageTests", Guid.NewGuid().ToString("N"));

    public SettingsFormTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Validate_accepts_blank_fields()
    {
        Assert.Null(SettingsForm.Validate("", "  ", " ", " "));
    }

    [Fact]
    public void Validate_accepts_an_existing_file_and_folder_even_when_quoted()
    {
        string exe = Path.Combine(_folder, "claude.exe");
        File.WriteAllText(exe, "");

        Assert.Null(SettingsForm.Validate($"\"{exe}\"", $" {_folder} ", "", ""));
    }

    [Fact]
    public void Validate_reports_a_missing_cli()
    {
        string exe = Path.Combine(_folder, "nope.exe");

        Assert.Equal($"Claude CLI not found: {exe}", SettingsForm.Validate(exe, "", "", ""));
    }

    [Fact]
    public void Validate_reports_a_missing_projects_folder()
    {
        string root = Path.Combine(_folder, "missing");

        Assert.Equal($"Projects folder not found: {root}", SettingsForm.Validate("", root, "", ""));
    }

    [Theory]
    [InlineData("me@example.com")]
    [InlineData(" first.last+tag@sub.example.be ")]
    public void Validate_accepts_an_email_address(string email)
    {
        Assert.Null(SettingsForm.Validate("", "", email, ""));
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("Me <me@example.com>")]
    [InlineData("a@example.com; b@example.com")]
    public void Validate_reports_an_invalid_email_address(string email)
    {
        Assert.Equal($"Not a valid email address: {email}", SettingsForm.Validate("", "", email, ""));
    }

    [Theory]
    [InlineData("1")]
    [InlineData(" 5 ")]
    public void Validate_accepts_a_refresh_interval_in_range(string minutes)
    {
        Assert.Null(SettingsForm.Validate("", "", "", minutes));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("two")]
    public void Validate_reports_a_refresh_interval_out_of_range_or_not_a_number(string minutes)
    {
        Assert.Equal(
            $"Refresh interval must be a whole number of minutes from 1 to 5: {minutes}", SettingsForm.Validate("", "", "", minutes));
    }

    [Fact]
    public void Apply_stores_blank_fields_as_null_and_keeps_the_position()
    {
        var before = new AppSettings(
            Topmost: false, Left: 10, Top: 20, ClaudePath: @"C:\old\claude.exe", StartWithWindows: false, InsightsEmail: "old@example.com",
            RefreshMinutes: 3);

        var after = SettingsForm.Apply(before, " ", "", "  ", " ", topmost: true, startWithWindows: true);

        Assert.Equal(new AppSettings(Topmost: true, Left: 10, Top: 20, ClaudePath: null, StartWithWindows: true, ProjectsRoot: null), after);
        Assert.Equal(AppSettings.DefaultProjectsRoot, after.EffectiveProjectsRoot);
        Assert.Equal(TimeSpan.FromMinutes(1), after.EffectiveRefreshInterval);
    }

    [Fact]
    public void Apply_trims_quotes_and_spaces()
    {
        var after = SettingsForm.Apply(
            new AppSettings(), " \"C:\\bin\\claude.exe\" ", " E:\\Code ", " me@example.com ", " 4 ", topmost: false, startWithWindows: false);

        Assert.Equal(@"C:\bin\claude.exe", after.ClaudePath);
        Assert.Equal(@"E:\Code", after.ProjectsRoot);
        Assert.Equal("me@example.com", after.InsightsEmail);
        Assert.Equal(4, after.RefreshMinutes);
    }

    [Fact]
    public void Apply_keeps_the_week_the_insights_report_was_sent_for()
    {
        var sentFor = new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.FromHours(1));

        var after = SettingsForm.Apply(new AppSettings(InsightsSentFor: sentFor), "", "", "me@example.com", "", false, false);

        Assert.Equal(sentFor, after.InsightsSentFor);
    }
}
