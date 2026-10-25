using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ClaudeUsage.Projects;
using ClaudeUsage.Storage;
using ClaudeUsage.Tray;

namespace ClaudeUsage.UI;

public partial class MainWindow : Window
{
    /// <summary>Height used for the on-screen test; the real height is content-sized and unknown before layout.</summary>
    private const double NominalHeight = 120;

    private readonly SettingsStore _store;
    private readonly MainViewModel _viewModel;
    private readonly ProjectCatalog _projects;
    private readonly bool _loaded;
    private AppSettings _settings;
    private HistoryWindow? _history;

    /// <param name="store">Where position and stay-on-top are saved.</param>
    /// <param name="settings">Settings as loaded once at startup; the window keeps its own copy from here on.</param>
    /// <param name="viewModel">The view model.</param>
    /// <param name="projects">The scanned projects, shown by the projects button.</param>
    public MainWindow(SettingsStore store, AppSettings settings, MainViewModel viewModel, ProjectCatalog projects)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _store = store;
        _settings = settings;
        _viewModel = viewModel;
        _projects = projects;
        InitializeComponent();
        DataContext = viewModel;

        Topmost = settings.Topmost;
        PlaceWindow(settings);
        _loaded = true;
    }

    /// <summary>Raised when the user picks a project from the projects button's menu.</summary>
    public event EventHandler<ClaudeProject>? OpenProjectRequested;

    /// <summary>Raised when the user picks "Rescan" in the projects button's menu.</summary>
    public event EventHandler? RescanProjectsRequested;

    /// <summary>Raised when the user clicks the settings button.</summary>
    public event EventHandler? SettingsRequested;

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

    /// <summary>The kept settings with the current position and stay-on-top.</summary>
    public AppSettings Settings => CurrentSettings();

    public void SaveNow() => _store.Save(CurrentSettings());

    /// <summary>Takes settings edited in the settings window, applies stay-on-top and saves straight away.</summary>
    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Topmost = settings.Topmost;
        SaveNow();
    }

    /// <summary>Records the Start-with-Windows choice in the kept settings and queues a save.</summary>
    public void SetStartWithWindows(bool enabled)
    {
        _settings = _settings with { StartWithWindows = enabled };
        QueueSave();
    }

    /// <summary>Records the weekly reset the insights report was mailed for and queues a save.</summary>
    public void SetInsightsSentFor(DateTimeOffset reset)
    {
        _settings = _settings with { InsightsSentFor = reset };
        QueueSave();
    }

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

    /// <summary>Drops down the project list under the button; built on each click so it is never stale.</summary>
    private void OnProjectsClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        if (_projects.Projects is not { Count: > 0 } projects)
        {
            menu.Items.Add(new MenuItem { Header = _projects.EmptyText, IsEnabled = false });
        }
        else
        {
            foreach (var project in projects)
            {
                // "_" would otherwise mark the next letter as the access key.
                var item = new MenuItem { Header = project.Name.Replace("_", "__", StringComparison.Ordinal), ToolTip = project.Directory };
                item.Click += (_, _) => OpenProjectRequested?.Invoke(this, project);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        var rescan = new MenuItem { Header = "Rescan", IsEnabled = !_projects.IsScanning };
        rescan.Click += (_, _) => RescanProjectsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(rescan);

        menu.IsOpen = true;
    }

    /// <summary>Toggles the history window: opens it, or closes the open one.</summary>
    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_history is not null)
        {
            _history.Close();
            return;
        }

        _history = new HistoryWindow(_viewModel);
        _history.Closed += (_, _) => _history = null;
        _history.Show();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

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
