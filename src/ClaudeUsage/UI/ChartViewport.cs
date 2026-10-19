namespace ClaudeUsage.UI;

/// <summary>
/// The visible time window of the history chart inside the data extent [<see cref="DataStart"/>,
/// <see cref="DataEnd"/>]. Zooming and panning are clamped so the window never leaves the extent. While the window
/// touches the right end it is "live" and keeps following <see cref="DataEnd"/> as new data arrives.
/// </summary>
public sealed class ChartViewport
{
    public static readonly TimeSpan MinSpan = TimeSpan.FromHours(1);

    // A window ending within this of the data end counts as live, so rounding never knocks it off.
    private static readonly TimeSpan s_liveTolerance = TimeSpan.FromSeconds(1);

    public ChartViewport(DateTimeOffset dataStart, DateTimeOffset dataEnd, TimeSpan span)
    {
        DataStart = dataStart;
        DataEnd = dataEnd;
        Span = span;
        Start = dataEnd - span;
        Clamp();
    }

    public DateTimeOffset DataStart { get; private set; }

    public DateTimeOffset DataEnd { get; private set; }

    public DateTimeOffset Start { get; private set; }

    public TimeSpan Span { get; private set; }

    public DateTimeOffset End => Start + Span;

    /// <summary>The whole extent, but never less than <see cref="MinSpan"/>.</summary>
    public TimeSpan MaxSpan => DataEnd - DataStart > MinSpan ? DataEnd - DataStart : MinSpan;

    public bool IsLive => End >= DataEnd - s_liveTolerance;

    public bool CanZoomIn => Span > MinSpan;

    public bool CanZoomOut => Span < MaxSpan;

    /// <summary>Moves the extent (new data, or time passing). A live window slides along; any other stays put.</summary>
    public void SetExtent(DateTimeOffset dataStart, DateTimeOffset dataEnd)
    {
        bool live = IsLive;
        DataStart = dataStart;
        DataEnd = dataEnd;
        if (live)
        {
            Start = DataEnd - Span;
        }

        Clamp();
    }

    /// <summary>
    /// Scales the span by <paramref name="factor"/> (below 1 zooms in) keeping the time under
    /// <paramref name="anchor"/> (0 = left edge, 1 = right edge) in place. A live window zoomed at its right edge stays live.
    /// </summary>
    public void Zoom(double factor, double anchor = 0.5)
    {
        if (factor <= 0 || double.IsNaN(factor))
        {
            return;
        }

        anchor = Math.Clamp(anchor, 0, 1);
        var pinned = Start + (Span * anchor);
        Span = Span * factor;
        ClampSpan();
        Start = pinned - (Span * anchor);
        Clamp();
    }

    /// <summary>Shifts the window by <paramref name="delta"/> (positive = later).</summary>
    public void Pan(TimeSpan delta)
    {
        Start += delta;
        Clamp();
    }

    public void ScrollTo(DateTimeOffset start)
    {
        Start = start;
        Clamp();
    }

    /// <summary>Shows the last <paramref name="span"/> up to the data end (live).</summary>
    public void ShowLast(TimeSpan span)
    {
        Span = span;
        ClampSpan();
        Start = DataEnd - Span;
        Clamp();
    }

    public void ShowAll() => ShowLast(MaxSpan);

    private void ClampSpan()
    {
        Span = Span < MinSpan ? MinSpan : Span > MaxSpan ? MaxSpan : Span;
    }

    private void Clamp()
    {
        ClampSpan();
        var latest = DataEnd - Span;
        var earliest = DataStart < latest ? DataStart : latest;
        Start = Start < earliest ? earliest : Start > latest ? latest : Start;
    }
}
