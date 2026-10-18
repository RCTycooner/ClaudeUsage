using System.Windows;
using ClaudeUsage.UI;

namespace ClaudeUsage.Tests;

public sealed class WindowPlacementTests
{
    // L-shaped layout: two monitors side by side on top, a third below the left one. The virtual-screen bounding
    // box covers (1920,1080)-(3840,2160) too, but no monitor does.
    private static readonly Rect[] s_lShape =
    [
        new(0, 0, 1920, 1040),
        new(1920, 0, 1920, 1040),
        new(0, 1080, 1920, 1040),
    ];

    private static Rect Window(double left, double top) => new(left, top, 320, 120);

    [Theory]
    [InlineData(100, 100)] // primary
    [InlineData(2500, 300)] // right monitor
    [InlineData(500, 1500)] // bottom monitor
    [InlineData(1800, 500)] // straddling two monitors
    [InlineData(-280, 100)] // mostly off the left edge, 40 px still visible
    public void Positions_on_a_monitor_are_reachable(double left, double top)
    {
        Assert.True(WindowPlacement.IsReachable(Window(left, top), s_lShape));
    }

    [Theory]
    [InlineData(2500, 1500)] // the empty corner of the L
    [InlineData(-1000, 100)] // left of everything
    [InlineData(100, -500)] // above everything
    [InlineData(-290, 100)] // only 30 px visible
    [InlineData(5000, 5000)] // a monitor that has been unplugged
    public void Positions_off_every_monitor_are_not_reachable(double left, double top)
    {
        Assert.False(WindowPlacement.IsReachable(Window(left, top), s_lShape));
    }

    [Fact]
    public void No_monitors_means_not_reachable()
    {
        Assert.False(WindowPlacement.IsReachable(Window(0, 0), []));
    }
}
