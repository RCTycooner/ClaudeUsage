using System.ComponentModel;
using System.Globalization;
using ClaudeUsage.Storage;

namespace ClaudeUsage.UI;

/// <summary>
/// View model of the history window: the samples and clock of <see cref="MainViewModel"/> plus a pan/zoom
/// <see cref="ChartViewport"/>. The extent runs from the oldest sample (or a week back, whichever is earlier) to now.
/// The scroll bar works in hours from the start of the extent.
/// </summary>
public sealed class HistoryViewModel : ObservableObject
{
    public static readonly TimeSpan InitialSpan = ChartMath.Range;

    /// <summary>Each zoom step scales the span by this (in) or its inverse (out).</summary>
    public const double ZoomStep = 1.5;

    private readonly MainViewModel _main;
    private readonly ChartViewport _viewport;

    public HistoryViewModel(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);
        _main = main;
        var (start, end) = Extent();
        _viewport = new ChartViewport(start, end, InitialSpan);
        _main.PropertyChanged += OnMainChanged;
    }

    public IReadOnlyList<UsageSample> Samples => _main.Samples;

    /// <summary>The widget's rows double as the legend (colour swatch and the current model name).</summary>
    public IReadOnlyList<BucketRow> Legend => _main.Rows;

    public DateTimeOffset ViewEnd => _viewport.End;

    public TimeSpan ViewSpan => _viewport.Span;

    public bool IsLive => _viewport.IsLive;

    public bool CanZoomIn => _viewport.CanZoomIn;

    public bool CanZoomOut => _viewport.CanZoomOut;

    public double ScrollMaximum => Hours(_viewport.MaxSpan - _viewport.Span);

    public double ScrollViewport => Hours(_viewport.Span);

    public double ScrollLargeChange => Hours(_viewport.Span) * 0.8;

    public double ScrollSmallChange => Hours(_viewport.Span) * 0.1;

    /// <summary>
    /// Read-only so the scroll bar binds one way: a two-way Value would write back values the bar coerced against a
    /// not-yet-updated Maximum. The bar's Scroll event calls <see cref="ScrollTo"/> instead.
    /// </summary>
    public double ScrollValue => Hours(_viewport.Start - _viewport.DataStart);

    /// <summary>"Mon 12 Oct 14:00 – Mon 19 Oct 14:00 · 7 days"; the end reads "now" while live.</summary>
    public string RangeText
    {
        get
        {
            var culture = CultureInfo.CurrentCulture;
            string from = ChartMath.FormatMoment(_viewport.Start, TimeZoneInfo.Local, culture);
            string to = IsLive ? "now" : ChartMath.FormatMoment(_viewport.End, TimeZoneInfo.Local, culture);
            return $"{from} – {to} · {FormatSpan(_viewport.Span)}";
        }
    }

    /// <summary>"45 min", "36 h", "7 days", "3.5 days".</summary>
    public static string FormatSpan(TimeSpan span) =>
        span < TimeSpan.FromHours(1) ? string.Create(CultureInfo.CurrentCulture, $"{span.TotalMinutes:0} min")
        : span < TimeSpan.FromDays(2) ? string.Create(CultureInfo.CurrentCulture, $"{span.TotalHours:0.#} h")
        : string.Create(CultureInfo.CurrentCulture, $"{span.TotalDays:0.#} days");

    /// <summary>Zooms around <paramref name="anchor"/> (0 = left edge, 1 = right edge); <paramref name="steps"/> &gt; 0 zooms in.</summary>
    public void Zoom(double steps, double anchor = 0.5) =>
        Update(() => _viewport.Zoom(Math.Pow(ZoomStep, -steps), anchor));

    /// <summary>Pans by a fraction of the visible span (positive = later).</summary>
    public void Pan(double fraction) => Update(() => _viewport.Pan(_viewport.Span * fraction));

    /// <summary>Scrolls so the view starts <paramref name="hours"/> after the start of the extent.</summary>
    public void ScrollTo(double hours) => Update(() => _viewport.ScrollTo(_viewport.DataStart + TimeSpan.FromHours(hours)));

    public void ShowLast(TimeSpan span) => Update(() => _viewport.ShowLast(span));

    public void ShowAll() => Update(_viewport.ShowAll);

    /// <summary>Jumps to the newest data, keeping the zoom; the view then follows new polls again.</summary>
    public void GoLive() => Update(() => _viewport.ShowLast(_viewport.Span));

    /// <summary>Stops following the widget; call when the window closes so it can be collected.</summary>
    public void Detach() => _main.PropertyChanged -= OnMainChanged;

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Samples))
        {
            Raise(nameof(Samples));
        }

        if (e.PropertyName is nameof(MainViewModel.Samples) or nameof(MainViewModel.Now))
        {
            var (start, end) = Extent();
            Update(() => _viewport.SetExtent(start, end));
        }
    }

    private (DateTimeOffset Start, DateTimeOffset End) Extent()
    {
        var end = _main.Now == default ? DateTimeOffset.Now : _main.Now;
        var start = end - InitialSpan;
        if (_main.Samples is [var first, ..] && first.Taken < start)
        {
            start = first.Taken;
        }

        return (start, end);
    }

    private void Update(Action change)
    {
        change();
        // Everything derives from the viewport; WPF skips bindings whose value did not actually change.
        foreach (var name in (string[])[
            nameof(ViewEnd), nameof(ViewSpan), nameof(IsLive), nameof(CanZoomIn), nameof(CanZoomOut),
            nameof(ScrollMaximum), nameof(ScrollViewport), nameof(ScrollLargeChange), nameof(ScrollSmallChange),
            nameof(ScrollValue), nameof(RangeText)])
        {
            Raise(name);
        }
    }

    private static double Hours(TimeSpan span) => span.TotalHours;
}
