using Microsoft.Win32;

namespace ClaudeUsage.Storage;

/// <summary>Whether the app starts with Windows.</summary>
public interface IStartupRegistration
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>
/// Start-with-Windows via a value in an HKCU <c>Run</c> key: <c>"&lt;exe&gt;" --hidden</c>, so the app starts in the
/// tray. Counts as enabled only when the stored command is exactly that (an entry left by a moved or renamed exe
/// reads as disabled, and enabling rewrites it). The key path is injectable for tests.
/// </summary>
public sealed class RunKeyStartupRegistration : IStartupRegistration
{
    public const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "ClaudeUsage";
    public const string HiddenArgument = "--hidden";

    private readonly string _keyPath;
    private readonly string _valueName;

    public RunKeyStartupRegistration(string? exePath = null, string keyPath = DefaultKeyPath, string valueName = DefaultValueName)
    {
        Command = $"\"{exePath ?? Environment.ProcessPath ?? string.Empty}\" {HiddenArgument}";
        _keyPath = keyPath;
        _valueName = valueName;
    }

    /// <summary>The value written to the Run key.</summary>
    public string Command { get; }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
            return key?.GetValue(_valueName) is string value
                && string.Equals(value.Trim(), Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
            key.SetValue(_valueName, Command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
            key?.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }
}

/// <summary>Keeps the Run key in line with the Start-with-Windows choice saved in settings.json.</summary>
public static class StartupSync
{
    /// <summary>
    /// Applies the saved choice to <paramref name="startup"/> and returns the settings to keep. Without a saved
    /// choice (settings from an older version) the current registration is adopted, so an existing entry survives.
    /// Re-applying an enabled choice also rewrites an entry left by a moved or renamed exe.
    /// </summary>
    public static AppSettings Apply(IStartupRegistration startup, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.StartWithWindows is not bool wanted)
        {
            return settings with { StartWithWindows = startup.IsEnabled };
        }

        if (startup.IsEnabled != wanted)
        {
            startup.SetEnabled(wanted);
        }

        return settings;
    }
}
