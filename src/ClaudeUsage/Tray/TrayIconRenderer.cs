using Drawing = System.Drawing;

namespace ClaudeUsage.Tray;

/// <summary>Draws the static tray icon: a bold white "C" on a green rounded square.</summary>
public static class TrayIconRenderer
{
    private static readonly Drawing.Color s_background = Drawing.Color.FromArgb(0x16, 0xA3, 0x4A);

    /// <summary>Renders the icon at <paramref name="size"/> (the tray's small icon size, which scales with DPI).</summary>
    public static Drawing.Bitmap Render(Drawing.Size size)
    {
        var bitmap = new Drawing.Bitmap(size.Width, size.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Drawing.Color.Transparent);

        float w = size.Width;
        float h = size.Height;
        float radius = Math.Max(2f, w * 0.22f);
        using (var path = RoundedRect(0, 0, w, h, radius))
        using (var fill = new Drawing.SolidBrush(s_background))
        {
            g.FillPath(fill, path);
        }

        using var font = new Drawing.Font("Segoe UI", h * 0.82f, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        using var format = new Drawing.StringFormat(Drawing.StringFormat.GenericTypographic)
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center,
        };
        using var textBrush = new Drawing.SolidBrush(Drawing.Color.White);
        g.DrawString("C", font, textBrush, new Drawing.RectangleF(0, 0, w, h), format);

        return bitmap;
    }

    private static Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        float d = r * 2;
        var path = new Drawing.Drawing2D.GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
