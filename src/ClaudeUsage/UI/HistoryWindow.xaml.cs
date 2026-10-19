using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ClaudeUsage.UI;

/// <summary>
/// Resizable window with the usage chart over all stored history: presets, zoom, scroll bar, and mouse/keyboard
/// pan and zoom. Closing it really closes it; the widget opens a fresh one next time.
/// </summary>
public partial class HistoryWindow : Window
{
    private const double WheelNotch = 120;

    // Default SM_CXPADDEDBORDER; together with the resize border it is how far a maximised window overhangs the screen.
    private const double PaddedBorder = 4;

    private readonly HistoryViewModel _viewModel;
    private readonly Thickness _normalBorder;
    private double? _dragStartX;
    private double _dragStartScroll;

    public HistoryWindow(MainViewModel main)
    {
        InitializeComponent();
        _viewModel = new HistoryViewModel(main);
        DataContext = _viewModel;
        _normalBorder = BorderThickness;
        StateChanged += (_, _) => OnStateChanged();
    }

    /// <summary>For the --snapshot debug render, which lays out the content without showing the window.</summary>
    internal HistoryViewModel ViewModel => _viewModel;

    private void OnClosed(object? sender, EventArgs e) => _viewModel.Detach();

    private void OnStateChanged()
    {
        bool maximized = WindowState == WindowState.Maximized;
        // A chromeless maximised window extends past the screen by its (invisible) frame; pad it back in.
        var frame = SystemParameters.WindowResizeBorderThickness;
        BorderThickness = maximized
            ? new Thickness(frame.Left + PaddedBorder, frame.Top + PaddedBorder, frame.Right + PaddedBorder, frame.Bottom + PaddedBorder)
            : _normalBorder;
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximise";
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, CultureInfo.InvariantCulture, out int days))
        {
            _viewModel.ShowLast(TimeSpan.FromDays(days));
        }
    }

    private void OnAllClick(object sender, RoutedEventArgs e) => _viewModel.ShowAll();

    private void OnZoomInClick(object sender, RoutedEventArgs e) => _viewModel.Zoom(1, ZoomAnchor());

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => _viewModel.Zoom(-1, ZoomAnchor());

    private void OnBackClick(object sender, RoutedEventArgs e) => _viewModel.Pan(-0.5);

    private void OnForwardClick(object sender, RoutedEventArgs e) => _viewModel.Pan(0.5);

    private void OnLiveClick(object sender, RoutedEventArgs e) => _viewModel.GoLive();

    private void OnScroll(object sender, ScrollEventArgs e) => _viewModel.ScrollTo(e.NewValue);

    private void OnChartMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double notches = e.Delta / WheelNotch;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Wheel down = later, like a horizontal scroll.
            _viewModel.Pan(-notches * 0.15);
        }
        else
        {
            _viewModel.Zoom(notches, AnchorAt(e.GetPosition(Chart).X));
        }

        e.Handled = true;
    }

    private void OnChartMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _viewModel.ShowAll();
            e.Handled = true;
            return;
        }

        _dragStartX = e.GetPosition(Chart).X;
        _dragStartScroll = _viewModel.ScrollValue;
        Chart.CaptureMouse();
        Chart.Cursor = Cursors.ScrollWE;
        e.Handled = true;
    }

    private void OnChartMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartX is not { } startX || Chart.PlotArea.Width <= 0)
        {
            return;
        }

        // Grab-and-drag: the point under the cursor follows it.
        double dx = e.GetPosition(Chart).X - startX;
        _viewModel.ScrollTo(_dragStartScroll - (dx / Chart.PlotArea.Width * _viewModel.ViewSpan.TotalHours));
    }

    private void OnChartMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Chart.ReleaseMouseCapture();

    private void OnChartLostMouseCapture(object sender, MouseEventArgs e)
    {
        _dragStartX = null;
        Chart.Cursor = null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Left:
                _viewModel.Pan(ctrl ? -1 : -0.1);
                break;
            case Key.Right:
                _viewModel.Pan(ctrl ? 1 : 0.1);
                break;
            case Key.PageUp:
                _viewModel.Pan(-0.8);
                break;
            case Key.PageDown:
                _viewModel.Pan(0.8);
                break;
            case Key.Add or Key.OemPlus:
                _viewModel.Zoom(1, ZoomAnchor());
                break;
            case Key.Subtract or Key.OemMinus:
                _viewModel.Zoom(-1, ZoomAnchor());
                break;
            case Key.Home:
                _viewModel.ScrollTo(0);
                break;
            case Key.End:
                _viewModel.GoLive();
                break;
            case Key.Escape:
                Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Buttons and keys zoom around the mouse when it is over the chart, else around the centre (right edge while live).</summary>
    private double ZoomAnchor() =>
        Chart.IsMouseOver ? AnchorAt(Mouse.GetPosition(Chart).X)
        : _viewModel.IsLive ? 1
        : 0.5;

    private double AnchorAt(double x)
    {
        var plot = Chart.PlotArea;
        return plot.Width <= 0 ? 0.5 : Math.Clamp((x - plot.Left) / plot.Width, 0, 1);
    }
}
