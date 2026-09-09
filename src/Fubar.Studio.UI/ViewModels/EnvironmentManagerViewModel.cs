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

        // Suppressed across the WHOLE reload, not only the assignment at the end. Emptying the bound
        // collection makes the ComboBox's selection model write null straight back through the
        // two-way binding, and that arrived here looking exactly like a user choosing "no
        // environment" - so `activeEnvironmentId` was erased from fubar.json every time this ran,
        // which is on every workspace activation and every time the open request changes workspace.
        // The next open then fell back to whichever environment sorted first: a workspace saved on
        // Staging quietly came back up on Production, with the picker agreeing. Restoring a
        // selection is not a choice, and neither is a collection being refilled underneath one.
        _suppressPersist = true;
        try
        {
            Environments.Clear();
            foreach (var environment in loaded)
            {
                Environments.Add(environment);
            }

            ActiveEnvironment = Environments.FirstOrDefault(e => e.Id == workspace.Manifest.ActiveEnvironmentId)
                ?? Environments.FirstOrDefault();
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    /// <summary>Called when the last workspace tab closes - nothing left to resolve variables against.</summary>
    public void ClearWorkspace()
    {
        // Same order as the reload above, and for the same reason: the flag goes up before the
        // collection is touched. Nulling ActiveWorkspace first happens to make the write-back
        // harmless here, but relying on that is one reordering away from persisting a null again.
        _suppressPersist = true;
        try
        {
            ActiveWorkspace = null;
            Environments.Clear();
            ActiveEnvironment = null;
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    private async Task PersistActiveEnvironmentAsync(Workspace workspace, string? environmentId)
    {
        workspace.Manifest.ActiveEnvironmentId = environmentId;
        await _workspaceStore.SaveAppManifestAsync(workspace.RootPath, workspace.Manifest);
    }
}
