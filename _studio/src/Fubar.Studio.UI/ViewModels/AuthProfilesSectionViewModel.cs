using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the Left Pane's "Auth Profiles" group (LeftPane.md §4.1): always visible once a workspace
/// is open, even with zero profiles, with a "+" to create one. Profiles live together in one
/// <c>auth-profiles.json</c> array (unlike per-file environments), so every mutation re-reads and
/// re-writes the whole list.
/// </summary>
public sealed partial class AuthProfilesSectionViewModel : ViewModelBase
{
    private readonly IAuthProfileStore _workspaceService;
    private readonly StatusLogViewModel _statusLog;
    private Workspace? _workspace;
    private List<AuthProfile> _profiles = [];

    public AuthProfilesSectionViewModel(IAuthProfileStore workspaceService, StatusLogViewModel statusLog)
    {
        _workspaceService = workspaceService;
        _statusLog = statusLog;

        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRows));
    }

    public ObservableCollection<AuthProfileRowViewModel> Rows { get; } = [];

    /// <summary>Whether the group has any profiles - the Left Pane shows an empty-state message
    /// instead of the list when false, but the group header itself always renders regardless.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>The profile currently open in the main canvas, if any - see
    /// <see cref="EnvironmentsSectionViewModel.SelectedEnvironmentId"/> for why this lives here
    /// rather than on the row itself.</summary>
    [ObservableProperty]
    public partial string? SelectedProfileId { get; set; }

    /// <summary>Raised when a row is chosen for editing - MainViewModel opens an
    /// <c>AuthProfileEditorViewModel</c> for it in the main canvas.</summary>
    public event Action<AuthProfile>? EditRequested;

    /// <summary>Called by MainViewModel whenever the active workspace changes (or closes).</summary>
    public async Task SetWorkspaceAsync(Workspace? workspace)
    {
        _workspace = workspace;
        _profiles = workspace is null ? [] : [.. await _workspaceService.LoadAuthProfilesAsync(workspace.RootPath)];
        Rebuild();
    }

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var profile in _profiles)
        {
            Rows.Add(new AuthProfileRowViewModel(profile));
        }
    }

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        if (_workspace is not { } workspace)
        {
            _statusLog.Log("Open a workspace before creating an auth profile.");
            return;
        }

        var profile = new AuthProfile { Name = "New Auth Profile", Config = new() { Type = Fubar.Studio.Core.Models.AuthType.Bearer } };
        _profiles.Add(profile);
        await _workspaceService.SaveAuthProfilesAsync(workspace.RootPath, _profiles);
        Rebuild();

        var row = Rows.FirstOrDefault(r => r.Model.Id == profile.Id);
        if (row is not null)
        {
            row.EditName = row.Name;
            row.IsEditing = true;
        }

        _statusLog.Log($"Created auth profile \"{profile.Name}\".");
    }

    [RelayCommand]
    private void BeginRename(AuthProfileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.EditName = row.Name;
        row.IsEditing = true;
    }

    [RelayCommand]
    private async Task CommitRenameAsync(AuthProfileRowViewModel? row)
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
        row.Name = newName;
        await _workspaceService.SaveAuthProfilesAsync(workspace.RootPath, _profiles);
        _statusLog.Log($"Renamed auth profile to \"{newName}\".");
    }

    [RelayCommand]
    private void CancelRename(AuthProfileRowViewModel? row)
    {
        if (row is not null)
        {
            row.IsEditing = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(AuthProfileRowViewModel? row)
    {
        if (row is null || _workspace is not { } workspace)
        {
            return;
        }

        _profiles.RemoveAll(p => p.Id == row.Model.Id);
        await _workspaceService.SaveAuthProfilesAsync(workspace.RootPath, _profiles);
        Rebuild();
        _statusLog.Log($"Deleted auth profile \"{row.Name}\".");
    }

    [RelayCommand]
    private void Edit(AuthProfileRowViewModel? row)
    {
        if (row is not null)
        {
            EditRequested?.Invoke(row.Model);
        }
    }
}
