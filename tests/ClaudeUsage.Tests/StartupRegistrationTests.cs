using ClaudeUsage.Storage;
using Microsoft.Win32;

namespace ClaudeUsage.Tests;

public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _keyPath = @"Software\ClaudeUsageTests-" + Guid.NewGuid().ToString("N");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);

    [Fact]
    public void Enable_writes_quoted_exe_path_and_disable_removes_it()
    {
        var registration = new RunKeyStartupRegistration(@"C:\Program Files\X\ClaudeUsage.exe", _keyPath, "ClaudeUsage");

        Assert.False(registration.IsEnabled);

        registration.SetEnabled(true);
        Assert.True(registration.IsEnabled);
        using (var key = Registry.CurrentUser.OpenSubKey(_keyPath))
        {
            Assert.Equal("\"C:\\Program Files\\X\\ClaudeUsage.exe\"", key?.GetValue("ClaudeUsage"));
        }

        registration.SetEnabled(false);
        Assert.False(registration.IsEnabled);
    }

    [Fact]
    public void Disable_when_not_enabled_does_not_throw()
    {
        var registration = new RunKeyStartupRegistration("x.exe", _keyPath, "ClaudeUsage");

        registration.SetEnabled(false);

        Assert.False(registration.IsEnabled);
    }
}
