using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Drives the Left Pane's active-environment badge (LeftPane.md §4.1): the active workspace's list
/// of <see cref="WorkspaceEnvironment"/>s and whichever one is currently active - the source
/// <c>IVariableResolver</c> resolves every <c>{{variable}}</c> token against. The active selection
/// is persisted to the workspace's <c>fubar.json</c> (<see cref="AppManifest.ActiveEnvironmentId"/>)
/// so it survives across sessions.
/// </summary>
public partial class EnvironmentManagerViewModel : ViewModelBase
{
    private readonly IEnvironmentStore _environmentStore;
    private readonly IWorkspaceStore _workspaceStore;
    private bool _suppressPersist;

    public EnvironmentManagerViewModel(IEnvironmentStore environmentStore, IWorkspaceStore workspaceStore)
    {
        _environmentStore = environmentStore;
        _workspaceStore = workspaceStore;
    }

    [ObservableProperty]
    public partial bool HasMissingSecrets { get; set; }

    /// <summary>Left Pane header's "Secrets" toggle (LeftPane.md §4.1): when true, the Universal
    /// Variable Tooltip system (RequestEditorPane.md §4) shows a secret variable's real resolved
    /// value on hover instead of masking it.</summary>
    [ObservableProperty]
    public partial bool SecretsRevealed { get; set; }

    public ObservableCollection<WorkspaceEnvironment> Environments { get; } = [];

    [ObservableProperty]
    public partial WorkspaceEnvironment? ActiveEnvironment { get; set; }

    /// <summary>The workspace <see cref="Environments"/>/<see cref="ActiveEnvironment"/> currently describe.</summary>
    public Workspace? ActiveWorkspace { get; private set; }

    partial void OnActiveEnvironmentChanged(WorkspaceEnvironment? value)
    {
        if (!_suppressPersist && ActiveWorkspace is { } workspace)
        {
            _ = PersistActiveEnvironmentAsync(workspace, value?.Id);
        }
    }

    /// <summary>(Re)loads <paramref name="workspace"/>'s environments from disk and restores its
    /// persisted <see cref="AppManifest.ActiveEnvironmentId"/>, or the first environment if none was saved.</summary>
    public async Task LoadForWorkspaceAsync(Workspace workspace)
    {
        ActiveWorkspace = workspace;

        var loaded = await _environmentStore.LoadEnvironmentsAsync(workspace.RootPath);
        Environments.Clear();
        foreach (var environment in loaded)
        {
            Environments.Add(environment);
        }

        // Setting ActiveEnvironment here is a restore, not a user choice - don't re-persist it
        // straight back (that's harmless but pointless disk I/O on every request open).
        _suppressPersist = true;
        ActiveEnvironment = Environments.FirstOrDefault(e => e.Id == workspace.Manifest.ActiveEnvironmentId)
            ?? Environments.FirstOrDefault();
        _suppressPersist = false;
    }

    /// <summary>Called when the last workspace tab closes - nothing left to resolve variables against.</summary>
    public void ClearWorkspace()
    {
        ActiveWorkspace = null;
        Environments.Clear();

        _suppressPersist = true;
        ActiveEnvironment = null;
        _suppressPersist = false;
    }

    private async Task PersistActiveEnvironmentAsync(Workspace workspace, string? environmentId)
    {
        workspace.Manifest.ActiveEnvironmentId = environmentId;
        await _workspaceStore.SaveAppManifestAsync(workspace.RootPath, workspace.Manifest);
    }
}
