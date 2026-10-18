using System.Windows;
using ClaudeUsage.Storage;
using ClaudeUsage.Tray;
using ClaudeUsage.UI;

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

    /// <summary>True once the app is really exiting, so the window's Closing handler lets it close.</summary>
    public bool IsQuitting { get; private set; }

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

        _settingsStore = new SettingsStore(SettingsStore.DefaultFolder);
        _window = new MainWindow(_settingsStore);
        _tray = new TrayIcon();
        _tray.ShowUiRequested += (_, _) => ShowWindow();
        _tray.QuitRequested += (_, _) => Quit();

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
