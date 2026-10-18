using System.Reflection;
using System.Runtime.InteropServices;
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

    public TrayIcon()
    {
        _icon = CreatePlaceholderIcon();
        _menu = new WinForms.ContextMenuStrip();

        // Phase 2: insert three disabled stat items at index 0..2 followed by a ToolStripSeparator
        // (see Update); they sit above "Show UI" / "Quit".
        _menu.Items.Add("Show UI", null, (_, _) => ShowUiRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = "Claude usage",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseUp += OnMouseUp;
    }

    public event EventHandler? ShowUiRequested;

    public event EventHandler? QuitRequested;

    /// <summary>Phase 2 hook: update the tooltip (max 127 chars) and the stat menu items.</summary>
    public void Update(string tooltip)
    {
        _notifyIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.MouseUp -= OnMouseUp;
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
