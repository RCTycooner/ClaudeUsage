using ClaudeUsage.Tray;
using Drawing = System.Drawing;

namespace ClaudeUsage.Tests;

public sealed class TrayIconRendererTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(69, false)]
    [InlineData(85, false)]
    [InlineData(100, false)]
    [InlineData(250, false)]
    [InlineData(-5, false)]
    [InlineData(null, false)]
    [InlineData(42, true)]
    public void Render_produces_bitmap_of_requested_size(int? percent, bool hasError)
    {
        foreach (var size in new[] { new Drawing.Size(16, 16), new Drawing.Size(32, 32) })
        {
            using var bitmap = TrayIconRenderer.Render(percent, hasError, size);

            Assert.Equal(size, bitmap.Size);
        }
    }

    [Fact]
    public void Render_colours_by_threshold()
    {
        var size = new Drawing.Size(32, 32);
        using var low = TrayIconRenderer.Render(10, false, size);
        using var high = TrayIconRenderer.Render(95, false, size);

        // A pixel inside the rounded square, away from the text.
        Assert.NotEqual(low.GetPixel(3, 16), high.GetPixel(3, 16));
    }

    [Fact]
    public void Failed_poll_keeps_the_colour_and_adds_a_corner_dot()
    {
        var size = new Drawing.Size(32, 32);
        using var ok = TrayIconRenderer.Render(10, false, size);
        using var stale = TrayIconRenderer.Render(10, true, size);

        // Same background (the last known level) ...
        Assert.Equal(ok.GetPixel(3, 16), stale.GetPixel(3, 16));
        // ... but the top-right corner carries the red stale marker.
        var corner = stale.GetPixel(size.Width - 7, 6);
        Assert.NotEqual(ok.GetPixel(size.Width - 7, 6), corner);
        Assert.True(corner.R > 200 && corner.G < 100 && corner.B < 100, $"expected red, got {corner}");
    }

    [Fact]
    public void Failed_poll_without_data_is_neutral()
    {
        var size = new Drawing.Size(32, 32);
        using var unknown = TrayIconRenderer.Render(null, false, size);
        using var error = TrayIconRenderer.Render(null, true, size);

        Assert.Equal(unknown.GetPixel(3, 16), error.GetPixel(3, 16));
    }
}
