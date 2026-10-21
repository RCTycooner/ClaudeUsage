using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClaudeUsage.Projects;
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
    private readonly WinForms.ToolStripMenuItem _projectsItem;
    private readonly ProjectCatalog _projects;
    private readonly Drawing.Icon _icon;
    private UsageSnapshot? _snapshot;
    private bool _ignoreNextMouseUp;
    private bool _disposed;

    public TrayIcon(IStartupRegistration startup, ProjectCatalog projects)
    {
        _startup = startup;
        _projects = projects;
        _icon = CreateIcon();
        _menu = new WinForms.ContextMenuStrip();

        _sessionItem = AddStatItem();
        _weekItem = AddStatItem();
        _modelItem = AddStatItem();
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Show UI", null, (_, _) => ShowUiRequested?.Invoke(this, EventArgs.Empty));
        _projectsItem = new WinForms.ToolStripMenuItem("Claude projects");
        _menu.Items.Add(_projectsItem);
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
        _projects.Changed += OnProjectsChanged;
        RefreshStatItems();
        RefreshProjectItems();
    }

    public event EventHandler? ShowUiRequested;

    public event EventHandler? QuitRequested;

    /// <summary>Raised after the user toggles "Start with Windows", with the registration state it ended in.</summary>
    public event EventHandler<bool>? StartWithWindowsChanged;

    public event EventHandler? RefreshRequested;

    /// <summary>Raised when the user picks a project from the "Claude projects" submenu.</summary>
    public event EventHandler<ClaudeProject>? OpenProjectRequested;

    /// <summary>Raised when the user picks "Rescan" in the "Claude projects" submenu.</summary>
    public event EventHandler? RescanProjectsRequested;

    /// <summary>Disables "Refresh now" while a refresh is running.</summary>
    public void SetRefreshing(bool refreshing) => _refreshItem.Enabled = !refreshing;

    /// <summary>
    /// Shows the last known snapshot in the tooltip and stat items. With <paramref name="hasError"/> the last known
    /// values stay, marked as stale in the tooltip. The icon itself is static.
    /// </summary>
    public void Update(UsageSnapshot? snapshot, bool hasError = false)
    {
        _snapshot = snapshot;
        string text = UsageText.Truncate(UsageText.Tooltip(snapshot, hasError));
        if (!string.Equals(_notifyIcon.Text, text, StringComparison.Ordinal))
        {
            _notifyIcon.Text = text;
        }

        RefreshStatItems();
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
        _projects.Changed -= OnProjectsChanged;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    /// <summary>Renders the static icon once at the tray's small icon size.</summary>
    private static Drawing.Icon CreateIcon()
    {
        using var bitmap = TrayIconRenderer.Render(WinForms.SystemInformation.SmallIconSize);

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

    private void OnProjectsChanged(object? sender, EventArgs e) => RefreshProjectItems();

    /// <summary>
    /// One item per project (full path as tooltip), or a disabled line while scanning or when none were found;
    /// then "Rescan", disabled while a scan runs.
    /// </summary>
    private void RefreshProjectItems()
    {
        _projectsItem.DropDownItems.Clear();
        if (_projects.Projects is not { Count: > 0 } projects)
        {
            _projectsItem.DropDownItems.Add(new WinForms.ToolStripMenuItem(_projects.EmptyText) { Enabled = false });
        }
        else
        {
            foreach (var project in projects)
            {
                // "&" would otherwise mark the next letter as the access key.
                var item = new WinForms.ToolStripMenuItem(project.Name.Replace("&", "&&", StringComparison.Ordinal))
                {
                    ToolTipText = project.Directory,
                };
                item.Click += (_, _) => OpenProjectRequested?.Invoke(this, project);
                _projectsItem.DropDownItems.Add(item);
            }
        }

        _projectsItem.DropDownItems.Add(new WinForms.ToolStripSeparator());
        var rescan = new WinForms.ToolStripMenuItem("Rescan") { Enabled = !_projects.IsScanning };
        rescan.Click += (_, _) => RescanProjectsRequested?.Invoke(this, EventArgs.Empty);
        _projectsItem.DropDownItems.Add(rescan);
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
