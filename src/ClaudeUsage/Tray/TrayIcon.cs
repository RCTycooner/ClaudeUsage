using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClaudeUsage.Storage;
using ClaudeUsage.Usage;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace ClaudeUsage.Tray;

/// <summary>System tray icon with a context menu. All WinForms types live in this folder.</summary>
public sealed partial class TrayIcon : IDisposable
{
    private static readonly MethodInfo? s_showContextMenu =
        typeof(WinForms.NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly IStartupRegistration _startup;
    private readonly WinForms.ToolStripMenuItem _refreshItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private readonly WinForms.ToolStripMenuItem _sessionItem;
    private readonly WinForms.ToolStripMenuItem _weekItem;
    private readonly WinForms.ToolStripMenuItem _modelItem;
    private Drawing.Icon _icon;
    private UsageSnapshot? _snapshot;
    private bool _ignoreNextMouseUp;
    private bool _disposed;

    public TrayIcon(IStartupRegistration startup)
    {
        _startup = startup;
        _icon = CreateFallbackIcon();
        _menu = new WinForms.ContextMenuStrip();

        _sessionItem = AddStatItem();
        _weekItem = AddStatItem();
        _modelItem = AddStatItem();
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Show UI", null, (_, _) => ShowUiRequested?.Invoke(this, EventArgs.Empty));
        _refreshItem = (WinForms.ToolStripMenuItem)_menu.Items.Add("Refresh now", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add("Open data folder", null, (_, _) => OpenDataFolder());
        _startupItem = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        _startupItem.Click += OnStartupClick;
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = UsageText.WaitingTooltip,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseUp += OnMouseUp;
        _notifyIcon.MouseDoubleClick += OnMouseDoubleClick;
        _menu.Opening += OnMenuOpening;
        RefreshStatItems();
    }

    public event EventHandler? ShowUiRequested;

    public event EventHandler? QuitRequested;

    /// <summary>Raised after the user toggles "Start with Windows", with the registration state it ended in.</summary>
    public event EventHandler<bool>? StartWithWindowsChanged;

    public event EventHandler? RefreshRequested;

    /// <summary>Disables "Refresh now" while a refresh is running.</summary>
    public void SetRefreshing(bool refreshing) => _refreshItem.Enabled = !refreshing;

    /// <summary>
    /// Shows the last known snapshot in the icon, tooltip and stat items. With <paramref name="hasError"/> the last
    /// known values stay, marked as stale (tooltip prefix, red dot on the icon); "!" only when there is no data.
    /// </summary>
    public void Update(UsageSnapshot? snapshot, bool hasError = false)
    {
        _snapshot = snapshot;
        _notifyIcon.Text = UsageText.Truncate(UsageText.Tooltip(snapshot, hasError));
        RefreshStatItems();
        UpdateIcon(snapshot, hasError);
    }

    /// <summary>Hides and disposes the icon. Safe to call more than once (also from the crash handler).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.MouseUp -= OnMouseUp;
        _notifyIcon.MouseDoubleClick -= OnMouseDoubleClick;
        _menu.Opening -= OnMenuOpening;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    private static Drawing.Icon CreateFallbackIcon()
    {
        string? exe = Environment.ProcessPath;
        if (exe is not null)
        {
            try
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon is not null)
                {
                    return icon;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Fall through to a rendered icon.
            }
        }

        return CreateIcon(null, hasError: false);
    }

    private static Drawing.Icon CreateIcon(int? percent, bool hasError)
    {
        using var bitmap = TrayIconRenderer.Render(percent, hasError, WinForms.SystemInformation.SmallIconSize);

        // Clone so the icon owns its data, then free the GDI handle from GetHicon.
        nint hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(hIcon);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private void UpdateIcon(UsageSnapshot? snapshot, bool hasError)
    {
        int? highest = null;
        foreach (var bucket in new[] { snapshot?.Session, snapshot?.Week, snapshot?.ModelWeek })
        {
            if (bucket is not null)
            {
                highest = Math.Max(highest ?? 0, bucket.Percent);
            }
        }

        var old = _icon;
        _icon = highest is null && !hasError ? CreateFallbackIcon() : CreateIcon(highest, hasError);
        _notifyIcon.Icon = _icon;
        old.Dispose();
    }

    private static void OpenDataFolder()
    {
        string folder = SettingsStore.DefaultFolder;
        Directory.CreateDirectory(folder);
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void OnStartupClick(object? sender, EventArgs e)
    {
        try
        {
            _startup.SetEnabled(!_startup.IsEnabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Leave the checkbox as the registry reports it.
        }

        _startupItem.Checked = _startup.IsEnabled;
        StartWithWindowsChanged?.Invoke(this, _startupItem.Checked);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint handle);

    /// <summary>
    /// A stat line: enabled so it renders in normal (readable) text rather than greyed out, but it has no click
    /// action; clicking it just closes the menu.
    /// </summary>
    private WinForms.ToolStripMenuItem AddStatItem()
    {
        var item = new WinForms.ToolStripMenuItem();
        _menu.Items.Add(item);
        return item;
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        RefreshStatItems();
        _startupItem.Checked = _startup.IsEnabled;
    }

    /// <summary>Recomputes the countdowns, so they are current whenever the menu opens.</summary>
    private void RefreshStatItems()
    {
        var now = DateTimeOffset.Now;
        _sessionItem.Text = UsageText.MenuLine("Session", _snapshot?.Session, now);
        _weekItem.Text = UsageText.MenuLine("Week", _snapshot?.Week, now);
        _modelItem.Text = UsageText.MenuLine(
            _snapshot?.ModelWeekLabel ?? UsageSnapshot.DefaultModelWeekLabel, _snapshot?.ModelWeek, now);
    }

    private void OnMouseDoubleClick(object? sender, WinForms.MouseEventArgs e)
    {
        // A double click raises MouseUp twice; the first already opened the menu.
        if (e.Button == WinForms.MouseButtons.Left)
        {
            _ignoreNextMouseUp = true;
        }
    }

    private void OnMouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        // Right click already opens the ContextMenuStrip; make left click do the same.
        if (e.Button != WinForms.MouseButtons.Left)
        {
            return;
        }

        if (_ignoreNextMouseUp)
        {
            _ignoreNextMouseUp = false;
            return;
        }

        if (_menu.Visible)
        {
            return;
        }

        if (s_showContextMenu is not null)
        {
            s_showContextMenu.Invoke(_notifyIcon, null);
        }
        else
        {
            // Without foreground activation the menu would not close when the user clicks elsewhere.
            SetForegroundWindow(_menu.Handle);
            _menu.Show(WinForms.Cursor.Position);
        }
    }
}
