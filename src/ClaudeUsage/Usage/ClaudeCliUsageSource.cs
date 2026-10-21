using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeUsage.Usage;

/// <summary>
/// Runs <c>claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config</c> and returns the envelope's
/// <c>result</c> text. <c>/usage</c> is a local command: no model call, no tokens.
/// </summary>
public sealed class ClaudeCliUsageSource : IUsageSource
{
    public const string ExeName = "claude.exe";
    public const string CmdName = "claude.cmd";

    private const int MaxDetailChars = 500;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <c>--strict-mcp-config</c> without <c>--mcp-config</c> starts no MCP servers: /usage needs none, and otherwise
    /// every poll would spawn the user's configured servers (stdio processes, claude.ai connectors). Not
    /// <c>--bare</c>: that disables OAuth, so /usage would report nothing.
    /// </summary>
    private static readonly string[] s_arguments =
        ["-p", "/usage", "--output-format", "json", "--no-session-persistence", "--strict-mcp-config"];

    /// <summary>Variables that tie a process to a running Claude Code session; the child must not inherit them.</summary>
    private static readonly string[] s_sessionVariables = ["CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_PID", "CLAUDE_JOB_DIR"];

    /// <summary>Fragments that mark a <c>CLAUDE_CODE_*</c> variable as session-specific rather than configuration.</summary>
    private static readonly string[] s_sessionFragments = ["SESSION", "MESSAGING", "SSE_PORT", "AGENT", "CHILD", "ENTRYPOINT"];

    private readonly string _workingDirectory;
    private volatile string? _claudePathOverride;

    /// <param name="workingDirectory">Folder to run in (the app's data folder, so no project config is picked up).</param>
    /// <param name="claudePathOverride">Explicit path to claude.exe (or claude.cmd) from settings, or null.</param>
    public ClaudeCliUsageSource(string workingDirectory, string? claudePathOverride)
    {
        _workingDirectory = workingDirectory;
        _claudePathOverride = claudePathOverride;
    }

    /// <summary>The settings override; a change applies from the next poll.</summary>
    public string? ClaudePathOverride
    {
        get => _claudePathOverride;
        set => _claudePathOverride = value;
    }

    public async Task<string> FetchRawAsync(CancellationToken cancellationToken)
    {
        var exe = ResolveExe(_claudePathOverride)
            ?? throw new UsageSourceException(
                $"{ExeName} not found (checked settings 'claudePath', PATH, %USERPROFILE%\\.local\\bin and %APPDATA%\\npm).");

        Directory.CreateDirectory(_workingDirectory);
        var psi = CreateStartInfo(exe, _workingDirectory);
        SanitizeEnvironment(psi.Environment);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new UsageSourceException($"Could not start {exe}: {ex.Message}", ex);
        }

        LowerPriority(process);

        // Process.Dispose does not close redirected streams the caller has touched; dispose them explicitly.
        using var stdoutReader = process.StandardOutput;
        using var stderrReader = process.StandardError;
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(s_timeout);
        var stdoutTask = stdoutReader.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = stderrReader.ReadToEndAsync(timeoutCts.Token);

        string stdout, stderr;
        bool exited = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            exited = true;
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new UsageSourceException(exited
                ? $"{ExeName} exited, but its output did not finish within {s_timeout.TotalSeconds:0} s (a child process may still hold the pipe)."
                : $"{ExeName} did not finish within {s_timeout.TotalSeconds:0} s and was killed.");
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
    /// Start info for <paramref name="exe"/> with the /usage arguments. A <c>.cmd</c>/<c>.bat</c> shim (npm install)
    /// cannot be started directly, so it runs through <c>cmd.exe /d /s /c "&lt;quoted command line&gt;"</c>.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string exe, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(exe);
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory,
        };

        if (IsBatchFile(exe))
        {
            // /s: strip only the outer quotes, keep the rest verbatim; /d: skip AutoRun commands.
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comSpec ? comSpec : "cmd.exe";
            var commandLine = string.Join(' ', new[] { exe }.Concat(s_arguments).Select(QuoteForCmd));
            psi.Arguments = $"/d /s /c \"{commandLine}\"";
        }
        else
        {
            psi.FileName = exe;
            foreach (var arg in s_arguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        return psi;
    }

    /// <summary>
    /// <see cref="RemoveSessionVariables"/>, plus disabling the auto-updater for these short-lived runs.
    /// </summary>
    public static void SanitizeEnvironment(IDictionary<string, string?> environment)
    {
        RemoveSessionVariables(environment);
        environment["DISABLE_AUTOUPDATER"] = "1";
    }

    /// <summary>
    /// Removes variables a parent Claude Code session sets (CLAUDECODE, CLAUDE_CODE_ENTRYPOINT, session ids, IPC
    /// sockets, CLAUDE_CODE_CHILD_SESSION), so a widget started from a Claude Code terminal does not make the child
    /// think it is nested.
    /// </summary>
    public static void RemoveSessionVariables(IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        foreach (var name in environment.Keys.ToList())
        {
            bool session = s_sessionVariables.Contains(name, StringComparer.OrdinalIgnoreCase)
                || (name.StartsWith("CLAUDE_CODE_", StringComparison.OrdinalIgnoreCase)
                    && s_sessionFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
            if (session)
            {
                environment.Remove(name);
            }
        }
    }

    /// <summary>
    /// Extracts <c>result</c> from the CLI's JSON envelope. If the whole output is not JSON (e.g. a banner or warning
    /// line before it), falls back to the last non-empty line that is a JSON object with <c>type == "result"</c> or a
    /// <c>result</c> property. Throws <see cref="UsageSourceException"/> (with the first 500 chars of output) when no
    /// envelope is found, <c>is_error</c> is true, or <c>result</c> is missing.
    /// </summary>
    public static string ParseEnvelope(string stdout)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        var snippet = Truncate(stdout.Trim());

        using var doc = TryParseObject(stdout) ?? FindEnvelopeLine(stdout)
            ?? throw new UsageSourceException($"{ExeName} output is not JSON: {snippet}") { RawOutput = snippet };
        var root = doc.RootElement;

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

    /// <summary>
    /// Settings override first, then claude.exe on PATH, then %USERPROFILE%\.local\bin\claude.exe, then an npm
    /// <c>claude.cmd</c> shim on PATH or in %APPDATA%\npm. Null if none exists.
    /// </summary>
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

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim('"'))
            .ToList();

        var exeDirs = pathDirs.Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
        var cmdDirs = pathDirs.Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));

        return FindIn(exeDirs, ExeName) ?? FindIn(cmdDirs, CmdName);

        static string? FindIn(IEnumerable<string> dirs, string fileName)
        {
            foreach (var dir in dirs)
            {
                try
                {
                    var candidate = Path.Combine(dir, fileName);
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

            return null;
        }
    }

    private static bool IsBatchFile(string path) =>
        path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    /// <summary>Quotes an argument for cmd.exe when it contains spaces or cmd metacharacters.</summary>
    private static string QuoteForCmd(string arg) =>
        arg.Length > 0 && arg.IndexOfAny([' ', '\t', '&', '|', '<', '>', '^', '(', ')', '%', '!', ',', ';', '=']) < 0
            ? arg
            : "\"" + arg.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static JsonDocument? TryParseObject(string text)
    {
        try
        {
            var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return doc;
            }

            doc.Dispose();
        }
        catch (JsonException)
        {
            // Not (only) JSON.
        }

        return null;
    }

    private static JsonDocument? FindEnvelopeLine(string stdout)
    {
        var lines = stdout.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith('{') || TryParseObject(line) is not { } doc)
            {
                continue;
            }

            var root = doc.RootElement;
            bool isResult = (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                    && type.GetString() == "result")
                || root.TryGetProperty("result", out _);
            if (isResult)
            {
                return doc;
            }

            doc.Dispose();
        }

        return null;
    }

    private static string Truncate(string text) => text.Length > MaxDetailChars ? text[..MaxDetailChars] + "..." : text;

    /// <summary>
    /// Runs the poll at BelowNormal so it does not compete with the foreground (it spawns git, rg and MCP servers,
    /// which inherit the class). Only this child is lowered: the widget itself, and anything it launches directly
    /// (such as <see cref="Projects.ProjectLauncher"/>), stays at Normal.
    /// </summary>
    private static void LowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already exited (or not accessible); the poll still works at its default priority.
        }
    }

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
