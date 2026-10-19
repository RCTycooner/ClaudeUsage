using ClaudeUsage.Storage;
using ClaudeUsage.Usage;

namespace ClaudeUsage.Tests;

public sealed class UsageHistoryStoreTests : IDisposable
{
    private static readonly TimeSpan s_cest = TimeSpan.FromHours(2);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ClaudeUsageTests-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 18, 21, 5, 12, s_cest);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private UsageHistoryStore CreateStore() => new(_folder, clock: () => _now);

    private static UsageSnapshot Snapshot(DateTimeOffset taken, int s = 3, int w = 17, int? f = 22) => new(
        taken,
        new UsageBucket(s, new DateTimeOffset(2026, 10, 19, 2, 0, 0, s_cest)),
        new UsageBucket(w, new DateTimeOffset(2026, 10, 20, 10, 0, 0, s_cest)),
        f is { } fv ? new UsageBucket(fv, new DateTimeOffset(2026, 10, 20, 10, 0, 0, s_cest)) : null,
        "raw");

    [Fact]
    public void Missing_file_is_empty_history()
    {
        Assert.Empty(CreateStore().Load());
    }

    [Fact]
    public void Append_then_Load_round_trips()
    {
        var store = CreateStore();
        store.Load();
        store.Append(Snapshot(_now.AddMinutes(-1).AddMilliseconds(345), s: 2));
        store.Append(Snapshot(_now, f: null));

        var loaded = CreateStore().Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(new UsageSample(
            _now.AddMinutes(-1), 2, 17, 22,
            new DateTimeOffset(2026, 10, 19, 2, 0, 0, s_cest),
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, s_cest),
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, s_cest)), loaded[0]);
        Assert.Null(loaded[1].ModelWeek);
        Assert.Null(loaded[1].ModelWeekResetsAt);
        Assert.Equal(loaded, store.Samples);
    }

    [Fact]
    public void Lines_use_compact_keys()
    {
        var store = CreateStore();
        store.Append(Snapshot(_now));

        var line = Assert.Single(File.ReadAllLines(store.Path));

        Assert.Equal(
            """{"t":"2026-10-18T21:05:12+02:00","s":3,"w":17,"f":22,"sr":"2026-10-19T02:00:00+02:00","wr":"2026-10-20T10:00:00+02:00","fr":"2026-10-20T10:00:00+02:00"}""",
            line);
    }

    [Fact]
    public void Load_prunes_samples_older_than_the_retention_window_and_rewrites_file()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "history.jsonl");
        File.WriteAllLines(path,
        [
            UsageHistoryStore.FormatLine(UsageSample.FromSnapshot(Snapshot(_now - UsageHistoryStore.DefaultRetention - TimeSpan.FromDays(1)))),
            UsageHistoryStore.FormatLine(UsageSample.FromSnapshot(Snapshot(_now - UsageHistoryStore.DefaultRetention + TimeSpan.FromDays(1)))),
            UsageHistoryStore.FormatLine(UsageSample.FromSnapshot(Snapshot(_now))),
        ]);

        var loaded = CreateStore().Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(_now - UsageHistoryStore.DefaultRetention + TimeSpan.FromDays(1), loaded[0].Taken);
        Assert.Equal(2, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void Append_prunes_once_a_day()
    {
        var store = CreateStore();
        store.Load();
        store.Append(Snapshot(_now));

        _now += UsageHistoryStore.DefaultRetention + TimeSpan.FromDays(1);
        store.Append(Snapshot(_now));

        Assert.Single(store.Samples);
        Assert.Single(File.ReadAllLines(store.Path));
    }

    [Fact]
    public void Corrupt_lines_are_skipped()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "history.jsonl");
        File.WriteAllLines(path,
        [
            UsageHistoryStore.FormatLine(UsageSample.FromSnapshot(Snapshot(_now.AddMinutes(-2)))),
            "{ not json",
            """{"s":5}""",
            "",
            UsageHistoryStore.FormatLine(UsageSample.FromSnapshot(Snapshot(_now))),
        ]);

        var loaded = CreateStore().Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(_now, loaded[1].Taken);

        // The file was rewritten without the bad lines.
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.NotNull(UsageHistoryStore.TryParseLine(l)));
        Assert.Equal(lines.Select(UsageHistoryStore.TryParseLine), loaded);
    }

    [Fact]
    public void Model_name_round_trips_and_old_lines_read_as_model_week()
    {
        var store = CreateStore();
        store.Append(Snapshot(_now) with { ModelName = "Opus" });

        var line = Assert.Single(File.ReadAllLines(store.Path));
        Assert.Contains("\"m\":\"Opus\"", line, StringComparison.Ordinal);
        Assert.Equal("Opus week", CreateStore().Load()[0].ToSnapshot().ModelWeekLabel);

        var old = UsageHistoryStore.TryParseLine("""{"t":"2026-10-18T21:05:12+02:00","f":22}""")!;
        Assert.Equal("Model week", old.ToSnapshot().ModelWeekLabel);
        Assert.Equal(22, old.ToSnapshot().ModelWeek!.Percent);
    }

    [Fact]
    public void Writes_utf8_without_bom_and_reads_files_with_one()
    {
        var store = CreateStore();
        store.Append(Snapshot(_now));
        Assert.False(File.ReadAllBytes(store.Path) is [0xEF, 0xBB, 0xBF, ..]);

        File.WriteAllText(
            store.Path,
            UsageHistoryStore.FormatLine(UsageSample.FromSnapshot(Snapshot(_now))) + "\n",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Single(CreateStore().Load());
        Assert.False(File.ReadAllBytes(store.Path) is [0xEF, 0xBB, 0xBF, ..]);
    }

    [Fact]
    public void ToSnapshot_restores_buckets()
    {
        var sample = UsageSample.FromSnapshot(Snapshot(_now, f: null));

        var snapshot = sample.ToSnapshot();

        Assert.Equal(3, snapshot.Session!.Percent);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 0, 0, s_cest), snapshot.Week!.ResetsAt);
        Assert.Null(snapshot.ModelWeek);
    }
}
