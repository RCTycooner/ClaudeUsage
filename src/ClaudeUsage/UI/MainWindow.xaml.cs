using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ClaudeUsage.Storage;

namespace ClaudeUsage.UI;

public partial class MainWindow : Window
{
    private const double MinVisiblePixels = 40;

    private readonly SettingsStore _store;
    private readonly bool _loaded;

    public MainWindow(SettingsStore store)
    {
        _store = store;
        InitializeComponent();

        var settings = store.Load();
        Topmost = settings.Topmost;
        PlaceWindow(settings);
        _loaded = true;
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    public void SaveNow() => _store.Save(CurrentSettings());

    private AppSettings CurrentSettings() =>
        new(Topmost, Left, Top, _store.Load().ClaudePath);

    private void PlaceWindow(AppSettings settings)
    {
        // Width is fixed; height is content-sized, so use a nominal height for the visibility test.
        if (settings.Left is double left && settings.Top is double top && IsOnScreen(left, top))
        {
            Left = left;
            Top = top;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private bool IsOnScreen(double left, double top)
    {
        double vLeft = SystemParameters.VirtualScreenLeft;
        double vTop = SystemParameters.VirtualScreenTop;
        double vRight = vLeft + SystemParameters.VirtualScreenWidth;
        double vBottom = vTop + SystemParameters.VirtualScreenHeight;

        return left + Width > vLeft + MinVisiblePixels
            && left < vRight - MinVisiblePixels
            && top >= vTop
            && top < vBottom - MinVisiblePixels;
    }

    private void OnBodyMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Buttons mark their clicks handled, so this only fires for the body.
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not ButtonBase)
        {
            DragMove();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Application.Current is App { IsQuitting: true })
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void OnPinChanged(object sender, RoutedEventArgs e) => QueueSave();

    private void OnLocationChanged(object? sender, EventArgs e) => QueueSave();

    private void QueueSave()
    {
        if (_loaded)
        {
            _store.QueueSave(CurrentSettings());
        }
    }
}
