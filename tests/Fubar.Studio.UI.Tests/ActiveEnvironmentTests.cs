using System.Collections.Specialized;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// Which environment is active, and what is allowed to change it.
///
/// <para>The picker is a two-way bound ComboBox, so its selection model writes back whenever the
/// collection under it changes - and a reload empties that collection before refilling it. That
/// write-back is not a user choosing anything, and treating it as one erased the workspace's saved
/// environment on every activation.</para>
/// </summary>
public class ActiveEnvironmentTests
{
    private sealed class FakeEnvironments(params WorkspaceEnvironment[] environments) : IEnvironmentStore
    {
        public Task<IReadOnlyList<WorkspaceEnvironment>> LoadEnvironmentsAsync(
            string rootPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceEnvironment>>(environments);

        public Task SaveEnvironmentAsync(string rootPath, WorkspaceEnvironment environment, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteEnvironmentAsync(string rootPath, string environmentId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingWorkspaceStore : IWorkspaceStore
    {
        public List<string?> Saved { get; } = [];

        public Task SaveAppManifestAsync(string rootPath, AppManifest manifest, CancellationToken cancellationToken = default)
        {
            Saved.Add(manifest.ActiveEnvironmentId);
            return Task.CompletedTask;
        }

        public bool IsWorkspaceRoot(string directoryPath) => true;

        public Task<Workspace> LoadWorkspaceAsync(string rootPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Workspace> CreateWorkspaceAsync(string rootPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static readonly WorkspaceEnvironment Production = new() { Id = "prd", Name = "Production" };
    private static readonly WorkspaceEnvironment Staging = new() { Id = "stg", Name = "Staging" };

    private static Workspace WorkspaceOn(string? activeId) => new()
    {
        RootPath = "/w",
        Manifest = new AppManifest { Name = "demo", ActiveEnvironmentId = activeId },
    };

    /// <summary>Stands in for the bound ComboBox: when the source collection empties under it, its
    /// selection model pushes null back into the view model. Nothing else in a test rig does this,
    /// which is exactly why the bug survived a suite that otherwise covered this class.</summary>
    private static void AttachPickerWriteBack(EnvironmentManagerViewModel manager) =>
        manager.Environments.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset || manager.Environments.Count == 0)
            {
                manager.ActiveEnvironment = null;
            }
        };

    [Fact]
    public async Task Reloading_a_workspace_does_not_erase_its_saved_environment()
    {
        // TWO loads, because the first one has nothing selected yet to lose: the damage happens on
        // the SECOND, which is every workspace activation and every time the open request changes
        // workspace - so "open a request" was enough, and the next open of the workspace came up on
        // whichever environment sorted first.
        var store = new RecordingWorkspaceStore();
        var manager = new EnvironmentManagerViewModel(new FakeEnvironments(Production, Staging), store);
        AttachPickerWriteBack(manager);

        var workspace = WorkspaceOn("stg");
        await manager.LoadForWorkspaceAsync(workspace);
        await manager.LoadForWorkspaceAsync(workspace);

        Assert.Equal("stg", workspace.Manifest.ActiveEnvironmentId);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task Reactivating_a_workspace_leaves_the_picker_where_it_was()
    {
        // The other half of the same bug, and the half that costs something: the erased manifest was
        // then read back by the very same reload, so the selection itself fell to the first
        // environment. Sending to Production while the picker said Staging is one click away from
        // there.
        var manager = new EnvironmentManagerViewModel(
            new FakeEnvironments(Production, Staging), new RecordingWorkspaceStore());
        AttachPickerWriteBack(manager);

        var workspace = WorkspaceOn("stg");
        await manager.LoadForWorkspaceAsync(workspace);
        await manager.LoadForWorkspaceAsync(workspace);

        Assert.Equal("Staging", manager.ActiveEnvironment?.Name);
    }

    [Fact]
    public async Task The_saved_environment_is_the_one_selected_not_the_first_one()
    {
        var manager = new EnvironmentManagerViewModel(
            new FakeEnvironments(Production, Staging), new RecordingWorkspaceStore());
        AttachPickerWriteBack(manager);

        await manager.LoadForWorkspaceAsync(WorkspaceOn("stg"));

        Assert.Equal("Staging", manager.ActiveEnvironment?.Name);
    }

    [Fact]
    public async Task A_workspace_with_nothing_saved_falls_back_to_the_first_environment()
    {
        var manager = new EnvironmentManagerViewModel(
            new FakeEnvironments(Production, Staging), new RecordingWorkspaceStore());

        await manager.LoadForWorkspaceAsync(WorkspaceOn(null));

        Assert.Equal("Production", manager.ActiveEnvironment?.Name);
    }

    [Fact]
    public async Task Choosing_an_environment_still_persists_it()
    {
        // The suppression must not swallow the real thing it was guarding against noise from.
        var store = new RecordingWorkspaceStore();
        var manager = new EnvironmentManagerViewModel(new FakeEnvironments(Production, Staging), store);

        var workspace = WorkspaceOn("prd");
        await manager.LoadForWorkspaceAsync(workspace);

        manager.ActiveEnvironment = manager.Environments.Single(e => e.Name == "Staging");

        Assert.Equal(["stg"], store.Saved);
        Assert.Equal("stg", workspace.Manifest.ActiveEnvironmentId);
    }

    [Fact]
    public async Task Closing_the_last_workspace_persists_nothing()
    {
        var store = new RecordingWorkspaceStore();
        var manager = new EnvironmentManagerViewModel(new FakeEnvironments(Production, Staging), store);
        AttachPickerWriteBack(manager);

        await manager.LoadForWorkspaceAsync(WorkspaceOn("stg"));
        manager.ClearWorkspace();

        Assert.Empty(store.Saved);
        Assert.Null(manager.ActiveEnvironment);
    }
}
