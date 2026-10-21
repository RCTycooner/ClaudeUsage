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
        Assert.Null(SettingsForm.Validate("", "  "));
    }

    [Fact]
    public void Validate_accepts_an_existing_file_and_folder_even_when_quoted()
    {
        string exe = Path.Combine(_folder, "claude.exe");
        File.WriteAllText(exe, "");

        Assert.Null(SettingsForm.Validate($"\"{exe}\"", $" {_folder} "));
    }

    [Fact]
    public void Validate_reports_a_missing_cli()
    {
        string exe = Path.Combine(_folder, "nope.exe");

        Assert.Equal($"Claude CLI not found: {exe}", SettingsForm.Validate(exe, ""));
    }

    [Fact]
    public void Validate_reports_a_missing_projects_folder()
    {
        string root = Path.Combine(_folder, "missing");

        Assert.Equal($"Projects folder not found: {root}", SettingsForm.Validate("", root));
    }

    [Fact]
    public void Apply_stores_blank_fields_as_null_and_keeps_the_position()
    {
        var before = new AppSettings(Topmost: false, Left: 10, Top: 20, ClaudePath: @"C:\old\claude.exe", StartWithWindows: false);

        var after = SettingsForm.Apply(before, " ", "", topmost: true, startWithWindows: true);

        Assert.Equal(new AppSettings(Topmost: true, Left: 10, Top: 20, ClaudePath: null, StartWithWindows: true, ProjectsRoot: null), after);
        Assert.Equal(AppSettings.DefaultProjectsRoot, after.EffectiveProjectsRoot);
    }

    [Fact]
    public void Apply_trims_quotes_and_spaces()
    {
        var after = SettingsForm.Apply(new AppSettings(), " \"C:\\bin\\claude.exe\" ", " E:\\Code ", topmost: false, startWithWindows: false);

        Assert.Equal(@"C:\bin\claude.exe", after.ClaudePath);
        Assert.Equal(@"E:\Code", after.ProjectsRoot);
    }
}
