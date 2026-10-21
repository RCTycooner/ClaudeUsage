using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ClaudeUsage.Projects;
using ClaudeUsage.Storage;
using ClaudeUsage.Tray;
using ClaudeUsage.UI;
using ClaudeUsage.Usage;

namespace ClaudeUsage;

public partial class App : Application, IDisposable
{
    private const string MutexName = @"Local\ClaudeUsageWidget";
    private const string ShowEventName = @"Local\ClaudeUsageWidget.Show";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _listenerCts;
    private SettingsStore? _settingsStore;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private FileLogger? _logger;
    private UsageHistoryStore? _history;
    private UsagePoller? _poller;

    /// <summary>True once the app is really exiting, so the window's Closing handler lets it close.</summary>
    public bool IsQuitting { get; private set; }

    /// <summary>Latest snapshot, last error and history; the window binds to this.</summary>
    public UsageState UsageState { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Debug aid: --snapshot <path.png> renders the window with synthetic data and exits (see SnapshotRenderer).
        if (e.Args is ["--snapshot", var snapshotPath])
        {
            SnapshotRenderer.Render(snapshotPath);
            Shutdown();
            return;
        }

        // --snapshot-history <path.png> [days]: the same for the history window, showing all or the last [days].
        if (e.Args is ["--snapshot-history", var historySnapshotPath, .. var rest] && rest.Length <= 1)
        {
            SnapshotRenderer.RenderHistory(
                historySnapshotPath,
                rest is [var days] && double.TryParse(days, CultureInfo.InvariantCulture, out double d) ? TimeSpan.FromDays(d) : null);
            Shutdown();
            return;
        }

        var folder = SettingsStore.DefaultFolder;
        _logger = new FileLogger(folder);
        HookLastChanceHandlers();

        if (!TryBecomeFirstInstance())
        {
            Shutdown();
            return;
        }

        // --hidden (used by the Start-with-Windows entry): start in the tray without showing the window.
        bool startHidden = e.Args.Contains(RunKeyStartupRegistration.HiddenArgument, StringComparer.OrdinalIgnoreCase);

        _settingsStore = new SettingsStore(folder);
        var startup = new RunKeyStartupRegistration();
        var settings = ApplyStartWithWindows(startup, _settingsStore.Load());
        var viewModel = new MainViewModel(UsageState);
        viewModel.Start();
        var projects = new ProjectCatalog(settings.EffectiveProjectsRoot);
        _window = new MainWindow(_settingsStore, settings, viewModel, projects);
        _window.OpenProjectRequested += (_, project) => OpenProject(project);
        _window.RescanProjectsRequested += (_, _) => RescanProjects(projects);
        _tray = new TrayIcon(startup, projects);
        _tray.OpenProjectRequested += (_, project) => OpenProject(project);
        _tray.RescanProjectsRequested += (_, _) => RescanProjects(projects);
        _ = ScanProjectsAsync(projects);
        _tray.StartWithWindowsChanged += (_, enabled) => _window.SetStartWithWindows(enabled);
        _tray.ShowUiRequested += (_, _) => ShowWindow();
        _tray.QuitRequested += (_, _) => Quit();
        _tray.RefreshRequested += (_, _) => _ = RefreshNowAsync();

        // Show the last known values straight away, then start polling (first poll runs immediately).
        _history = new UsageHistoryStore(folder);
        var history = _history.Load();
        if (history.Count > 0)
        {
            var last = history[^1].ToSnapshot();
            UsageState.SetSnapshot(last, history);
            _tray.Update(last);
        }

        _poller = new UsagePoller(new ClaudeCliUsageSource(folder, settings.ClaudePath), Dispatcher);
        _poller.Updated += OnUsageUpdated;
        _poller.Failed += OnUsageFailed;
        _poller.Start();

        StartListeningForSecondInstance();
        if (!startHidden)
        {
            _window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _listenerCts?.Cancel();
        _poller?.Dispose();
        _tray?.Dispose();
        _settingsStore?.Dispose();
        _showEvent?.Dispose();
        _listenerCts?.Dispose();

        // History and log writes run in the background; let them finish (the poller is stopped, so none follow).
        _history?.Flush();
        _logger?.Flush();
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
    }

    /// <summary>
    /// Takes the single-instance mutex. If another instance holds it, or the mutex or event cannot be created or
    /// opened (e.g. created by an elevated instance, access denied), asks the other instance to show its window and
    /// returns false, rather than crashing.
    /// </summary>
    private bool TryBecomeFirstInstance()
    {
        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
            if (!isFirst)
            {
                _instanceMutex.Dispose();
                _instanceMutex = null;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            _logger?.Log("Could not create the single-instance mutex; assuming another instance is running.", ex);
            _instanceMutex = null;
        }

        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            _logger?.Log("Could not create the show-window event.", ex);
            _showEvent = null;
        }

        if (_instanceMutex is not null)
        {
            return true;
        }

        _showEvent?.Set();
        _showEvent?.Dispose();
        _showEvent = null;
        return false;
    }

    /// <summary>
    /// Makes the Run key match the saved Start-with-Windows choice; a first run with an older settings file adopts
    /// the current registration and saves it. A registry failure is logged and the settings are kept as they are.
    /// </summary>
    private AppSettings ApplyStartWithWindows(IStartupRegistration startup, AppSettings settings)
    {
        try
        {
            var applied = StartupSync.Apply(startup, settings);
            if (applied != settings)
            {
                _settingsStore?.Save(applied);
            }

            return applied;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _logger?.Log("Could not apply the saved Start-with-Windows setting.", ex);
            return settings;
        }
    }

    /// <summary>Logs anything that would otherwise vanish or kill the app silently.</summary>
    private void HookLastChanceHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A widget should survive a bad event handler; log it and keep running.
        _logger?.Log("Unhandled exception on the UI thread (ignored).", e.Exception, e.Exception.StackTrace);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var error = e.ExceptionObject as Exception;
        _logger?.Log($"Fatal unhandled exception (terminating: {e.IsTerminating}).", error, error?.StackTrace);
        _logger?.Flush();

        // The process is going down; remove the tray icon so no ghost icon is left behind.
        try
        {
            _tray?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            // Best effort.
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Log("Unobserved task exception.", e.Exception, e.Exception.StackTrace);
        e.SetObserved();
    }

    private void OnUsageUpdated(object? sender, UsageSnapshot snapshot)
    {
        // Comparing with the previous snapshot logs a disappearing line once, not on every poll.
        var gone = UsageSnapshot.DisappearedBuckets(UsageState.Latest, snapshot);
        if (gone.Count > 0)
        {
            _logger?.Log($"Usage line(s) no longer in /usage output: {string.Join(", ", gone)}.", detail: snapshot.RawText);
        }

        _history?.Append(snapshot);
        UsageState.SetSnapshot(snapshot, _history?.Samples ?? []);
        _tray?.Update(snapshot);
    }

    private void OnUsageFailed(object? sender, UsagePollFailure failure)
    {
        _logger?.Log(failure.Message, failure.Error, failure.RawText);
        UsageState.SetFailure(failure);
        _tray?.Update(UsageState.Latest, hasError: true);
    }

    private async Task RefreshNowAsync()
    {
        if (_poller is null)
        {
            return;
        }

        _tray?.SetRefreshing(true);
        try
        {
            await _poller.RefreshNowAsync();
        }
        finally
        {
            _tray?.SetRefreshing(false);
        }
    }

    /// <summary>Scans the projects root off the UI thread; the menus show "Scanning…" until it finishes.</summary>
    private async Task ScanProjectsAsync(ProjectCatalog catalog)
    {
        IReadOnlyList<ClaudeProject> found;
        try
        {
            found = await Task.Run(() => ProjectScanner.Scan(catalog.Root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger?.Log($"Could not scan {catalog.Root} for projects.", ex);
            found = [];
        }

        catalog.SetProjects(found);
    }

    /// <summary>Starts a fresh scan unless one is already running (the menus disable "Rescan" meanwhile).</summary>
    private void RescanProjects(ProjectCatalog catalog)
    {
        if (catalog.IsScanning)
        {
            return;
        }

        catalog.BeginScan();
        _ = ScanProjectsAsync(catalog);
    }

    private void OpenProject(ClaudeProject project)
    {
        try
        {
            ProjectLauncher.Open(project);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger?.Log($"Could not open PowerShell in {project.Directory}.", ex);
            MessageBox.Show($"Could not open PowerShell in {project.Directory}:\n{ex.Message}", "Claude usage",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowWindow() => _window?.ShowAndActivate();

    private void Quit()
    {
        IsQuitting = true;
        _window?.SaveNow();
        Shutdown();
    }

    private void StartListeningForSecondInstance()
    {
        if (_showEvent is null)
        {
            return;
        }

        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;
        var showEvent = _showEvent;

        _ = Task.Factory.StartNew(() =>
        {
            var handles = new[] { showEvent, token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                Dispatcher.BeginInvoke(ShowWindow);
            }
        }, TaskCreationOptions.LongRunning);
    }
}
