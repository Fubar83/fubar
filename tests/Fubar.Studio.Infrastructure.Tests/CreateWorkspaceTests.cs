using Fubar.Studio.Infrastructure.Workspaces;

namespace Fubar.Studio.Infrastructure.Tests;

/// <summary>
/// Setting an empty folder up as a workspace.
///
/// This used to live inside the "New Workspace" click handler, where it could not be tested at all -
/// and where nothing invoked it either, because the command was never bound to anything in the UI.
/// The whole feature existed and was unreachable, which is the trap the codebase already warns about
/// for Core options that no view model reads.
/// </summary>
public class CreateWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fubar-new-workspace-" + Guid.NewGuid().ToString("n"));

    private static WorkspaceService Service() => new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task An_empty_folder_becomes_a_workspace_that_can_be_opened()
    {
        Directory.CreateDirectory(_root);

        var workspace = await Service().CreateWorkspaceAsync(_root, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_root, "fubar.json")));
        Assert.True(Service().IsWorkspaceRoot(_root));
        Assert.Equal(Path.GetFileName(_root), workspace.Manifest.Name);
    }

    [Fact]
    public async Task The_folder_does_not_have_to_exist_yet()
    {
        // The OS folder picker can be pointed at a path the user has just typed.
        var workspace = await Service().CreateWorkspaceAsync(_root, CancellationToken.None);

        Assert.True(Directory.Exists(_root));
        Assert.NotNull(workspace);
    }

    [Fact]
    public async Task Both_collections_and_environments_are_laid_out()
    {
        // Saving either kind of file would create its own folder on demand, so this is not
        // load-bearing - it is what makes "these are ordinary files you can commit" legible before
        // the first save rather than after it.
        await Service().CreateWorkspaceAsync(_root, CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(_root, "collections")));
        Assert.True(Directory.Exists(Path.Combine(_root, "environments")));
    }

    [Fact]
    public async Task History_is_kept_out_of_version_control()
    {
        await Service().CreateWorkspaceAsync(_root, CancellationToken.None);

        var ignore = await File.ReadAllTextAsync(Path.Combine(_root, ".gitignore"), CancellationToken.None);

        Assert.Contains(".fubar/", ignore);
    }

    [Fact]
    public async Task An_existing_workspace_is_opened_rather_than_reinitialised()
    {
        // The commonest way to reach this is browsing to the wrong folder. Rewriting someone's
        // manifest because of a misclick is unrecoverable in a way that opening the wrong workspace
        // is not.
        Directory.CreateDirectory(_root);
        await Service().CreateWorkspaceAsync(_root, CancellationToken.None);

        var manifestPath = Path.Combine(_root, "fubar.json");
        var original = await File.ReadAllTextAsync(manifestPath, CancellationToken.None);

        await File.WriteAllTextAsync(manifestPath, original.Replace(Path.GetFileName(_root), "Renamed"), CancellationToken.None);

        var reopened = await Service().CreateWorkspaceAsync(_root, CancellationToken.None);

        Assert.Equal("Renamed", reopened.Manifest.Name);
    }

    [Fact]
    public async Task An_existing_gitignore_is_left_alone()
    {
        // It may already say things about this repository that have nothing to do with us.
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, ".gitignore"), "node_modules/\n", CancellationToken.None);

        await Service().CreateWorkspaceAsync(_root, CancellationToken.None);

        var ignore = await File.ReadAllTextAsync(Path.Combine(_root, ".gitignore"), CancellationToken.None);

        Assert.Equal("node_modules/\n", ignore);
    }

    [Fact]
    public async Task A_new_workspace_is_ready_for_collections_and_environments_rather_than_holding_any()
    {
        var workspace = await Service().CreateWorkspaceAsync(_root, CancellationToken.None);

        Assert.Equal(_root, workspace.RootPath);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_root, "collections")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_root, "environments")));
    }
}
