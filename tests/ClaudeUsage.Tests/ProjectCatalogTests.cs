using ClaudeUsage.Projects;

namespace ClaudeUsage.Tests;

public sealed class ProjectCatalogTests
{
    [Fact]
    public void Starts_scanning_and_BeginScan_returns_to_scanning_after_a_result()
    {
        var catalog = new ProjectCatalog(@"D:\");
        int changes = 0;
        catalog.Changed += (_, _) => changes++;

        Assert.True(catalog.IsScanning);
        Assert.Equal(@"Scanning D:\ for CLAUDE.md…", catalog.EmptyText);

        catalog.SetProjects([new ClaudeProject("Othello", @"D:\Git\Othello")]);
        Assert.False(catalog.IsScanning);

        catalog.BeginScan();
        Assert.True(catalog.IsScanning);
        Assert.Null(catalog.Projects);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void BeginScan_with_a_root_switches_root_and_drops_the_list()
    {
        var catalog = new ProjectCatalog(@"D:\");
        catalog.SetProjects([new ClaudeProject("Othello", @"D:\Git\Othello")]);
        int changes = 0;
        catalog.Changed += (_, _) => changes++;

        catalog.BeginScan(@"E:\Code");

        Assert.Equal(@"E:\Code", catalog.Root);
        Assert.True(catalog.IsScanning);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void EmptyText_says_nothing_was_found_after_an_empty_scan()
    {
        var catalog = new ProjectCatalog(@"E:\Code");

        catalog.SetProjects([]);

        Assert.Equal(@"No CLAUDE.md found under E:\Code", catalog.EmptyText);
    }
}
