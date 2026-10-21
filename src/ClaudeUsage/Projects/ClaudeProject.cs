namespace ClaudeUsage.Projects;

/// <summary>A folder that holds a CLAUDE.md file.</summary>
/// <param name="Name">The folder's own name, shown in the menus (e.g. "Othello" for D:\Git\Othello).</param>
/// <param name="Directory">Full path of the folder.</param>
public sealed record ClaudeProject(string Name, string Directory);
