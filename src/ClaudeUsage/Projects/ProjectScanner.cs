using System.IO;

namespace ClaudeUsage.Projects;

/// <summary>
/// Walks a folder tree and returns every folder that holds a CLAUDE.md file. Skips hidden and system folders,
/// junctions/symlinks (no loops), and folders that never hold a project of their own (build output, packages,
/// Claude Code's own <c>.claude</c> folder with its worktrees). Unreadable folders are skipped.
/// </summary>
public static class ProjectScanner
{
    public const string MarkerFileName = "CLAUDE.md";

    private static readonly HashSet<string> s_skippedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".claude", ".git", ".vs", ".idea", "node_modules", "bin", "obj", "packages", "$RECYCLE.BIN",
        "System Volume Information",
    };

    private static readonly EnumerationOptions s_subfolders = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>Projects under <paramref name="root"/> (the root included), sorted by name, then path.</summary>
    public static IReadOnlyList<ClaudeProject> Scan(string root, CancellationToken cancellationToken = default)
    {
        var found = new List<ClaudeProject>();
        if (!Directory.Exists(root))
        {
            return found;
        }

        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string folder = pending.Pop();
            try
            {
                if (File.Exists(Path.Combine(folder, MarkerFileName)))
                {
                    found.Add(new ClaudeProject(DisplayName(folder), folder));
                }

                foreach (string sub in Directory.EnumerateDirectories(folder, "*", s_subfolders))
                {
                    if (!s_skippedNames.Contains(Path.GetFileName(sub)))
                    {
                        pending.Push(sub);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Vanished or unreadable folder; carry on with the rest.
            }
        }

        return [.. found
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Directory, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The folder's own name; a drive root ("D:\") has none, so it shows as its path.</summary>
    public static string DisplayName(string folder)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return name.Length > 0 ? name : folder;
    }
}
