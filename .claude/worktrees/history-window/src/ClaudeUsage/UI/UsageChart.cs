using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeUsage.Storage;
using ClaudeUsage.Usage;

namespace ClaudeUsage.UI;

/// <summary>
/// Usage chart: 0–100 % y-axis, the <see cref="Span"/> ending at <see cref="Now"/> on the x-axis, one line per
/// bucket. Lines break across gaps longer than <see cref="ChartMath.MaxGap"/>; the x-axis ticks adapt from 15 minutes
/// to months. With <see cref="ShowCrosshair"/> the sample under the mouse is read out. Drawn in
/// <see cref="OnRender"/>; no library.
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

    public static readonly DependencyProperty SpanProperty = DependencyProperty.Register(
        nameof(Span),
        typeof(TimeSpan),
        typeof(UsageChart),
        new FrameworkPropertyMetadata(ChartMath.Range, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowCrosshairProperty = DependencyProperty.Register(
        nameof(ShowCrosshair),
        typeof(bool),
        typeof(UsageChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double AxisLabelWidth = 22;
    private const double TopPad = 5;
    private const double RightPad = 2;
    private const double XLabelHeight = 14;
    private const double LabelFontSize = 9.5;
    private const double MinDayLabelWidth = 22;
    private const double LongDayLabelWidth = 48;
    private const double MinTickLabelWidth = 56;

    private static readonly Pen s_gridMajor = Palette.FrozenPen(Palette.GridMajor, 1);
    private static readonly Pen s_gridMinor = Palette.FrozenPen(Palette.GridMinor, 1);
    private static readonly Pen s_crosshair = Palette.FrozenPen(Palette.TextMuted, 1);
    private static readonly Pen s_tooltipBorder = Palette.FrozenPen(Palette.PanelBorder, 1);
    private static readonly Pen s_dotOutline = Palette.FrozenPen(Palette.Panel, 1.5);
    private static readonly Typeface s_typeface = new("Segoe UI");

    private static readonly (Func<UsageSample, int?> Value, Pen Pen, Brush Brush)[] s_series =
    [
        (s => s.Session, Palette.FrozenPen(Palette.SessionSeriesLine, 1.1), Palette.SessionSeries),
        (s => s.Week, Palette.FrozenPen(Palette.WeekSeries, 1.6), Palette.WeekSeries),
        (s => s.ModelWeek, Palette.FrozenPen(Palette.ModelSeries, 1.6), Palette.ModelSeries),
    ];

    // The series drawing is the expensive part; it is rebuilt only when the data, window or size change, not when
    // the crosshair moves.
    private (IReadOnlyList<UsageSample>? Samples, DateTimeOffset From, DateTimeOffset To, Rect Plot) _cacheKey;
    private DrawingGroup? _seriesCache;
    private Point? _hover;

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

    /// <summary>Length of the x-axis, ending at <see cref="Now"/>; 7 days by default.</summary>
    public TimeSpan Span
    {
        get => (TimeSpan)GetValue(SpanProperty);
        set => SetValue(SpanProperty, value);
    }

    /// <summary>Shows a vertical line and the values of the sample nearest the mouse.</summary>
    public bool ShowCrosshair
    {
        get => (bool)GetValue(ShowCrosshairProperty);
        set => SetValue(ShowCrosshairProperty, value);
    }

    /// <summary>The plotting area inside the axis labels, in this element's coordinates.</summary>
    public Rect PlotArea => new(
        AxisLabelWidth,
        TopPad,
        Math.Max(0, ActualWidth - AxisLabelWidth - RightPad),
        Math.Max(0, ActualHeight - TopPad - XLabelHeight));

    /// <summary>Crosshair position; set by mouse moves, or directly by the snapshot renderer.</summary>
    internal Point? HoverPoint
    {
        get => _hover;
        set
        {
            _hover = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, 110);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (ShowCrosshair)
        {
            HoverPoint = e.GetPosition(this);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is not null)
        {
            HoverPoint = null;
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        // Transparent fill so the whole area takes mouse input (wheel, drag, crosshair), not just the drawn lines.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var plot = PlotArea;
        if (plot.Width < 10 || plot.Height < 10)
        {
            return;
        }

        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var to = Now == default ? DateTimeOffset.Now : Now;
        var from = to - (Span > TimeSpan.Zero ? Span : ChartMath.Range);

        DrawGrid(drawingContext, plot, dip);
        DrawTimeAxis(drawingContext, plot, from, to, dip);

        var samples = Samples ?? [];
        if (ChartMath.CountPlottable(samples, from, to) < 2)
        {
            var text = Label("collecting data…", dip, 11);
            drawingContext.DrawText(text, new Point(
                plot.Left + ((plot.Width - text.Width) / 2),
                plot.Top + ((plot.Height - text.Height) / 2)));
            return;
        }

        var key = (samples, from, to, plot);
        if (_seriesCache is null || _cacheKey != key)
        {
            _seriesCache = BuildSeries(samples, plot, from, to);
            _cacheKey = key;
        }

        drawingContext.DrawDrawing(_seriesCache);

        if (ShowCrosshair && _hover is { } hover && plot.Contains(hover))
        {
            DrawCrosshair(drawingContext, samples, plot, from, to, hover, dip);
        }
    }

    private static DrawingGroup BuildSeries(IReadOnlyList<UsageSample> samples, Rect plot, DateTimeOffset from, DateTimeOffset to)
    {
        // One slot per pixel keeps the line's shape while dropping points that would land on the same column.
        var slot = TimeSpan.FromTicks((long)((to - from).Ticks / Math.Max(1, plot.Width)));
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(plot.Left - 2, plot.Top - 2, plot.Width + 4, plot.Height + 4)) };
        using (var dc = group.Open())
        {
            foreach (var (value, pen, brush) in s_series)
            {
                var segments = ChartMath.Segment(samples, value, from, to, ChartMath.MaxGap)
                    .Select(s => ChartMath.Decimate(s, from, slot))
                    .ToList();
                DrawSeries(dc, segments, plot, from, to, pen, brush);
            }
        }

        group.Freeze();
        return group;
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

    private static void DrawTimeAxis(DrawingContext dc, Rect plot, DateTimeOffset from, DateTimeOffset to, double dip)
    {
        // Bare weekday names are only unambiguous within a week; beyond that, day ticks need room for the date too.
        double minDayWidth = to - from > ChartMath.Range ? LongDayLabelWidth : MinDayLabelWidth;
        var step = ChartMath.ChooseStep(to - from, plot.Width, minDayWidth, MinTickLabelWidth);
        var culture = CultureInfo.CurrentCulture;
        var ticks = ChartMath.Ticks(from, to, TimeZoneInfo.Local, step, culture.DateTimeFormat.FirstDayOfWeek);
        foreach (var tick in ticks)
        {
            double x = Snap(ChartMath.MapX(tick, from, to, plot.Left, plot.Width));
            // Midnights stand out among hour ticks.
            bool major = step.Unit == TickUnit.Minute && Local(tick).TimeOfDay == TimeSpan.Zero;
            dc.DrawLine(major ? s_gridMajor : s_gridMinor, new Point(x, plot.Top), new Point(x, plot.Bottom + 3));
        }

        if (step == TickStep.OneDay)
        {
            DrawDayLabels(dc, plot, from, to, ticks, dip);
            return;
        }

        // Other steps: a label centred under each tick, kept inside the plot and skipped where it would overlap.
        double lastRight = double.NegativeInfinity;
        foreach (var tick in ticks)
        {
            var local = Local(tick);
            string format = step.Unit switch
            {
                TickUnit.Minute => local.TimeOfDay == TimeSpan.Zero ? "ddd d" : "t",
                TickUnit.Month => local.Month == 1 ? "MMM yyyy" : "MMM",
                _ => "d MMM",
            };
            var text = Label(local.ToString(format, culture), dip);
            double x = ChartMath.MapX(tick, from, to, plot.Left, plot.Width) - (text.Width / 2);
            x = Math.Clamp(x, plot.Left, Math.Max(plot.Left, plot.Right - text.Width));
            if (x < lastRight + 6)
            {
                continue;
            }

            dc.DrawText(text, new Point(x, plot.Bottom + 2));
            lastRight = x + text.Width;
        }
    }

    private static void DrawDayLabels(
        DrawingContext dc,
        Rect plot,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<DateTimeOffset> ticks,
        double dip)
    {
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

            string format = x1 - x0 >= LongDayLabelWidth ? "ddd d" : "ddd";
            var text = Label(Local(edges[i]).ToString(format, CultureInfo.CurrentCulture), dip);
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

    private static void DrawCrosshair(
        DrawingContext dc,
        IReadOnlyList<UsageSample> samples,
        Rect plot,
        DateTimeOffset from,
        DateTimeOffset to,
        Point hover,
        double dip)
    {
        var t = from + ((to - from) * ((hover.X - plot.Left) / plot.Width));
        // Snap to the nearest sample, but not across a gap: a few pixels or one poll gap, whichever is wider.
        var reach = TimeSpan.FromTicks(Math.Max(ChartMath.MaxGap.Ticks, (long)((to - from).Ticks / plot.Width * 6)));
        int i = ChartMath.LowerBound(samples, t);
        UsageSample? nearest = null;
        foreach (int j in (int[])[i - 1, i])
        {
            if (j >= 0 && j < samples.Count && samples[j].Taken >= from && samples[j].Taken <= to
                && (samples[j].Taken - t).Duration() <= reach
                && (nearest is null || (samples[j].Taken - t).Duration() < (nearest.Taken - t).Duration()))
            {
                nearest = samples[j];
            }
        }

        double x = Snap(nearest is null ? hover.X : ChartMath.MapX(nearest.Taken, from, to, plot.Left, plot.Width));
        dc.DrawLine(s_crosshair, new Point(x, plot.Top), new Point(x, plot.Bottom));

        var lines = new List<(FormattedText Text, Brush? Swatch)>
        {
            (Label(ChartMath.FormatMoment(nearest?.Taken ?? t, TimeZoneInfo.Local, CultureInfo.CurrentCulture), dip, 11, Palette.TextPrimary), null),
        };
        if (nearest is null)
        {
            lines.Add((Label("no data", dip, 11), null));
        }
        else
        {
            string[] names = ["Session", "Week", UsageSnapshot.ModelWeekLabelFor(nearest.ModelName)];
            for (int s = 0; s < s_series.Length; s++)
            {
                if (s_series[s].Value(nearest) is not { } v)
                {
                    continue;
                }

                var brush = s_series[s].Brush;
                dc.DrawEllipse(brush, s_dotOutline, new Point(x, ChartMath.MapY(v, plot.Top, plot.Height)), 3.5, 3.5);
                lines.Add((Label(string.Create(CultureInfo.CurrentCulture, $"{names[s]}  {UsageText.DisplayPercent(v)} %"), dip, 11, Palette.TextPrimary), brush));
            }
        }

        const double pad = 7, swatch = 8, gap = 6, lineGap = 2;
        double textWidth = lines.Max(l => l.Text.Width + (l.Swatch is null ? 0 : swatch + gap));
        double boxWidth = textWidth + (2 * pad);
        double boxHeight = lines.Sum(l => l.Text.Height + lineGap) - lineGap + (2 * pad);
        double left = x + 12 + boxWidth <= plot.Right ? x + 12 : x - 12 - boxWidth;
        var box = new Rect(Math.Max(plot.Left, left), plot.Top + 4, boxWidth, boxHeight);
        dc.DrawRoundedRectangle(Palette.Tooltip, s_tooltipBorder, box, 6, 6);

        double y = box.Top + pad;
        foreach (var (text, brush) in lines)
        {
            double tx = box.Left + pad;
            if (brush is not null)
            {
                dc.DrawRoundedRectangle(brush, null, new Rect(tx, y + ((text.Height - swatch) / 2), swatch, swatch), 2, 2);
                tx += swatch + gap;
            }

            dc.DrawText(text, new Point(tx, y));
            y += text.Height + lineGap;
        }
    }

    private static DateTimeOffset Local(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, TimeZoneInfo.Local);

    private static Point ToPoint(ChartPoint p, Rect plot, DateTimeOffset from, DateTimeOffset to) =>
        new(ChartMath.MapX(p.Taken, from, to, plot.Left, plot.Width), ChartMath.MapY(p.Percent, plot.Top, plot.Height));

    private static FormattedText Label(string text, double dip, double size = LabelFontSize, Brush? brush = null) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, s_typeface, size, brush ?? Palette.TextMuted, dip);

    // Centre 1 px lines on a pixel so they stay crisp.
    private static double Snap(double v) => Math.Round(v) + 0.5;
}
