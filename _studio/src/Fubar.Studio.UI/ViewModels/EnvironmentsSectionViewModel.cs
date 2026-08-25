using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the Left Pane's "Environments" group (LeftPane.md §4.1): always visible once a workspace
/// is open, even with zero environments, with a "+" to create one. Delegates the resolution-facing
/// list (<see cref="EnvironmentManagerViewModel.Environments"/>, used by the executor/resolver) to
/// <see cref="EnvironmentManagerViewModel"/> and just re-reads it after every mutation, rather than
/// keeping a second independent copy of workspace environment state.
/// </summary>
public sealed partial class EnvironmentsSectionViewModel : ViewModelBase
{
    private readonly IEnvironmentStore _workspaceService;
    private readonly EnvironmentManagerViewModel _environmentManager;
    private readonly StatusLogViewModel _statusLog;
    private Workspace? _workspace;

    public EnvironmentsSectionViewModel(IEnvironmentStore workspaceService, EnvironmentManagerViewModel environmentManager, StatusLogViewModel statusLog)
    {
        _workspaceService = workspaceService;
        _environmentManager = environmentManager;
        _statusLog = statusLog;

        _environmentManager.Environments.CollectionChanged += (_, _) => Rebuild();
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRows));
    }

    public ObservableCollection<EnvironmentRowViewModel> Rows { get; } = [];

    /// <summary>Whether the group has any environments - the Left Pane shows an empty-state message
    /// instead of the list when false, but the group header itself always renders regardless.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>The environment currently open in the main canvas, if any - MainViewModel keeps this
    /// in sync with <c>ActiveEditor</c> so the matching row can highlight itself (and so opening a
    /// request/auth profile elsewhere clears it). Compared by Id since <see cref="Rebuild"/> replaces
    /// every <see cref="EnvironmentRowViewModel"/> instance on every reload.</summary>
    [ObservableProperty]
    public partial string? SelectedEnvironmentId { get; set; }

    /// <summary>Raised when a row is chosen for editing (its variables) - MainViewModel opens an
    /// <c>EnvironmentEditorViewModel</c> for it in the main canvas.</summary>
    public event Action<WorkspaceEnvironment>? EditRequested;

    /// <summary>Called by MainViewModel whenever the active workspace changes (or closes).</summary>
    public void SetWorkspace(Workspace? workspace)
    {
        _workspace = workspace;
        Rebuild();
    }

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var environment in _environmentManager.Environments)
        {
            Rows.Add(new EnvironmentRowViewModel(environment));
        }
    }

    [RelayCommand]
    private async Task NewEnvironmentAsync()
    {
        if (_workspace is not { } workspace)
        {
            _statusLog.Log("Open a workspace before creating an environment.");
            return;
        }

        var environment = new WorkspaceEnvironment { Name = "New Environment" };
        await _workspaceService.SaveEnvironmentAsync(workspace.RootPath, environment);
        await _environmentManager.LoadForWorkspaceAsync(workspace);

        var row = Rows.FirstOrDefault(r => r.Model.Id == environment.Id);
        if (row is not null)
        {
            row.EditName = row.Name;
            row.IsEditing = true;
        }

        _statusLog.Log($"Created environment \"{environment.Name}\".");
    }

    [RelayCommand]
    private void BeginRename(EnvironmentRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.EditName = row.Name;
        row.IsEditing = true;
    }

    [RelayCommand]
    private async Task CommitRenameAsync(EnvironmentRowViewModel? row)
    {
        if (row is null || !row.IsEditing)
        {
            return;
        }

        row.IsEditing = false;
        var newName = row.EditName.Trim();
        if (string.IsNullOrEmpty(newName) || newName == row.Name || _workspace is not { } workspace)
        {
            return;
        }

        row.Model.Name = newName;
        await _workspaceService.SaveEnvironmentAsync(workspace.RootPath, row.Model);
        await _environmentManager.LoadForWorkspaceAsync(workspace);
        _statusLog.Log($"Renamed environment to \"{newName}\".");
    }

    [RelayCommand]
    private void CancelRename(EnvironmentRowViewModel? row)
    {
        if (row is not null)
        {
            row.IsEditing = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(EnvironmentRowViewModel? row)
    {
        if (row is null || _workspace is not { } workspace)
        {
            return;
        }

        await _workspaceService.DeleteEnvironmentAsync(workspace.RootPath, row.Model.Id);
        await _environmentManager.LoadForWorkspaceAsync(workspace);
        _statusLog.Log($"Deleted environment \"{row.Name}\".");
    }

    [RelayCommand]
    private void Edit(EnvironmentRowViewModel? row)
    {
        if (row is not null)
        {
            EditRequested?.Invoke(row.Model);
        }
    }
}
