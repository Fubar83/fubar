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

    /// <summary>
    /// An existing .gitignore is APPENDED to, never rewritten - it may already say things about this
    /// repository that have nothing to do with us, and it is in the user's history.
    ///
    /// <para>This test used to assert the file came back byte-identical, and that assertion WAS the
    /// leak: the rule was written only when no .gitignore existed, so the commonest case - a workspace
    /// inside a repository that already has one - left execution history tracked. What it was really
    /// protecting is that we do not clobber the user's rules, which is what it checks now.</para>
    /// </summary>
    [Fact]
    public async Task An_existing_gitignore_keeps_everything_it_already_said()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, ".gitignore"), "node_modules/\n", CancellationToken.None);

        await Service().CreateWorkspaceAsync(_root, CancellationToken.None);

        var ignore = await File.ReadAllTextAsync(Path.Combine(_root, ".gitignore"), CancellationToken.None);

        Assert.StartsWith("node_modules/\n", ignore, StringComparison.Ordinal);
        Assert.Contains(".fubar/", ignore, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_workspace_is_ready_for_collections_and_environments_rather_than_holding_any()
    {
        var workspace = await Service().CreateWorkspaceAsync(_root, CancellationToken.None);

        Assert.Equal(_root, workspace.RootPath);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_root, "collections")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_root, "environments")));
    }

    /// <summary>
    /// The case that leaked. The rule used to be written only when NO .gitignore existed, so aiming
    /// New Workspace at a repository you already have - the documented way to use this - left
    /// execution history, response bodies included, tracked by Git.
    /// </summary>
    [Fact]
    public async Task Existing_gitignore_gains_the_fubar_rule()
    {
        Directory.CreateDirectory(_root);
        var gitignore = Path.Combine(_root, ".gitignore");
        await File.WriteAllTextAsync(gitignore, "bin/\nobj/\n");

        await Service().CreateWorkspaceAsync(_root);

        var lines = await File.ReadAllLinesAsync(gitignore);
        Assert.Contains(".fubar/", lines);
        // The user's own rules are appended to, never rewritten.
        Assert.Contains("bin/", lines);
        Assert.Contains("obj/", lines);
    }

    /// <summary>Opening a workspace repeatedly must not append the rule again each time.</summary>
    [Fact]
    public async Task The_fubar_rule_is_not_duplicated_on_reopen()
    {
        var workspace = await Service().CreateWorkspaceAsync(_root);
        await Service().LoadWorkspaceAsync(workspace.RootPath);
        await Service().LoadWorkspaceAsync(workspace.RootPath);

        var lines = await File.ReadAllLinesAsync(Path.Combine(_root, ".gitignore"));
        Assert.Single(lines, line => line.Trim() == ".fubar/");
    }

    /// <summary>
    /// A workspace that is not itself the repository root gets no protection from a rule written into
    /// its own .gitignore, so the history directory excludes itself as well.
    /// </summary>
    [Fact]
    public async Task History_writes_a_gitignore_that_excludes_itself()
    {
        var workspace = await Service().CreateWorkspaceAsync(_root);
        var history = new Fubar.Studio.Infrastructure.History.HistoryService();

        await history.AppendAsync(workspace.RootPath, "req-1", new Fubar.Studio.Core.Models.ExecutionSnapshot());

        Assert.Equal(
            ["# Execution history: local to this machine, never committed.", "*"],
            (await File.ReadAllLinesAsync(Path.Combine(_root, ".fubar", ".gitignore"))).Where(l => l.Length > 0));
    }

    /// <summary>An existing rule spelled another legal way is recognised rather than duplicated.</summary>
    [Theory]
    [InlineData(".fubar")]
    [InlineData("/.fubar/")]
    [InlineData(".fubar/**")]
    public async Task An_equivalent_existing_rule_is_left_alone(string existingRule)
    {
        Directory.CreateDirectory(_root);
        var gitignore = Path.Combine(_root, ".gitignore");
        await File.WriteAllTextAsync(gitignore, existingRule + "\n");

        await Service().CreateWorkspaceAsync(_root);

        Assert.Equal([existingRule], (await File.ReadAllLinesAsync(gitignore)).Where(l => l.Length > 0));
    }
}
