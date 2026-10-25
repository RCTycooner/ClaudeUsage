using System.IO;
using System.Windows;
using System.Windows.Input;
using ClaudeUsage.Projects;
using ClaudeUsage.Storage;
using ClaudeUsage.Usage;
using Microsoft.Win32;

namespace ClaudeUsage.UI;

/// <summary>
/// Edits the user-facing parts of settings.json. Save raises <see cref="Saved"/> with the new settings and closes;
/// the app applies and writes them. Position is not shown: the widget keeps that up to date itself.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    /// <param name="settings">The settings as the app currently has them.</param>
    public SettingsWindow(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        _settings = settings;

        ClaudePathBox.Text = settings.ClaudePath ?? string.Empty;
        ProjectsRootBox.Text = settings.ProjectsRoot ?? string.Empty;
        ProjectsRootHint.Text =
            $"Scanned for {ProjectScanner.MarkerFileName} files (the projects menu). Leave blank for {AppSettings.DefaultProjectsRoot}";
        InsightsEmailBox.Text = settings.InsightsEmail ?? string.Empty;
        TopmostBox.IsChecked = settings.Topmost;
        StartWithWindowsBox.IsChecked = settings.StartWithWindows == true;
    }

    /// <summary>Raised on Save with the edited settings (position fields unchanged).</summary>
    public event EventHandler<AppSettings>? Saved;

    private void OnBrowseClaudeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Find the Claude CLI",
            Filter = $"Claude CLI|{ClaudeCliUsageSource.ExeName};{ClaudeCliUsageSource.CmdName}|Programs (*.exe, *.cmd)|*.exe;*.cmd",
        };

        // Start where the current (or auto-detected) CLI lives.
        if (ClaudeCliUsageSource.ResolveExe(ClaudePathBox.Text) is { } current)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
            dialog.FileName = Path.GetFileName(current);
        }

        if (dialog.ShowDialog(this) == true)
        {
            ClaudePathBox.Text = dialog.FileName;
        }
    }

    private void OnBrowseRootClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Projects folder" };
        string current = ProjectsRootBox.Text.Trim().Trim('"');
        dialog.InitialDirectory = Directory.Exists(current) ? current : AppSettings.DefaultProjectsRoot;

        if (dialog.ShowDialog(this) == true)
        {
            ProjectsRootBox.Text = dialog.FolderName;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (SettingsForm.Validate(ClaudePathBox.Text, ProjectsRootBox.Text, InsightsEmailBox.Text) is { } error)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Saved?.Invoke(this, SettingsForm.Apply(
            _settings, ClaudePathBox.Text, ProjectsRootBox.Text, InsightsEmailBox.Text, TopmostBox.IsChecked == true,
            StartWithWindowsBox.IsChecked == true));
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
