using WinForms = System.Windows.Forms;
using Wpf = System.Windows;

namespace ClaudeUsage.Tray;

/// <summary>
/// Monitor working areas for window placement. Lives in Tray/ because it uses WinForms' <c>Screen</c> (WinForms is
/// confined to this folder).
/// </summary>
public static class ScreenAreas
{
    /// <summary>
    /// The working area (screen minus taskbar) of every monitor, in WPF device-independent units. Screen reports
    /// physical pixels; they are scaled by the system DPI, which is what WPF uses for window Left/Top.
    /// </summary>
    public static IReadOnlyList<Wpf.Rect> WorkingAreas()
    {
        var primary = WinForms.Screen.PrimaryScreen;
        double scale = primary is { Bounds.Width: > 0 } p ? Wpf.SystemParameters.PrimaryScreenWidth / p.Bounds.Width : 1;

        return [.. WinForms.Screen.AllScreens.Select(s => new Wpf.Rect(
            s.WorkingArea.X * scale,
            s.WorkingArea.Y * scale,
            s.WorkingArea.Width * scale,
            s.WorkingArea.Height * scale))];
    }
}
