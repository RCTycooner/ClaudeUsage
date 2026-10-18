using System.Windows.Media;

namespace ClaudeUsage.UI;

/// <summary>How close a bucket is to its limit; drives the bar colour.</summary>
public enum UsageLevel
{
    Ok,
    Warning,
    Critical,
}

/// <summary>
/// Colours shared by the rows and the chart. All brushes and pens are frozen. Series colours (sky, violet, pink)
/// are kept clear of the green/amber/red threshold colours so a line is never mistaken for a warning.
/// </summary>
public static class Palette
{
    public const int WarningThreshold = 70;
    public const int CriticalThreshold = 90;

    public static readonly Brush SessionSeries = Frozen(0x38, 0xBD, 0xF8);

    /// <summary>Session line, slightly translucent: its 5-hour sawtooth is dense and should not bury the week lines.</summary>
    public static readonly Brush SessionSeriesLine = Frozen(0x38, 0xBD, 0xF8, 0xB0);

    public static readonly Brush WeekSeries = Frozen(0xA7, 0x8B, 0xFA);
    public static readonly Brush FableSeries = Frozen(0xF4, 0x72, 0xB6);

    public static readonly Brush Ok = Frozen(0x4A, 0xDE, 0x80);
    public static readonly Brush Warning = Frozen(0xFB, 0xBF, 0x24);
    public static readonly Brush Critical = Frozen(0xF8, 0x71, 0x71);

    public static readonly Brush TextMuted = Frozen(0x9A, 0x9A, 0xA5);
    public static readonly Brush GridMajor = Frozen(0x4A, 0x4A, 0x55);
    public static readonly Brush GridMinor = Frozen(0x2E, 0x2E, 0x36);

    /// <summary>Green below 70 %, amber below 90 %, red from 90 %. Values outside 0–100 are clamped first.</summary>
    public static UsageLevel LevelFor(int percent)
    {
        int p = Math.Clamp(percent, 0, 100);
        return p >= CriticalThreshold ? UsageLevel.Critical
            : p >= WarningThreshold ? UsageLevel.Warning
            : UsageLevel.Ok;
    }

    public static Brush BrushFor(UsageLevel level) => level switch
    {
        UsageLevel.Critical => Critical,
        UsageLevel.Warning => Warning,
        _ => Ok,
    };

    public static Brush BrushForPercent(int percent) => BrushFor(LevelFor(percent));

    public static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
