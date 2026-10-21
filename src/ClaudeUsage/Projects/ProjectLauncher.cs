using System.Diagnostics;

namespace ClaudeUsage.Projects;

/// <summary>Opens a PowerShell window in a project folder and starts <c>claude</c> in it.</summary>
public static class ProjectLauncher
{
    /// <summary>-NoExit keeps the window (and any error from claude) open after claude exits.</summary>
    public static ProcessStartInfo CreateStartInfo(string directory) => new("powershell.exe", "-NoExit -Command claude")
    {
        WorkingDirectory = directory,
        UseShellExecute = true,
    };

    /// <exception cref="System.ComponentModel.Win32Exception">PowerShell could not be started.</exception>
    public static void Open(ClaudeProject project)
    {
        using var _ = Process.Start(CreateStartInfo(project.Directory));
    }
}
