using System.Globalization;
using System.IO;
using System.Text;

namespace ClaudeUsage.Storage;

/// <summary>
/// Appends timestamped lines to log.txt. When the file exceeds the cap it is moved to log.old.txt and a new one
/// is started, so at most about twice the cap is kept. Thread-safe; never throws on I/O errors. Lines are written
/// in order on a background <see cref="SerialWriter"/>, so <see cref="Log"/> never blocks; <see cref="Flush"/> before exit.
/// </summary>
public sealed class FileLogger
{
    public const long DefaultMaxBytes = 256 * 1024;

    private readonly string _folder;
    private readonly long _maxBytes;
    private readonly SerialWriter _writer = new();

    public FileLogger(string folder, long maxBytes = DefaultMaxBytes)
    {
        _folder = folder;
        _maxBytes = maxBytes;
        Path = System.IO.Path.Combine(folder, "log.txt");
    }

    public string Path { get; }

    public void Log(string message, Exception? error = null, string? detail = null)
    {
        var sb = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
            .Append("  ")
            .Append(message);
        if (error is not null)
        {
            sb.Append("\n    ").Append(error.GetType().Name).Append(": ").Append(error.Message);
        }

        if (!string.IsNullOrEmpty(detail))
        {
            sb.Append("\n    --- output ---\n").Append(detail.Replace("\n", "\n    ", StringComparison.Ordinal));
        }

        sb.Append('\n');
        var text = sb.ToString();

        _writer.Enqueue(() =>
        {
            try
            {
                Directory.CreateDirectory(_folder);
                var info = new FileInfo(Path);
                if (info.Exists && info.Length > _maxBytes)
                {
                    File.Move(Path, System.IO.Path.Combine(_folder, "log.old.txt"), overwrite: true);
                }

                File.AppendAllText(Path, text, TextFiles.Utf8NoBom);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never break the app.
            }
        });
    }

    /// <summary>Waits for queued lines to be written; false if <paramref name="timeout"/> ran out first.</summary>
    public bool Flush(TimeSpan? timeout = null) => _writer.Flush(timeout);
}
