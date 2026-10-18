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
}
