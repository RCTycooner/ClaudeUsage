using System.Windows;

namespace ClaudeUsage.UI;

/// <summary>Pure placement rules for restoring the window position.</summary>
public static class WindowPlacement
{
    /// <summary>Minimum overlap, in each direction, for a saved position to count as reachable.</summary>
    public const double MinVisible = 40;

    /// <summary>
    /// True when <paramref name="window"/> overlaps at least one working area by <paramref name="minVisible"/> in
    /// both directions. Checking each monitor (rather than the virtual-screen bounding box) rejects positions in
    /// the dead corners of L-shaped or mismatched multi-monitor layouts, and on monitors that were unplugged.
    /// </summary>
    public static bool IsReachable(Rect window, IEnumerable<Rect> workingAreas, double minVisible = MinVisible)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        foreach (var area in workingAreas)
        {
            var overlap = Rect.Intersect(window, area);
            if (!overlap.IsEmpty && overlap.Width >= minVisible && overlap.Height >= minVisible)
            {
                return true;
            }
        }

        return false;
    }
}
