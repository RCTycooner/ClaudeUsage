using Microsoft.Win32;

namespace ClaudeUsage.Storage;

/// <summary>Whether the app starts with Windows.</summary>
public interface IStartupRegistration
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>Start-with-Windows via a value in an HKCU <c>Run</c> key. The key path is injectable for tests.</summary>
public sealed class RunKeyStartupRegistration : IStartupRegistration
{
    public const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "ClaudeUsage";

    private readonly string _keyPath;
    private readonly string _valueName;
    private readonly string _exePath;

    public RunKeyStartupRegistration(string? exePath = null, string keyPath = DefaultKeyPath, string valueName = DefaultValueName)
    {
        _exePath = exePath ?? Environment.ProcessPath ?? string.Empty;
        _keyPath = keyPath;
        _valueName = valueName;
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
            return key?.GetValue(_valueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
            key.SetValue(_valueName, $"\"{_exePath}\"", RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
            key?.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }
}
