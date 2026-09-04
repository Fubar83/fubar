using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Secrets;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Opened in the main canvas (in place of the Request Editor) when a Left Pane Environments row is
/// clicked for editing. A variable's <see cref="VariableKind"/> decides where its value lives: Normal in
/// the environment JSON, Secret in the OS keyring (<see cref="ISecretStoreService"/>), Session in the
/// in-memory <see cref="ISessionVariableStore"/>. Secret/Session values are never written to disk (see
/// <see cref="AppVariable"/>'s doc comment); this editor reads/writes the real value transparently.
/// </summary>
public partial class EnvironmentEditorViewModel : ViewModelBase
{
    private readonly Workspace _workspace;
    private readonly IEnvironmentStore _workspaceService;
    private readonly ISecretStoreService _secretStore;
    private readonly ISessionVariableStore _sessionStore;
    private readonly IVariableWriter _variableWriter;
    private readonly StatusLogViewModel _statusLog;
    private readonly string _environmentId;

    // Secret variables present when the editor opened, so we can delete their keyring entry if the user
    // changes a variable away from Secret (otherwise the old secret would be orphaned in the vault).
    private readonly HashSet<string> _originalSecretKeys = new(StringComparer.Ordinal);

    public EnvironmentEditorViewModel(
        WorkspaceEnvironment environment,
        Workspace workspace,
        IEnvironmentStore workspaceService,
        ISecretStoreService secretStore,
        ISessionVariableStore sessionStore,
        IVariableWriter variableWriter,
        StatusLogViewModel statusLog)
    {
        _workspace = workspace;
        _workspaceService = workspaceService;
        _secretStore = secretStore;
        _sessionStore = sessionStore;
        _variableWriter = variableWriter;
        _statusLog = statusLog;
        _environmentId = environment.Id;

        Name = environment.Name;

        foreach (var variable in environment.Variables)
        {
            var value = variable.Kind switch
            {
                VariableKind.Secret => _secretStore.TryGetSecret(workspace.WorkspaceId, variable.Key) ?? "",
                VariableKind.Session => _sessionStore.Get(SessionScope.For(workspace, _environmentId), variable.Key) ?? "",
                _ => variable.Value ?? "",
            };

            if (variable.Kind == VariableKind.Secret)
            {
                _originalSecretKeys.Add(variable.Key);
            }

            Rows.Add(new EnvironmentVariableRowViewModel
            {
                Key = variable.Key,
                Value = value,
                Kind = variable.Kind,
                Description = variable.Description ?? "",
            });
        }
    }

    /// <summary>Used by MainViewModel to highlight this environment's row in the Left Pane while
    /// it's the active canvas surface.</summary>
    public string EnvironmentId => _environmentId;

    /// <summary>The workspace this environment belongs to - used by MainViewModel to clear the
    /// main canvas when that workspace's tab is closed.</summary>
    public Workspace Workspace => _workspace;

    [ObservableProperty]
    public partial string Name { get; set; }

    public ObservableCollection<EnvironmentVariableRowViewModel> Rows { get; } = [];

    /// <summary>Raised after a successful Save - the Left Pane's Environments section refreshes from it.</summary>
    public event Action? Saved;

    [RelayCommand]
    private void AddRow() => Rows.Add(new EnvironmentVariableRowViewModel());

    [RelayCommand]
    private void RemoveRow(EnvironmentVariableRowViewModel? row)
    {
        if (row is not null)
        {
            Rows.Remove(row);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Built empty and filled through IVariableWriter, so this editor and the CAPTURE path share one
        // implementation of "where may this value live" rather than two that happen to agree. The
        // editor is the only place a variable's Kind can CHANGE, so it declares each row's kind first
        // and lets the writer place the value.
        var model = new WorkspaceEnvironment { Id = _environmentId, Name = Name, Variables = [] };
        var stillSecret = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in Rows.Where(r => !string.IsNullOrWhiteSpace(r.Key)))
        {
            model.Variables.Add(new AppVariable
            {
                Key = row.Key,
                Kind = row.Kind,
                Description = string.IsNullOrEmpty(row.Description) ? null : row.Description,
            });

            _variableWriter.Write(_workspace, model, row.Key, row.Value, row.Kind);

            if (row.Kind == VariableKind.Secret)
            {
                stillSecret.Add(row.Key);
            }
        }

        // Drop keyring entries for variables that used to be Secret but no longer are (same key), so a
        // stale secret isn't left behind in the vault.
        foreach (var orphan in _originalSecretKeys.Where(k => !stillSecret.Contains(k)))
        {
            _secretStore.DeleteSecret(_workspace.WorkspaceId, orphan);
        }

        try
        {
            await _workspaceService.SaveEnvironmentAsync(_workspace.RootPath, model);
            _originalSecretKeys.Clear();
            _originalSecretKeys.UnionWith(stillSecret);
            _statusLog.Log($"Saved environment \"{Name}\".");
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Failed to save environment \"{Name}\": {ex.Message}");
        }
    }
}
