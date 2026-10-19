using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudeUsage.Storage;
using ClaudeUsage.Usage;

namespace ClaudeUsage.UI;

/// <summary>
/// Debug aid behind <c>--snapshot &lt;path.png&gt;</c> and <c>--snapshot-history &lt;path.png&gt;</c>: renders the
/// window or the history window content offscreen with synthetic history and writes a PNG. Touches no settings,
/// history or tray, and never runs claude.exe.
/// </summary>
public static class SnapshotRenderer
{
    public static void Render(string path)
    {
        var now = DateTimeOffset.Now;
        var viewModel = SyntheticViewModel(now, ChartMath.Range);
        using var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "ClaudeUsage-snapshot"));
        var window = new MainWindow(store, new AppSettings(), viewModel);

        // Render the window's content tree on its own; the window itself is never shown.
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        root.DataContext = viewModel;
        // Two passes: the rows' item containers only exist after the first layout.
        var size = default(Size);
        for (int pass = 0; pass < 2; pass++)
        {
            root.Measure(new Size(window.Width, double.PositiveInfinity));
            size = new Size(window.Width, root.DesiredSize.Height); // includes the margin around the panel
            root.Arrange(new Rect(size));
            root.UpdateLayout();
        }

        Save(root, size, path);
    }

    /// <summary>
    /// The history window at its default size over 30 days of data, showing the last <paramref name="show"/> (all
    /// of it by default), with the crosshair read-out at 70 % of the plot width.
    /// </summary>
    public static void RenderHistory(string path, TimeSpan? show = null)
    {
        var now = DateTimeOffset.Now;
        var viewModel = SyntheticViewModel(now, TimeSpan.FromDays(30));
        var window = new HistoryWindow(viewModel);
        if (show is { } span)
        {
            window.ViewModel.ShowLast(span);
        }
        else
        {
            window.ViewModel.ShowAll();
        }

        var content = (UIElement)window.Content;
        window.Content = null;
        // The window's background and border are not part of its content; wrap it to get them in the picture.
        var root = new Border
        {
            Background = window.Background,
            BorderBrush = window.BorderBrush,
            BorderThickness = window.BorderThickness,
            DataContext = window.DataContext,
            Child = content,
        };
        root.Resources.MergedDictionaries.Add(window.Resources);
        var size = new Size(window.Width, window.Height);
        for (int pass = 0; pass < 2; pass++)
        {
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
        }

        var plot = window.Chart.PlotArea;
        window.Chart.HoverPoint = new Point(plot.Left + (plot.Width * 0.7), plot.Top + (plot.Height / 2));
        root.UpdateLayout(); // re-renders the chart with the crosshair
        Save(root, size, path);
    }

    /// <summary>
    /// One sample a minute for <paramref name="days"/> (default 7): a 5-hour session sawtooth (idle at night), weekly
    /// ramps that reset every 7 days (last 3 days ago), and an overnight gap every third night where the machine was asleep.
    /// </summary>
    public static IReadOnlyList<UsageSample> SyntheticHistory(DateTimeOffset now, TimeSpan? days = null)
    {
        var samples = new List<UsageSample>();
        var start = now - (days ?? ChartMath.Range);
        var weekReset = now.AddDays(-3).AddHours(-2);
        while (weekReset - TimeSpan.FromDays(7) > start)
        {
            weekReset -= TimeSpan.FromDays(7);
        }

        double week = 38, fable = 52, session = 0;
        DateTimeOffset sessionStart = start;
        var random = new Random(7);

        for (var t = start; t <= now; t = t.AddMinutes(1))
        {
            var local = t.ToLocalTime();
            bool asleep = local.Hour is >= 1 and < 7 && (int)(now - t).TotalDays % 3 == 1;
            if (asleep)
            {
                continue;
            }

            if (t - sessionStart >= TimeSpan.FromHours(5))
            {
                sessionStart = t;
                session = 0;
            }

            if (t >= weekReset)
            {
                week = 0;
                fable = 0;
                weekReset += TimeSpan.FromDays(7);
            }

            bool working = local.Hour is >= 8 and < 23;
            if (working)
            {
                double burst = random.NextDouble() < 0.35 ? random.NextDouble() * 0.9 : 0;
                session = Math.Min(100, session + burst);
                week += burst * 0.055;
                fable += burst * 0.075;
            }

            samples.Add(new UsageSample(
                t,
                (int)session,
                (int)week,
                (int)Math.Min(fable, 104),
                sessionStart.AddHours(5),
                weekReset,
                weekReset,
                "Fable"));
        }

        return samples;
    }

    private static MainViewModel SyntheticViewModel(DateTimeOffset now, TimeSpan days)
    {
        var history = SyntheticHistory(now, days);
        var state = new UsageState();
        state.SetSnapshot(history[^1].ToSnapshot() with { Taken = now.AddSeconds(-12) }, history);
        return new MainViewModel(state, () => now);
    }

    private static void Save(Visual root, Size size, string path)
    {
        const double scale = 2;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * scale),
            (int)Math.Ceiling(size.Height * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
