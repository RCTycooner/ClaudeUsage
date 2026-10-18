using System.Reflection;
using System.Runtime.InteropServices;
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
    private readonly Drawing.Icon _icon;
    private readonly WinForms.ToolStripMenuItem _sessionItem;
    private readonly WinForms.ToolStripMenuItem _weekItem;
    private readonly WinForms.ToolStripMenuItem _fableItem;
    private UsageSnapshot? _snapshot;

    public TrayIcon()
    {
        _icon = CreatePlaceholderIcon();
        _menu = new WinForms.ContextMenuStrip();

        _sessionItem = AddStatItem();
        _weekItem = AddStatItem();
        _fableItem = AddStatItem();
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Show UI", null, (_, _) => ShowUiRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = UsageText.WaitingTooltip,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseUp += OnMouseUp;
        _menu.Opening += OnMenuOpening;
        RefreshStatItems();
    }

    public event EventHandler? ShowUiRequested;

    public event EventHandler? QuitRequested;

    /// <summary>
    /// Shows the last known snapshot in the stat items. With <paramref name="hasError"/> the tooltip switches to
    /// the error text, while the menu keeps showing the last known values.
    /// </summary>
    public void Update(UsageSnapshot? snapshot, bool hasError = false)
    {
        _snapshot = snapshot;
        _notifyIcon.Text = UsageText.Truncate(hasError ? UsageText.ErrorTooltip : UsageText.Tooltip(snapshot));
        RefreshStatItems();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.MouseUp -= OnMouseUp;
        _menu.Opening -= OnMenuOpening;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    private static Drawing.Icon CreatePlaceholderIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xD9, 0x77, 0x57));
            g.FillEllipse(brush, 2, 2, 28, 28);
        }

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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint handle);

    private WinForms.ToolStripMenuItem AddStatItem()
    {
        var item = new WinForms.ToolStripMenuItem { Enabled = false };
        _menu.Items.Add(item);
        return item;
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e) => RefreshStatItems();

    /// <summary>Recomputes the countdowns, so they are current whenever the menu opens.</summary>
    private void RefreshStatItems()
    {
        var now = DateTimeOffset.Now;
        _sessionItem.Text = UsageText.MenuLine("Session", _snapshot?.Session, now);
        _weekItem.Text = UsageText.MenuLine("Week", _snapshot?.Week, now);
        _fableItem.Text = UsageText.MenuLine("Fable week", _snapshot?.FableWeek, now);
    }

    private void OnMouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        // Right click already opens the ContextMenuStrip; make left click do the same.
        if (e.Button != WinForms.MouseButtons.Left)
        {
            return;
        }

        if (s_showContextMenu is not null)
        {
            s_showContextMenu.Invoke(_notifyIcon, null);
        }
        else
        {
            _menu.Show(WinForms.Cursor.Position);
        }
    }
}
