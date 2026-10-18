using System.Windows;
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

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        if (!isFirst)
        {
            _showEvent.Set();
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        var folder = SettingsStore.DefaultFolder;
        _settingsStore = new SettingsStore(folder);
        _logger = new FileLogger(folder);
        _window = new MainWindow(_settingsStore);
        _tray = new TrayIcon();
        _tray.ShowUiRequested += (_, _) => ShowWindow();
        _tray.QuitRequested += (_, _) => Quit();

        // Show the last known values straight away, then start polling (first poll runs immediately).
        _history = new UsageHistoryStore(folder);
        var history = _history.Load();
        if (history.Count > 0)
        {
            var last = history[^1].ToSnapshot();
            UsageState.SetSnapshot(last, history);
            _tray.Update(last);
        }

        _poller = new UsagePoller(new ClaudeCliUsageSource(folder, _settingsStore.Load().ClaudePath), Dispatcher);
        _poller.Updated += OnUsageUpdated;
        _poller.Failed += OnUsageFailed;
        _poller.Start();

        StartListeningForSecondInstance();
        _window.Show();
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
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
    }

    private void OnUsageUpdated(object? sender, UsageSnapshot snapshot)
    {
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

    private void ShowWindow() => _window?.ShowAndActivate();

    private void Quit()
    {
        IsQuitting = true;
        _window?.SaveNow();
        Shutdown();
    }

    private void StartListeningForSecondInstance()
    {
        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;
        var showEvent = _showEvent!;

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
