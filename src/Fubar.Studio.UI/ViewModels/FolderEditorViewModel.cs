using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Application.Comparison;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// One <c>_folder.json</c> in the main canvas (docs/spec-endpoints.md §9.3).
/// </summary>
/// <remarks>
/// <para>A folder is the level almost every shared rule belongs at - a folder of endpoints from one
/// service shares the same noise, the same auth and the same headers - and it was the last level with
/// no editor at all. Its file was written by the comparison window's "save to folder" and otherwise
/// edited by hand, which is why the Rules tab existed for an endpoint and a case but not for the
/// place most rules should have gone.</para>
/// <para>The same tab vocabulary as an endpoint, minus the ones a folder does not have: it carries no
/// variables and nothing to send, so <c>Headers · Auth · Rules</c> is the whole of it.</para>
/// </remarks>
public sealed partial class FolderEditorViewModel : ViewModelBase, ISaveableEditor
{
    private readonly IFolderConfigStore _folders;
    private readonly StatusLogViewModel _statusLog;
    private readonly FolderConfig _config;

    public FolderEditorViewModel(
        FolderConfig config,
        string folderPath,
        Workspace workspace,
        IReadOnlyList<AuthProfile> authProfiles,
        IFolderConfigStore folders,
        IRequestComparisonSettings comparisonSettings,
        StatusLogViewModel statusLog)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(workspace);

        _config = config;
        _folders = folders;
        _statusLog = statusLog;

        FolderPath = folderPath;
        Workspace = workspace;
        Name = System.IO.Path.GetFileName(folderPath);

        Headers = new KeyValueGridViewModel(config.Headers);
        Headers.Changed += MarkDirty;

        // "Inherit" first and selected by default: a folder that says nothing about auth is the
        // common case, and a picker whose first entry was a real profile would make silence look like
        // a choice.
        AuthProfiles = [InheritAuth, .. authProfiles];
        SelectedAuthProfile = authProfiles.FirstOrDefault(p => p.Id == config.AuthProfileId) ?? InheritAuth;

        // A folder is a level of the chain like any other, so the tab is the same one. It needs the
        // NAME as well as the scope: every folder above shares ComparisonScope.Folder, and matching on
        // scope alone would let this screen offer to delete a grandparent's rule as if it were its own.
        Rules = new RulesViewModel(
            new RuleLevel
            {
                Scope = ComparisonScope.Folder,
                SourceName = SourceNameOf(folderPath, workspace),
                LevelName = $"this folder",
                GetComparison = () => _config.Comparison,
                SetComparison = value => _config.Comparison = value,
                GetTolerances = () => _config.Tolerances,
                SetTolerances = value => _config.Tolerances = value,
                GetSnapshot = () => _config.Snapshot,
                SetSnapshot = value => _config.Snapshot = value,
                Changed = MarkDirty,
            },
            workspace,
            folderPath,
            null,
            comparisonSettings,
            statusLog);

        _ = Rules.RefreshAsync();

        // Last: building the grids and picking the current auth profile both raise their own change
        // events, and an editor that opens already claiming unsaved work teaches you to ignore the dot.
        IsDirty = false;
    }

    /// <summary>What the picker calls "no opinion". A real profile in that slot would make a folder
    /// that says nothing about auth look like one that chose something.</summary>
    public static AuthProfile InheritAuth { get; } = new() { Id = "", Name = "Inherit" };

    /// <summary>
    /// How the inheritance chain names this folder's layer, which is what tells its own rules apart
    /// from an ancestor's - see <c>WorkspaceService.GetInheritanceChainAsync</c>, which calls the
    /// collections directory itself "Workspace Root".
    /// </summary>
    private static string SourceNameOf(string folderPath, Workspace workspace)
    {
        var collections = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(workspace.RootPath, "collections"));

        return string.Equals(
            System.IO.Path.GetFullPath(folderPath), collections, StringComparison.OrdinalIgnoreCase)
            ? "Folder: Workspace Root"
            : $"Folder: {System.IO.Path.GetFileName(folderPath)}";
    }

    public string FolderPath { get; }

    public Workspace Workspace { get; }

    public string Name { get; }

    /// <summary>Headers added to every request beneath this folder, unless one of them says
    /// otherwise.</summary>
    public KeyValueGridViewModel Headers { get; }

    public IReadOnlyList<AuthProfile> AuthProfiles { get; }

    /// <summary>Every rule that applies here, and where each came from.</summary>
    public RulesViewModel Rules { get; }

    [ObservableProperty]
    public partial AuthProfile SelectedAuthProfile { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    partial void OnSelectedAuthProfileChanged(AuthProfile value) => MarkDirty();

    private void MarkDirty() => IsDirty = true;

    /// <summary>Raised after a successful save, so the tree and anything reading the chain catch up.</summary>
    public event Action? Saved;

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _config.Headers = Headers.ToModel();
            _config.AuthProfileId = SelectedAuthProfile.Id is { Length: > 0 } id ? id : null;

            await _folders.SaveFolderConfigAsync(FolderPath, _config);

            IsDirty = false;
            _statusLog.Log($"Saved folder settings for \"{Name}\".");
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Could not save \"{Name}\": {ex.Message}");
        }
    }

    Task ISaveableEditor.SaveAsync() => SaveCommand.ExecuteAsync(null);
}
