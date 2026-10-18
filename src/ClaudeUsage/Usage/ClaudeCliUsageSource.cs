using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeUsage.Usage;

/// <summary>
/// Runs <c>claude.exe -p "/usage" --output-format json --no-session-persistence</c> and returns the
/// envelope's <c>result</c> text. <c>/usage</c> is a local command: no model call, no tokens.
/// </summary>
public sealed class ClaudeCliUsageSource : IUsageSource
{
    public const string ExeName = "claude.exe";

    private const int MaxDetailChars = 500;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    private readonly string _workingDirectory;
    private readonly string? _claudePathOverride;

    /// <param name="workingDirectory">Folder to run in (the app's data folder, so no project config is picked up).</param>
    /// <param name="claudePathOverride">Explicit path to claude.exe from settings, or null.</param>
    public ClaudeCliUsageSource(string workingDirectory, string? claudePathOverride)
    {
        _workingDirectory = workingDirectory;
        _claudePathOverride = claudePathOverride;
    }

    public async Task<string> FetchRawAsync(CancellationToken cancellationToken)
    {
        var exe = ResolveExe(_claudePathOverride)
            ?? throw new UsageSourceException(
                $"{ExeName} not found (checked settings 'claudePath', PATH and %USERPROFILE%\\.local\\bin).");

        Directory.CreateDirectory(_workingDirectory);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = _workingDirectory,
        };
        foreach (var arg in new[] { "-p", "/usage", "--output-format", "json", "--no-session-persistence" })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new UsageSourceException($"Could not start {exe}: {ex.Message}", ex);
        }

        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(s_timeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        string stdout, stderr;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new UsageSourceException($"{ExeName} did not finish within {s_timeout.TotalSeconds:0} s and was killed.");
        }

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
        {
            throw new UsageSourceException(
                $"{ExeName} exited with code {process.ExitCode}: {Truncate(stderr.Trim())}")
            { RawOutput = Truncate(stderr) };
        }

        return ParseEnvelope(stdout);
    }

    /// <summary>
    /// Extracts <c>result</c> from the CLI's JSON envelope. Throws <see cref="UsageSourceException"/>
    /// (with the first 500 chars of output) when it is not JSON, <c>is_error</c> is true, or <c>result</c> is missing.
    /// </summary>
    public static string ParseEnvelope(string stdout)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        var snippet = Truncate(stdout.Trim());
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new UsageSourceException($"Unexpected output from {ExeName}: {snippet}") { RawOutput = snippet };
            }

            if (root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True)
            {
                throw new UsageSourceException($"{ExeName} reported an error: {snippet}") { RawOutput = snippet };
            }

            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.String)
            {
                throw new UsageSourceException($"{ExeName} output has no 'result' text: {snippet}") { RawOutput = snippet };
            }

            return result.GetString()!;
        }
        catch (JsonException ex)
        {
            throw new UsageSourceException($"{ExeName} output is not JSON: {snippet}", ex) { RawOutput = snippet };
        }
    }

    /// <summary>Settings override first, then PATH, then %USERPROFILE%\.local\bin\claude.exe. Null if none exists.</summary>
    public static string? ResolveExe(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(overridePath.Trim().Trim('"'));
            if (File.Exists(expanded))
            {
                return expanded;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), ExeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry; skip it.
            }
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", ExeName);
        return File.Exists(fallback) ? fallback : null;
    }

    private static string Truncate(string text) => text.Length > MaxDetailChars ? text[..MaxDetailChars] + "..." : text;

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Already gone or not killable; nothing more to do.
        }
    }
}
