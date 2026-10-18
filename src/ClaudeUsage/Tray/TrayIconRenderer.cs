using System.Globalization;
using ClaudeUsage.UI;
using Drawing = System.Drawing;

namespace ClaudeUsage.Tray;

/// <summary>Draws the tray icon: the highest usage percentage as bold text on a rounded square coloured by threshold.</summary>
public static class TrayIconRenderer
{
    private static readonly Drawing.Color s_ok = Drawing.Color.FromArgb(0x16, 0xA3, 0x4A);
    private static readonly Drawing.Color s_warning = Drawing.Color.FromArgb(0xF5, 0x9E, 0x0B);
    private static readonly Drawing.Color s_critical = Drawing.Color.FromArgb(0xDC, 0x26, 0x26);
    private static readonly Drawing.Color s_neutral = Drawing.Color.FromArgb(0x6B, 0x72, 0x80);

    private static readonly Drawing.Color s_errorDot = Drawing.Color.FromArgb(0xEF, 0x44, 0x44);

    /// <summary>
    /// Renders the icon. <paramref name="percent"/> null means no data: "?" or, with <paramref name="hasError"/>, "!".
    /// With a percentage and <paramref name="hasError"/> the last known number stays and a red dot in the top-right
    /// corner marks it as stale. The percentage is clamped to 0-100.
    /// </summary>
    public static Drawing.Bitmap Render(int? percent, bool hasError, Drawing.Size size)
    {
        string text;
        Drawing.Color background;
        if (percent is null)
        {
            text = hasError ? "!" : "?";
            background = s_neutral;
        }
        else
        {
            int p = Math.Clamp(percent.Value, 0, 100);
            text = p.ToString(CultureInfo.InvariantCulture);
            background = Palette.LevelFor(p) switch
            {
                UsageLevel.Critical => s_critical,
                UsageLevel.Warning => s_warning,
                _ => s_ok,
            };
        }

        var bitmap = new Drawing.Bitmap(size.Width, size.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Drawing.Color.Transparent);

        float w = size.Width;
        float h = size.Height;
        float radius = Math.Max(2f, w * 0.22f);
        using (var path = RoundedRect(0, 0, w, h, radius))
        using (var fill = new Drawing.SolidBrush(background))
        {
            g.FillPath(fill, path);
        }

        // Three digits ("100") need a smaller font than one or two.
        float fontPx = text.Length >= 3 ? h * 0.52f : text.Length == 2 ? h * 0.70f : h * 0.82f;
        using var font = new Drawing.Font("Segoe UI", fontPx, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        using var format = new Drawing.StringFormat(Drawing.StringFormat.GenericTypographic)
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center,
        };
        var foreground = background == s_warning ? Drawing.Color.Black : Drawing.Color.White;
        using var textBrush = new Drawing.SolidBrush(foreground);
        g.DrawString(text, font, textBrush, new Drawing.RectangleF(0, 0, w, h), format);

        if (hasError && percent is not null)
        {
            // Stale marker: red dot with a white ring so it also shows on the red (critical) background.
            float d = Math.Max(5f, w * 0.42f);
            float ring = Math.Max(1f, w / 16f);
            using var ringBrush = new Drawing.SolidBrush(Drawing.Color.White);
            using var dotBrush = new Drawing.SolidBrush(s_errorDot);
            g.FillEllipse(ringBrush, w - d, 0, d, d);
            g.FillEllipse(dotBrush, w - d + ring, ring, d - (2 * ring), d - (2 * ring));
        }

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
