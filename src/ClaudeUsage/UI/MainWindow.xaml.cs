using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ClaudeUsage.Storage;
using ClaudeUsage.Tray;

namespace ClaudeUsage.UI;

public partial class MainWindow : Window
{
    /// <summary>Height used for the on-screen test; the real height is content-sized and unknown before layout.</summary>
    private const double NominalHeight = 120;

    private readonly SettingsStore _store;
    private readonly MainViewModel _viewModel;
    private readonly bool _loaded;
    private AppSettings _settings;

    /// <param name="store">Where position and stay-on-top are saved.</param>
    /// <param name="settings">Settings as loaded once at startup; the window keeps its own copy from here on.</param>
    /// <param name="viewModel">The view model.</param>
    public MainWindow(SettingsStore store, AppSettings settings, MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _store = store;
        _settings = settings;
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

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

    /// <summary>
    /// The kept settings with the current position and stay-on-top; no disk read. Left/Top are NaN until a
    /// centred window is first shown (e.g. started with --hidden); the saved position is kept then.
    /// </summary>
    private AppSettings CurrentSettings() => _settings = _settings with
    {
        Left = double.IsNaN(Left) ? _settings.Left : Left,
        Top = double.IsNaN(Top) ? _settings.Top : Top,
        Topmost = Topmost,
    };

    private void PlaceWindow(AppSettings settings)
    {
        // Width is fixed; height is content-sized, so use a nominal height for the visibility test.
        if (settings.Left is double left && settings.Top is double top
            && WindowPlacement.IsReachable(new Rect(left, top, Width, NominalHeight), ScreenAreas.WorkingAreas()))
        {
            Left = left;
            Top = top;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
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

    private void OnFooterClick(object sender, RoutedEventArgs e) => _viewModel.CopyRawText();

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
