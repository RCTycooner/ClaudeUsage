namespace ClaudeUsage.Projects;

/// <summary>
/// The scanned project list shared by the tray menu and the window's projects button. Only touched on the UI
/// thread: the scan runs in the background and the result is handed over with <see cref="SetProjects"/>.
/// </summary>
public sealed class ProjectCatalog(string root)
{
    public string Root { get; } = root;

    /// <summary>Null while the scan is still running.</summary>
    public IReadOnlyList<ClaudeProject>? Projects { get; private set; }

    /// <summary>What a menu shows in place of the list when there are no items.</summary>
    public string EmptyText => Projects is null
        ? $"Scanning {Root} for {ProjectScanner.MarkerFileName}…"
        : $"No {ProjectScanner.MarkerFileName} found under {Root}";

    public bool IsScanning => Projects is null;

    public event EventHandler? Changed;

    /// <summary>Drops the current list so the menus show "Scanning…" until <see cref="SetProjects"/>.</summary>
    public void BeginScan()
    {
        Projects = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetProjects(IReadOnlyList<ClaudeProject> projects)
    {
        Projects = projects;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
