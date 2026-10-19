using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ClaudeUsage.Storage;

namespace ClaudeUsage.UI;

/// <summary>
/// 7-day usage chart: 0–100 % y-axis, last 7 × 24 h ending <see cref="Now"/> on the x-axis, one line per bucket.
/// Lines break across gaps longer than <see cref="ChartMath.MaxGap"/>. Drawn in <see cref="OnRender"/>; no library.
/// </summary>
public sealed class UsageChart : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples),
        typeof(IReadOnlyList<UsageSample>),
        typeof(UsageChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NowProperty = DependencyProperty.Register(
        nameof(Now),
        typeof(DateTimeOffset),
        typeof(UsageChart),
        new FrameworkPropertyMetadata(default(DateTimeOffset), FrameworkPropertyMetadataOptions.AffectsRender));

    private const double AxisLabelWidth = 22;
    private const double TopPad = 5;
    private const double RightPad = 2;
    private const double XLabelHeight = 14;
    private const double LabelFontSize = 9.5;
    private const double MinDayLabelWidth = 22;

    private static readonly Pen s_gridMajor = Palette.FrozenPen(Palette.GridMajor, 1);
    private static readonly Pen s_gridMinor = Palette.FrozenPen(Palette.GridMinor, 1);
    private static readonly Typeface s_typeface = new("Segoe UI");

    private static readonly (Func<UsageSample, int?> Value, Pen Pen, Brush Brush)[] s_series =
    [
        (s => s.Session, Palette.FrozenPen(Palette.SessionSeriesLine, 1.1), Palette.SessionSeries),
        (s => s.Week, Palette.FrozenPen(Palette.WeekSeries, 1.6), Palette.WeekSeries),
        (s => s.ModelWeek, Palette.FrozenPen(Palette.ModelSeries, 1.6), Palette.ModelSeries),
    ];

    public UsageChart()
    {
        SnapsToDevicePixels = true;
    }

    public IReadOnlyList<UsageSample>? Samples
    {
        get => (IReadOnlyList<UsageSample>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    /// <summary>Right edge of the x-axis. Default (unset) means the current time.</summary>
    public DateTimeOffset Now
    {
        get => (DateTimeOffset)GetValue(NowProperty);
        set => SetValue(NowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, 110);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        double width = ActualWidth;
        double height = ActualHeight;
        var plot = new Rect(
            AxisLabelWidth,
            TopPad,
            Math.Max(0, width - AxisLabelWidth - RightPad),
            Math.Max(0, height - TopPad - XLabelHeight));
        if (plot.Width < 10 || plot.Height < 10)
        {
            return;
        }

        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var to = Now == default ? DateTimeOffset.Now : Now;
        var from = to - ChartMath.Range;

        DrawGrid(drawingContext, plot, dip);
        DrawDays(drawingContext, plot, from, to, dip);

        var samples = Samples ?? [];
        if (ChartMath.CountPlottable(samples, from, to) < 2)
        {
            var text = Label("collecting data…", dip, 11);
            drawingContext.DrawText(text, new Point(
                plot.Left + ((plot.Width - text.Width) / 2),
                plot.Top + ((plot.Height - text.Height) / 2)));
            return;
        }

        drawingContext.PushClip(new RectangleGeometry(new Rect(plot.Left - 2, plot.Top - 2, plot.Width + 4, plot.Height + 4)));
        foreach (var (value, pen, brush) in s_series)
        {
            DrawSeries(drawingContext, ChartMath.Segment(samples, value, from, to, ChartMath.MaxGap), plot, from, to, pen, brush);
        }

        drawingContext.Pop();
    }

    private static void DrawGrid(DrawingContext dc, Rect plot, double dip)
    {
        for (int p = 0; p <= 100; p += 25)
        {
            double y = Snap(ChartMath.MapY(p, plot.Top, plot.Height));
            dc.DrawLine(p % 50 == 0 ? s_gridMajor : s_gridMinor, new Point(plot.Left, y), new Point(plot.Right, y));
            if (p % 50 == 0)
            {
                var text = Label(p.ToString(CultureInfo.InvariantCulture), dip);
                dc.DrawText(text, new Point(plot.Left - 4 - text.Width, y - (text.Height / 2)));
            }
        }
    }

    private static void DrawDays(DrawingContext dc, Rect plot, DateTimeOffset from, DateTimeOffset to, double dip)
    {
        var ticks = ChartMath.DayTicks(from, to, TimeZoneInfo.Local);
        foreach (var tick in ticks)
        {
            double x = Snap(ChartMath.MapX(tick, from, to, plot.Left, plot.Width));
            dc.DrawLine(s_gridMinor, new Point(x, plot.Top), new Point(x, plot.Bottom + 3));
        }

        // Weekday names centred in each (possibly partial) day; skipped where the day is too narrow.
        var edges = new List<DateTimeOffset>(ticks.Count + 2) { from };
        edges.AddRange(ticks);
        edges.Add(to);
        for (int i = 0; i + 1 < edges.Count; i++)
        {
            double x0 = ChartMath.MapX(edges[i], from, to, plot.Left, plot.Width);
            double x1 = ChartMath.MapX(edges[i + 1], from, to, plot.Left, plot.Width);
            if (x1 - x0 < MinDayLabelWidth)
            {
                continue;
            }

            var day = TimeZoneInfo.ConvertTime(edges[i], TimeZoneInfo.Local);
            var text = Label(day.ToString("ddd", CultureInfo.CurrentCulture), dip);
            dc.DrawText(text, new Point(((x0 + x1) / 2) - (text.Width / 2), plot.Bottom + 2));
        }
    }

    private static void DrawSeries(
        DrawingContext dc,
        IReadOnlyList<IReadOnlyList<ChartPoint>> segments,
        Rect plot,
        DateTimeOffset from,
        DateTimeOffset to,
        Pen pen,
        Brush brush)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            foreach (var segment in segments)
            {
                if (segment.Count < 2)
                {
                    continue;
                }

                ctx.BeginFigure(ToPoint(segment[0], plot, from, to), isFilled: false, isClosed: false);
                for (int i = 1; i < segment.Count; i++)
                {
                    ctx.LineTo(ToPoint(segment[i], plot, from, to), isStroked: true, isSmoothJoin: false);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        // Isolated readings (a lone poll between gaps) would be invisible as lines; draw them as dots.
        foreach (var segment in segments)
        {
            if (segment.Count == 1)
            {
                dc.DrawEllipse(brush, null, ToPoint(segment[0], plot, from, to), 1.5, 1.5);
            }
        }
    }

    private static Point ToPoint(ChartPoint p, Rect plot, DateTimeOffset from, DateTimeOffset to) =>
        new(ChartMath.MapX(p.Taken, from, to, plot.Left, plot.Width), ChartMath.MapY(p.Percent, plot.Top, plot.Height));

    private static FormattedText Label(string text, double dip, double size = LabelFontSize) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, s_typeface, size, Palette.TextMuted, dip);

    // Centre 1 px lines on a pixel so they stay crisp.
    private static double Snap(double v) => Math.Round(v) + 0.5;
}
