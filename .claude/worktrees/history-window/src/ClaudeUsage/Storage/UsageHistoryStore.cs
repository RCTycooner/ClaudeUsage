using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeUsage.Usage;

namespace ClaudeUsage.Storage;

/// <summary>One history point. Percentages are as reported (not clamped); null where the line was missing.</summary>
public sealed record UsageSample(
    DateTimeOffset Taken,
    int? Session,
    int? Week,
    int? ModelWeek,
    DateTimeOffset? SessionResetsAt,
    DateTimeOffset? WeekResetsAt,
    DateTimeOffset? ModelWeekResetsAt,
    string? ModelName = null)
{
    public static UsageSample FromSnapshot(UsageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Whole seconds keep the JSONL compact.
        var t = snapshot.Taken;
        var taken = new DateTimeOffset(t.Ticks - (t.Ticks % TimeSpan.TicksPerSecond), t.Offset);
        return new UsageSample(
            taken,
            snapshot.Session?.Percent,
            snapshot.Week?.Percent,
            snapshot.ModelWeek?.Percent,
            snapshot.Session?.ResetsAt,
            snapshot.Week?.ResetsAt,
            snapshot.ModelWeek?.ResetsAt,
            snapshot.ModelWeek is null ? null : snapshot.ModelName);
    }

    public UsageSnapshot ToSnapshot() => new(
        Taken,
        Session is { } s ? new UsageBucket(s, SessionResetsAt) : null,
        Week is { } w ? new UsageBucket(w, WeekResetsAt) : null,
        ModelWeek is { } f ? new UsageBucket(f, ModelWeekResetsAt) : null,
        RawText: "",
        ModelName);
}

/// <summary>
/// history.jsonl: one compact JSON object per poll, append-only, pruned to the retention window on load and
/// once a day. Corrupt lines are skipped. Thread-safe.
/// </summary>
public sealed class UsageHistoryStore
{
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(90);

    private static readonly TimeSpan s_pruneEvery = TimeSpan.FromDays(1);
    private static readonly JsonSerializerOptions s_json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _folder;
    private readonly TimeSpan _retention;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Lock _gate = new();
    private readonly List<UsageSample> _samples = [];
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    public UsageHistoryStore(string folder, TimeSpan? retention = null, Func<DateTimeOffset>? clock = null)
    {
        _folder = folder;
        Path = System.IO.Path.Combine(folder, "history.jsonl");
        _retention = retention ?? DefaultRetention;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Path { get; }

    /// <summary>A copy of the samples, oldest first.</summary>
    public IReadOnlyList<UsageSample> Samples
    {
        get
        {
            lock (_gate)
            {
                return [.. _samples];
            }
        }
    }

    public UsageSample? Latest
    {
        get
        {
            lock (_gate)
            {
                return _samples.Count > 0 ? _samples[^1] : null;
            }
        }
    }

    /// <summary>Reads the file (missing file = empty), skips corrupt lines, then prunes.</summary>
    public IReadOnlyList<UsageSample> Load()
    {
        lock (_gate)
        {
            _samples.Clear();
            bool dirty = false;
            try
            {
                if (File.Exists(Path))
                {
                    // A BOM from an older version is read fine, but rewrite the file once to drop it.
                    dirty = StartsWithBom(Path);
                    // Encoding.UTF8 on read still detects and skips a BOM written by older versions.
                    foreach (var line in File.ReadLines(Path, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        if (TryParseLine(line) is { } sample)
                        {
                            _samples.Add(sample);
                        }
                        else
                        {
                            dirty = true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable history: start empty rather than fail.
            }

            _samples.Sort((a, b) => a.Taken.CompareTo(b.Taken));
            PruneLocked(rewriteAlways: dirty);
            return [.. _samples];
        }
    }

    public UsageSample Append(UsageSnapshot snapshot)
    {
        var sample = UsageSample.FromSnapshot(snapshot);
        lock (_gate)
        {
            _samples.Add(sample);
            TryWrite(() => File.AppendAllText(Path, FormatLine(sample) + "\n", TextFiles.Utf8NoBom));
            if (_clock() - _lastPrune >= s_pruneEvery)
            {
                PruneLocked(rewriteAlways: false);
            }
        }

        return sample;
    }

    /// <summary>Drops samples older than the retention window and rewrites the file if anything was removed.</summary>
    public void Prune()
    {
        lock (_gate)
        {
            PruneLocked(rewriteAlways: false);
        }
    }

    public static string FormatLine(UsageSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var dto = new Line
        {
            T = sample.Taken,
            S = sample.Session,
            W = sample.Week,
            F = sample.ModelWeek,
            Sr = sample.SessionResetsAt,
            Wr = sample.WeekResetsAt,
            Fr = sample.ModelWeekResetsAt,
            M = sample.ModelName,
        };
        return JsonSerializer.Serialize(dto, s_json);
    }

    public static UsageSample? TryParseLine(string line)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<Line>(line, s_json);
            return dto?.T is { } t
                ? new UsageSample(t, dto.S, dto.W, dto.F, dto.Sr, dto.Wr, dto.Fr, dto.M)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void PruneLocked(bool rewriteAlways)
    {
        var now = _clock();
        _lastPrune = now;
        var cutoff = now - _retention;
        int removed = _samples.RemoveAll(s => s.Taken < cutoff);
        if (removed > 0 || rewriteAlways)
        {
            var text = new StringBuilder();
            foreach (var s in _samples)
            {
                text.Append(FormatLine(s)).Append('\n');
            }

            TryWrite(() => TextFiles.WriteAllTextAtomic(Path, text.ToString()));
        }
    }

    private static bool StartsWithBom(string path)
    {
        Span<byte> head = stackalloc byte[3];
        using var stream = File.OpenRead(path);
        return stream.ReadAtLeast(head, 3, throwOnEndOfStream: false) == 3 && head is [0xEF, 0xBB, 0xBF];
    }

    private void TryWrite(Action write)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a history write is not fatal; the in-memory list stays correct.
        }
    }

    private sealed class Line
    {
        [JsonPropertyName("t")]
        public DateTimeOffset? T { get; set; }

        [JsonPropertyName("s")]
        public int? S { get; set; }

        [JsonPropertyName("w")]
        public int? W { get; set; }

        [JsonPropertyName("f")]
        public int? F { get; set; }

        [JsonPropertyName("sr")]
        public DateTimeOffset? Sr { get; set; }

        [JsonPropertyName("wr")]
        public DateTimeOffset? Wr { get; set; }

        [JsonPropertyName("fr")]
        public DateTimeOffset? Fr { get; set; }

        /// <summary>Name of the per-model bucket ("Fable"); absent in older lines, which then read as "Model week".</summary>
        [JsonPropertyName("m")]
        public string? M { get; set; }
    }
}
