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
    public async Task QueueSave_coalesces_calls_within_the_debounce_window()
    {
        using var store = new SettingsStore(_folder, TimeSpan.FromMilliseconds(150));

        store.QueueSave(new AppSettings(Left: 1));
        store.QueueSave(new AppSettings(Left: 2));
        store.QueueSave(new AppSettings(Left: 3));

        Assert.False(File.Exists(store.Path));
        await WaitUntilAsync(() => File.Exists(store.Path));
        Assert.Equal(new AppSettings(Left: 3), store.Load());
    }

    [Fact]
    public void Flush_writes_the_pending_value_immediately()
    {
        using var store = new SettingsStore(_folder, TimeSpan.FromHours(1));
        store.QueueSave(new AppSettings(Topmost: true, Left: 10, Top: 20));

        store.Flush();

        Assert.Equal(new AppSettings(Topmost: true, Left: 10, Top: 20), store.Load());
    }

    [Fact]
    public void Dispose_flushes_the_pending_value()
    {
        var store = new SettingsStore(_folder, TimeSpan.FromHours(1));
        store.QueueSave(new AppSettings(Left: 42));

        store.Dispose();

        using var reader = new SettingsStore(_folder);
        Assert.Equal(new AppSettings(Left: 42), reader.Load());
    }

    [Fact]
    public void QueueSave_after_Dispose_writes_directly()
    {
        var store = new SettingsStore(_folder, TimeSpan.FromHours(1));
        store.Dispose();

        store.QueueSave(new AppSettings(Top: 7));

        Assert.Equal(new AppSettings(Top: 7), store.Load());
    }

    [Fact]
    public void Save_writes_utf8_without_bom_and_leaves_no_temp_file()
    {
        using var store = new SettingsStore(_folder);

        store.Save(new AppSettings(ClaudePath: @"C:\Programme\Ünïcode\claude.exe"));

        var bytes = File.ReadAllBytes(store.Path);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]);
        Assert.Equal(["settings.json"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public void Load_tolerates_a_bom()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), """{ "topmost": true }""", new System.Text.UTF8Encoding(true));

        using var store = new SettingsStore(_folder);

        Assert.True(store.Load().Topmost);
    }

    [Fact]
    public void Concurrent_saves_do_not_throw_or_corrupt_the_file()
    {
        using var store = new SettingsStore(_folder, TimeSpan.FromMilliseconds(1));

        Parallel.For(0, 200, i =>
        {
            if (i % 2 == 0)
            {
                store.Save(new AppSettings(Left: i));
            }
            else
            {
                store.QueueSave(new AppSettings(Left: i));
            }
        });
        store.Flush();

        Assert.NotNull(store.Load().Left);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for condition.");
            await Task.Delay(20);
        }
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
