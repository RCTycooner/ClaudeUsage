using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsage.Storage;
using ClaudeUsage.Usage;

namespace ClaudeUsage.UI;

/// <summary>Minimal INotifyPropertyChanged base.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    /// <summary>Notifies a change of a computed property.</summary>
    protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One stats row: label with its chart colour, bar, "NN %", "resets in …" and where the bucket is heading.</summary>
public sealed class BucketRow(string label, Brush swatch) : ObservableObject
{
    public const string Missing = "—";

    private string _label = label;
    private string _percentText = Missing;
    private double _barValue;
    private Brush _barBrush = Palette.Ok;
    private string _countdown = "";
    private string _forecast = "";
    private Brush _forecastBrush = Palette.TextMuted;

    /// <summary>Row label; the per-model row follows the model name in the latest snapshot ("Fable week").</summary>
    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>Series colour; the swatch makes the rows double as the chart legend.</summary>
    public Brush Swatch { get; } = swatch;

    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }

    /// <summary>0–100, clamped for display.</summary>
    public double BarValue { get => _barValue; private set => Set(ref _barValue, value); }

    public Brush BarBrush { get => _barBrush; private set => Set(ref _barBrush, value); }

    public string Countdown { get => _countdown; private set => Set(ref _countdown, value); }

    /// <summary>
    /// " · 100 % in 3h 10m", or " · ~85 % at reset" when the reset comes first (separator only after a countdown);
    /// empty without an estimate.
    /// </summary>
    public string Forecast { get => _forecast; private set => Set(ref _forecast, value); }

    /// <summary>Red when 100 % would be hit before the reset, muted otherwise.</summary>
    public Brush ForecastBrush { get => _forecastBrush; private set => Set(ref _forecastBrush, value); }

    /// <param name="estimate">Where the bucket is heading (see <see cref="LimitForecast"/>), if known.</param>
    public void Update(UsageBucket? bucket, DateTimeOffset now, LimitEstimate? estimate = null)
    {
        if (bucket is null)
        {
            PercentText = Missing;
            BarValue = 0;
            BarBrush = Palette.Ok;
            Countdown = "";
            Forecast = "";
            ForecastBrush = Palette.TextMuted;
            return;
        }

        int shown = UsageText.DisplayPercent(bucket.Percent);
        PercentText = string.Create(CultureInfo.InvariantCulture, $"{shown} %");
        BarValue = shown;
        BarBrush = Palette.BrushForPercent(bucket.Percent);
        Countdown = CountdownFormatter.Format(bucket.ResetsAt, now) is { } c ? "resets " + c : "";
        string? forecast = estimate switch
        {
            { FullAt: { } full } => "100 % " + CountdownFormatter.Format(full, now),
            { AtReset: { } atReset } => string.Create(CultureInfo.InvariantCulture, $"~{atReset} % at reset"),
            _ => null,
        };
        Forecast = forecast is null ? "" : (Countdown.Length > 0 ? " · " : "") + forecast;
        ForecastBrush = estimate?.FullAt < bucket.ResetsAt ? Palette.Critical : Palette.TextMuted;
    }
}

/// <summary>
/// Window view model over <see cref="UsageState"/>. Re-renders countdowns and the "updated … ago" footer on a
/// 15 s timer, so they stay current even while polls fail.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan s_hintDuration = TimeSpan.FromSeconds(2);

    private readonly UsageState _state;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string, bool> _copy;
    private DispatcherTimer? _tick;
    private DispatcherTimer? _hintTimer;
    private string? _transientHint;

    private string _footerText = FooterFormat.Waiting;
    private string? _footerToolTip;
    private string _footerHint = "";
    private bool _isError;
    private bool _canCopy;
    private IReadOnlyList<UsageSample> _samples = [];
    private DateTimeOffset _now;

    public MainViewModel(UsageState state, Func<DateTimeOffset>? clock = null, Func<string, bool>? copy = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _copy = copy ?? CopyToClipboard;

        Session = new BucketRow("Session", Palette.SessionSeries);
        Week = new BucketRow("Week", Palette.WeekSeries);
        ModelWeek = new BucketRow(UsageSnapshot.DefaultModelWeekLabel, Palette.ModelSeries);
        Rows = [Session, Week, ModelWeek];

        _state.Changed += (_, _) => Refresh();
        Refresh();
    }

    public BucketRow Session { get; }

    public BucketRow Week { get; }

    public BucketRow ModelWeek { get; }

    public IReadOnlyList<BucketRow> Rows { get; }

    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }

    /// <summary>Full error message when the footer shows a shortened one.</summary>
    public string? FooterToolTip { get => _footerToolTip; private set => Set(ref _footerToolTip, value); }

    /// <summary>"click to copy", briefly "copied" after a click; empty when there is nothing to copy.</summary>
    public string FooterHint { get => _footerHint; private set => Set(ref _footerHint, value); }

    public bool IsError { get => _isError; private set => Set(ref _isError, value); }

    /// <summary>True when the last poll failed and left raw output that can be copied.</summary>
    public bool CanCopy { get => _canCopy; private set => Set(ref _canCopy, value); }

    public IReadOnlyList<UsageSample> Samples { get => _samples; private set => Set(ref _samples, value); }

    public DateTimeOffset Now { get => _now; private set => Set(ref _now, value); }

    /// <summary>Starts the 15 s re-render timer on the current dispatcher.</summary>
    public void Start()
    {
        if (_tick is not null)
        {
            return;
        }

        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TickInterval };
        _tick.Tick += (_, _) => Refresh();
        _tick.Start();
    }

    public void Refresh()
    {
        var now = _clock();
        var latest = _state.Latest;
        var history = _state.History;
        Session.Update(latest?.Session, now, LimitForecast.Session(latest?.Session, now));
        Week.Update(latest?.Week, now, LimitForecast.Weekly(latest?.Week, history, s => s.Week, now));
        ModelWeek.Update(latest?.ModelWeek, now, LimitForecast.Weekly(latest?.ModelWeek, history, s => s.ModelWeek, now));
        if (latest is not null)
        {
            ModelWeek.Label = latest.ModelWeekLabel;
        }

        if (_state.LastError is { } error)
        {
            IsError = true;
            FooterText = FooterFormat.Error(error.Message);
            FooterToolTip = error.Message;
            CanCopy = !string.IsNullOrEmpty(error.RawText);
        }
        else
        {
            IsError = false;
            FooterText = FooterFormat.Updated(_state.LastUpdated, now);
            FooterToolTip = null;
            CanCopy = false;
        }

        FooterHint = _transientHint ?? (CanCopy ? FooterFormat.CopyHint : "");
        Samples = history;
        Now = now;
    }

    /// <summary>Copies the raw output of the failed poll and briefly confirms in the footer.</summary>
    public void CopyRawText()
    {
        if (_state.LastError?.RawText is not { Length: > 0 } raw)
        {
            return;
        }

        _transientHint = _copy(raw) ? FooterFormat.Copied : FooterFormat.CopyFailed;
        FooterHint = _transientHint;

        _hintTimer ??= CreateHintTimer();
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private DispatcherTimer CreateHintTimer()
    {
        var timer = new DispatcherTimer { Interval = s_hintDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _transientHint = null;
            Refresh();
        };
        return timer;
    }

    private static bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (ExternalException)
        {
            // Another process holds the clipboard open.
            return false;
        }
    }
}
