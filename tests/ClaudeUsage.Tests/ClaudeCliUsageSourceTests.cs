using ClaudeUsage.Usage;

namespace ClaudeUsage.Tests;

public sealed class ClaudeCliUsageSourceTests
{
    [Fact]
    public void ParseEnvelope_returns_result_text()
    {
        const string json = """
            {"type":"result","subtype":"success","is_error":false,"duration_ms":12,"num_turns":0,
             "result":"Current session: 3% used · resets Oct 19, 2am (Europe/Brussels)\n",
             "total_cost_usd":0,"local_command":"usage"}
            """;

        Assert.Equal(
            "Current session: 3% used · resets Oct 19, 2am (Europe/Brussels)\n",
            ClaudeCliUsageSource.ParseEnvelope(json));
    }

    [Fact]
    public void ParseEnvelope_throws_when_is_error()
    {
        const string json = """{"type":"result","is_error":true,"result":"Invalid API key"}""";

        var ex = Assert.Throws<UsageSourceException>(() => ClaudeCliUsageSource.ParseEnvelope(json));

        Assert.Contains("reported an error", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid API key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseEnvelope_throws_when_result_missing()
    {
        var ex = Assert.Throws<UsageSourceException>(
            () => ClaudeCliUsageSource.ParseEnvelope("""{"type":"result","is_error":false}"""));

        Assert.Contains("no 'result'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Error: something went wrong")]
    [InlineData("[1,2,3]")]
    public void ParseEnvelope_throws_on_non_object(string stdout)
    {
        Assert.Throws<UsageSourceException>(() => ClaudeCliUsageSource.ParseEnvelope(stdout));
    }

    [Fact]
    public void ParseEnvelope_truncates_detail_to_500_chars()
    {
        var stdout = new string('x', 2000);

        var ex = Assert.Throws<UsageSourceException>(() => ClaudeCliUsageSource.ParseEnvelope(stdout));

        Assert.True(ex.RawOutput!.Length <= 503);
    }

    [Fact]
    public void ParseEnvelope_skips_a_leading_banner_line()
    {
        const string stdout = "Update available: 2.2.0 (run claude update)\r\n" +
            """{"type":"result","is_error":false,"result":"Current session: 3% used"}""" + "\r\n";

        Assert.Equal("Current session: 3% used", ClaudeCliUsageSource.ParseEnvelope(stdout));
    }

    [Fact]
    public void ParseEnvelope_takes_the_last_result_line_and_ignores_other_json_lines()
    {
        const string stdout = """
            {"type":"system","subtype":"init"}
            {"type":"result","is_error":false,"result":"first"}
            {"type":"result","is_error":false,"result":"last"}
            {"type":"log","message":"bye"}
            """;

        Assert.Equal("last", ClaudeCliUsageSource.ParseEnvelope(stdout));
    }

    [Fact]
    public void ParseEnvelope_fallback_still_honours_is_error()
    {
        const string stdout = "banner\n" + """{"type":"result","is_error":true,"result":"Not logged in"}""";

        var ex = Assert.Throws<UsageSourceException>(() => ClaudeCliUsageSource.ParseEnvelope(stdout));

        Assert.Contains("reported an error", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeEnvironment_removes_session_variables_and_disables_updater()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLAUDECODE"] = "1",
            ["CLAUDE_CODE_ENTRYPOINT"] = "cli",
            ["CLAUDE_CODE_SESSION_ID"] = "abc",
            ["CLAUDE_CODE_SSE_PORT"] = "1234",
            ["CLAUDE_CODE_MESSAGING_SOCKET"] = "x",
            ["CLAUDE_PID"] = "42",
            ["CLAUDE_CODE_GIT_BASH_PATH"] = @"C:\Git\bin\bash.exe",
            ["CLAUDE_CODE_USE_BEDROCK"] = "1",
            ["PATH"] = @"C:\Windows",
        };

        ClaudeCliUsageSource.SanitizeEnvironment(env);

        Assert.Equal(
            ["CLAUDE_CODE_GIT_BASH_PATH", "CLAUDE_CODE_USE_BEDROCK", "DISABLE_AUTOUPDATER", "PATH"],
            env.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("1", env["DISABLE_AUTOUPDATER"]);
    }

    [Fact]
    public void CreateStartInfo_runs_exe_directly()
    {
        var psi = ClaudeCliUsageSource.CreateStartInfo(@"C:\Users\me\.local\bin\claude.exe", @"C:\data");

        Assert.Equal(@"C:\Users\me\.local\bin\claude.exe", psi.FileName);
        Assert.Equal(["-p", "/usage", "--output-format", "json", "--no-session-persistence", "--strict-mcp-config"], psi.ArgumentList);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.CreateNoWindow);
    }

    [Fact]
    public void CreateStartInfo_uses_the_given_arguments()
    {
        var psi = ClaudeCliUsageSource.CreateStartInfo(@"C:\Users\me\.local\bin\claude.exe", @"C:\data", ["-p", "/insights"]);

        Assert.Equal(["-p", "/insights"], psi.ArgumentList);
    }

    [Fact]
    public void CreateStartInfo_runs_cmd_shim_through_cmd_exe_with_quoting()
    {
        var psi = ClaudeCliUsageSource.CreateStartInfo(@"C:\Users\Jane Doe\AppData\Roaming\npm\claude.cmd", @"C:\data");

        Assert.EndsWith("cmd.exe", psi.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(psi.ArgumentList);
        Assert.Equal(
            "/d /s /c \"\"C:\\Users\\Jane Doe\\AppData\\Roaming\\npm\\claude.cmd\" -p /usage --output-format json --no-session-persistence --strict-mcp-config\"",
            psi.Arguments);
    }

    [Fact]
    public void ResolveExe_prefers_existing_override()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.Equal(file, ClaudeCliUsageSource.ResolveExe(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
