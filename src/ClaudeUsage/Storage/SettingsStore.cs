using System.IO;
using System.Text.Json;

namespace ClaudeUsage.Storage;

/// <summary>Persists <see cref="AppSettings"/> as settings.json in the given folder.</summary>
public sealed class SettingsStore : IDisposable
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeSpan s_debounce = TimeSpan.FromMilliseconds(500);

    private readonly string _folder;
    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly Timer _timer;
    private AppSettings? _pending;

    public SettingsStore(string folder)
    {
        _folder = folder;
        _path = Path.Combine(folder, "settings.json");
        _timer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsage");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), s_json) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable settings are not worth failing over; fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            _pending = null;
            Write(settings);
        }
    }

    /// <summary>Schedules a save 500 ms from now; further calls within that window replace the pending value.</summary>
    public void QueueSave(AppSettings settings)
    {
        lock (_gate)
        {
            _pending = settings;
            _timer.Change(s_debounce, Timeout.InfiniteTimeSpan);
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

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }

    private void Write(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, s_json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a settings write is not fatal.
        }
    }
}
