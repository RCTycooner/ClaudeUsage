using ClaudeUsage.Projects;

namespace ClaudeUsage.Tests;

public sealed class ProjectScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ClaudeUsageTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var dir in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(dir, FileAttributes.Directory);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Finds_folders_with_CLAUDE_md_at_any_depth_sorted_by_name()
    {
        string othello = AddProject(@"Git\Othello");
        string chess = AddProject(@"Other\Deep\Down\Chess");
        Directory.CreateDirectory(Path.Combine(_root, "NoMarker"));

        var projects = ProjectScanner.Scan(_root);

        Assert.Equal([new ClaudeProject("Chess", chess), new ClaudeProject("Othello", othello)], projects);
    }

    [Fact]
    public void Includes_the_root_and_nested_projects()
    {
        AddProject("");
        AddProject(@"Mono\Inner");

        var names = ProjectScanner.Scan(_root).Select(p => p.Name);

        Assert.Equal([Path.GetFileName(_root), "Inner"], names);
    }

    [Fact]
    public void Skips_build_output_dependencies_and_claude_worktrees()
    {
        AddProject(@"App\node_modules\pkg");
        AddProject(@"App\bin\Debug");
        AddProject(@"App\.claude\worktrees\feature");
        AddProject(@"App\.git\x");

        Assert.Empty(ProjectScanner.Scan(_root));
    }

    [Fact]
    public void Skips_hidden_folders()
    {
        AddProject(@"Hidden\Project");
        File.SetAttributes(Path.Combine(_root, "Hidden"), FileAttributes.Directory | FileAttributes.Hidden);

        Assert.Empty(ProjectScanner.Scan(_root));
    }

    [Fact]
    public void Same_names_in_different_folders_are_all_listed()
    {
        string a = AddProject(@"A\Tool");
        string b = AddProject(@"B\Tool");

        Assert.Equal([a, b], ProjectScanner.Scan(_root).Select(p => p.Directory));
    }

    [Fact]
    public void Missing_root_gives_an_empty_list()
    {
        Assert.Empty(ProjectScanner.Scan(Path.Combine(_root, "does-not-exist")));
    }

    [Theory]
    [InlineData(@"D:\Git-Calidos\Othello", "Othello")]
    [InlineData(@"D:\Git-Calidos\Othello\", "Othello")]
    [InlineData(@"D:\", @"D:\")]
    public void DisplayName_is_the_folder_name(string folder, string expected)
    {
        Assert.Equal(expected, ProjectScanner.DisplayName(folder));
    }

    [Fact]
    public void Launcher_opens_powershell_running_claude_in_the_folder()
    {
        var psi = ProjectLauncher.CreateStartInfo(@"D:\Git\Othello");

        Assert.Equal("powershell.exe", psi.FileName);
        Assert.Equal("-NoExit -Command claude", psi.Arguments);
        Assert.Equal(@"D:\Git\Othello", psi.WorkingDirectory);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void Launcher_does_not_pass_on_parent_claude_session_variables()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CODE_CHILD_SESSION", "1");
        try
        {
            var psi = ProjectLauncher.CreateStartInfo(@"D:\Git\Othello");

            Assert.False(psi.Environment.ContainsKey("CLAUDE_CODE_CHILD_SESSION"));
            Assert.False(psi.Environment.ContainsKey("DISABLE_AUTOUPDATER"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_CHILD_SESSION", null);
        }
    }

    private string AddProject(string relative)
    {
        string dir = Path.Combine(_root, relative);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ProjectScanner.MarkerFileName), "# test");
        return Path.TrimEndingDirectorySeparator(dir);
    }
}
