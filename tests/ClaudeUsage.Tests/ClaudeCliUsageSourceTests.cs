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
