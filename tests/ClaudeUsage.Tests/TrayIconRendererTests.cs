using ClaudeUsage.Tray;
using Drawing = System.Drawing;

namespace ClaudeUsage.Tests;

public sealed class TrayIconRendererTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void Render_produces_bitmap_of_requested_size(int px)
    {
        var size = new Drawing.Size(px, px);
        using var bitmap = TrayIconRenderer.Render(size);

        Assert.Equal(size, bitmap.Size);
    }

    [Fact]
    public void Render_draws_a_green_square_with_a_white_letter()
    {
        var size = new Drawing.Size(32, 32);
        using var bitmap = TrayIconRenderer.Render(size);

        // Inside the square, left of the letter: the green background.
        var background = bitmap.GetPixel(2, 16);
        Assert.Equal(Drawing.Color.FromArgb(0x16, 0xA3, 0x4A).ToArgb(), background.ToArgb());

        // Somewhere in the bitmap there are white letter pixels.
        bool hasWhite = false;
        for (int x = 0; x < size.Width && !hasWhite; x++)
        {
            for (int y = 0; y < size.Height && !hasWhite; y++)
            {
                var p = bitmap.GetPixel(x, y);
                hasWhite = p.A == 255 && p.R > 240 && p.G > 240 && p.B > 240;
            }
        }

        Assert.True(hasWhite, "expected white letter pixels");
    }
}
