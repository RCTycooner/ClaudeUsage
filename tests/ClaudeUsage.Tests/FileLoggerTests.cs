using ClaudeUsage.Storage;

namespace ClaudeUsage.Tests;

public sealed class FileLoggerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ClaudeUsageTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Log_writes_utf8_without_bom()
    {
        var logger = new FileLogger(_folder);

        logger.Log("première", new InvalidOperationException("boom"), "raw · output");
        logger.Log("second");
        Assert.True(logger.Flush());

        var bytes = File.ReadAllBytes(logger.Path);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]);
        var text = File.ReadAllText(logger.Path);
        Assert.Contains("première", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: boom", text, StringComparison.Ordinal);
        Assert.Contains("raw · output", text, StringComparison.Ordinal);
        Assert.Contains("second", text, StringComparison.Ordinal);
    }
}
