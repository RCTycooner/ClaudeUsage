using System.Diagnostics;
using ClaudeUsage.Usage;

namespace ClaudeUsage.Projects;

/// <summary>Opens a PowerShell window in a project folder and starts <c>claude</c> in it.</summary>
public static class ProjectLauncher
{
    /// <summary>
    /// -NoExit keeps the window (and any error from claude) open after claude exits. Without shell execute, a GUI
    /// parent still gives PowerShell its own console, and the environment can drop the variables of a Claude Code
    /// session the widget was started from; otherwise the new claude thinks it is nested and stops saving transcripts.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string directory)
    {
        var psi = new ProcessStartInfo("powershell.exe", "-NoExit -Command claude")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
        };
        ClaudeCliUsageSource.RemoveSessionVariables(psi.Environment);
        return psi;
    }

    /// <exception cref="System.ComponentModel.Win32Exception">PowerShell could not be started.</exception>
    public static void Open(ClaudeProject project)
    {
        using var _ = Process.Start(CreateStartInfo(project.Directory));
    }
}
