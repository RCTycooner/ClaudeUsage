using ClaudeUsage.Storage;
using Microsoft.Win32;

namespace ClaudeUsage.Tests;

public sealed class StartupRegistrationTests : IDisposable
{
    private const string Exe = @"C:\Program Files\X\ClaudeUsage.exe";

    private readonly string _keyPath = @"Software\ClaudeUsageTests-" + Guid.NewGuid().ToString("N");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);

    private string? StoredValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue("ClaudeUsage") as string;
    }

    private void Store(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
        key.SetValue("ClaudeUsage", value);
    }

    [Fact]
    public void Enable_writes_quoted_exe_path_with_hidden_switch_and_disable_removes_it()
    {
        var registration = new RunKeyStartupRegistration(Exe, _keyPath, "ClaudeUsage");

        Assert.False(registration.IsEnabled);

        registration.SetEnabled(true);
        Assert.True(registration.IsEnabled);
        Assert.Equal("\"C:\\Program Files\\X\\ClaudeUsage.exe\" --hidden", StoredValue());

        registration.SetEnabled(false);
        Assert.False(registration.IsEnabled);
        Assert.Null(StoredValue());
    }

    [Theory]
    [InlineData(@"""C:\Old\ClaudeUsage.exe"" --hidden")]
    [InlineData(@"""C:\Program Files\X\ClaudeUsage.exe""")]
    [InlineData(@"C:\Program Files\X\ClaudeUsage.exe --hidden")]
    public void Stale_or_different_value_is_not_enabled_and_enabling_rewrites_it(string stale)
    {
        Store(stale);
        var registration = new RunKeyStartupRegistration(Exe, _keyPath, "ClaudeUsage");

        Assert.False(registration.IsEnabled);

        registration.SetEnabled(true);

        Assert.True(registration.IsEnabled);
        Assert.Equal(registration.Command, StoredValue());
    }

    [Fact]
    public void Disable_when_not_enabled_does_not_throw()
    {
        var registration = new RunKeyStartupRegistration("x.exe", _keyPath, "ClaudeUsage");

        registration.SetEnabled(false);

        Assert.False(registration.IsEnabled);
    }
}
