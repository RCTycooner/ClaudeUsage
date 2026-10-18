using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeUsage.Storage;

/// <summary>
/// Persists <see cref="AppSettings"/> as settings.json in the given folder. Thread-safe: all writes go through one
/// lock and replace the file atomically (temp file + move), so a crash never leaves half a file behind.
/// </summary>
public sealed class SettingsStore : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _folder;
    private readonly TimeSpan _debounce;
    private readonly Lock _gate = new();
    private readonly Timer _timer;
    private AppSettings? _pending;
    private bool _disposed;

    public SettingsStore(string folder, TimeSpan? debounce = null)
    {
        _folder = folder;
        _debounce = debounce ?? DefaultDebounce;
        Path = System.IO.Path.Combine(folder, "settings.json");
        _timer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public static string DefaultFolder { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsage");

    public string Path { get; }

    /// <summary>Reads the file. Meant to be called once at startup; the caller keeps the value.</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                // Encoding.UTF8 skips a BOM if an older version (or a text editor) wrote one.
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path, Encoding.UTF8), s_json) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable settings are not worth failing over; fall back to defaults.
        }

        return new AppSettings();
    }

    /// <summary>Writes immediately and drops any pending debounced value.</summary>
    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            _pending = null;
            Write(settings);
        }
    }

    /// <summary>Schedules a save after the debounce delay; further calls within that window replace the pending value.</summary>
    public void QueueSave(AppSettings settings)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                Write(settings);
                return;
            }

            _pending = settings;
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes any pending value immediately.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_pending is { } pending)
            {
                _pending = null;
                Write(pending);
            }
        }
    }

    /// <summary>Stops the debounce timer and writes any pending value.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Dispose();
        }

        Flush();
    }

    private void Write(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            TextFiles.WriteAllTextAtomic(Path, JsonSerializer.Serialize(settings, s_json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a settings write is not fatal.
        }
    }
}
