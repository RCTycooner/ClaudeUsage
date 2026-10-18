using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudeUsage.Storage;
using ClaudeUsage.Usage;

namespace ClaudeUsage.UI;

/// <summary>
/// Debug aid behind <c>--snapshot &lt;path.png&gt;</c>: renders the window content offscreen with 7 days of synthetic
/// history and writes a PNG. Touches no settings, history or tray, and never runs claude.exe.
/// </summary>
public static class SnapshotRenderer
{
    public static void Render(string path)
    {
        var now = DateTimeOffset.Now;
        var history = SyntheticHistory(now);
        var state = new UsageState();
        state.SetSnapshot(history[^1].ToSnapshot() with { Taken = now.AddSeconds(-12) }, history);

        var viewModel = new MainViewModel(state, () => now);
        using var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "ClaudeUsage-snapshot"));
        var window = new MainWindow(store, viewModel);

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

    /// <summary>
    /// One sample a minute for 7 days: a 5-hour session sawtooth (idle at night), weekly ramps that reset 3 days
    /// ago, and two overnight gaps where the machine was asleep.
    /// </summary>
    public static IReadOnlyList<UsageSample> SyntheticHistory(DateTimeOffset now)
    {
        var samples = new List<UsageSample>();
        var start = now - ChartMath.Range;
        var weekReset = now.AddDays(-3).AddHours(-2);
        double week = 38, fable = 52, session = 0;
        DateTimeOffset sessionStart = start;
        var random = new Random(7);

        for (var t = start; t <= now; t = t.AddMinutes(1))
        {
            var local = t.ToLocalTime();
            bool asleep = local.Hour is >= 1 and < 7 && (now - t).TotalDays is > 1.5 and < 2.5 or > 4.5 and < 5.5;
            if (asleep)
            {
                continue;
            }

            if (t - sessionStart >= TimeSpan.FromHours(5))
            {
                sessionStart = t;
                session = 0;
            }

            if (t >= weekReset && t < weekReset.AddMinutes(1))
            {
                week = 0;
                fable = 0;
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
                weekReset.AddDays(7),
                weekReset.AddDays(7)));
        }

        return samples;
    }
}
