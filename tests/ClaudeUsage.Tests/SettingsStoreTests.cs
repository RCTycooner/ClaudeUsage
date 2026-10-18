using ClaudeUsage.Storage;

namespace ClaudeUsage.Tests;

public sealed class SettingsStoreTests : IDisposable
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
    public void Load_returns_defaults_when_file_is_missing()
    {
        using var store = new SettingsStore(_folder);

        Assert.Equal(new AppSettings(), store.Load());
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var settings = new AppSettings(Topmost: true, Left: 120.5, Top: 80, ClaudePath: @"C:\bin\claude.exe");

        using var store = new SettingsStore(_folder);
        store.Save(settings);

        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_corrupt()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{ not json");

        using var store = new SettingsStore(_folder);

        Assert.Equal(new AppSettings(), store.Load());
    }
}
