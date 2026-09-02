using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the Left Pane's request tree plus the title bar's workspace tab strip: one or more open
/// <see cref="WorkspaceRootViewModel"/> roots (each watching its own directory via
/// <c>FileSystemWatcher</c>), exactly one of which is <see cref="ActiveRoot"/> at a time - only its
/// tree/context shows in the Left Pane, matching a browser's "one tab visible at a time" model.
/// Also owns the right-click context menu file operations (Create, Rename, Delete, Duplicate,
/// Reveal in File Manager) and the New Request / New Folder quick actions. Which workspaces are
/// open (and which is active) is persisted via <see cref="IAppSettingsService"/> so
/// <see cref="RestoreLastSessionAsync"/> can reopen them all on the next launch.
/// </summary>
public partial class WorkspaceExplorerViewModel : ViewModelBase, IDisposable
{
    private readonly IRequestStore _requestStore;
    private readonly IWorkspaceStore _workspaceStore;
    private readonly IFolderPickerService _folderPicker;
    private readonly IOpenApiImportService _openApiImport;
    private readonly ICurlImportService _curlImport;
    private readonly IPostmanImportService _postmanImport;
    private readonly IImportDialogService _importDialog;
    private readonly IFilePickerService _filePicker;
    private readonly StatusLogViewModel _statusLog;
    private readonly IAppSettingsService _settingsService;
    private bool _suppressPersist;

    public WorkspaceExplorerViewModel(
        IRequestStore requestStore,
        IWorkspaceStore workspaceStore,
        IFolderPickerService folderPicker,
        IOpenApiImportService openApiImport,
        ICurlImportService curlImport,
        IPostmanImportService postmanImport,
        IImportDialogService importDialog,
        IFilePickerService filePicker,
        StatusLogViewModel statusLog,
        IAppSettingsService settingsService)
    {
        _requestStore = requestStore;
        _workspaceStore = workspaceStore;
        _folderPicker = folderPicker;
        _openApiImport = openApiImport;
        _curlImport = curlImport;
        _postmanImport = postmanImport;
        _importDialog = importDialog;
        _filePicker = filePicker;
        _statusLog = statusLog;
        _settingsService = settingsService;
    }

    public ObservableCollection<WorkspaceRootViewModel> Roots { get; } = [];

    /// <summary>Only the primary window persists/restores the global open-workspace session; windows
    /// that tabs were torn off into are session-only, so they don't clobber the saved set (see
    /// WindowManager). Set once at window creation.</summary>
    public bool PersistsSession { get; set; } = true;

    /// <summary>The single workspace tab currently shown in the Left Pane - null when no workspace is
    /// open at all. Two-way bound to <c>fc:TabStrip.SelectedItem</c> in the title bar, so clicking a tab
    /// sets this and this drives which tab reads as selected.</summary>
    [ObservableProperty]
    public partial WorkspaceRootViewModel? ActiveRoot { get; set; }

    partial void OnActiveRootChanged(WorkspaceRootViewModel? oldValue, WorkspaceRootViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Children.CollectionChanged -= OnActiveRootChildrenChanged;
        }

        if (newValue is not null)
        {
            newValue.Children.CollectionChanged += OnActiveRootChildrenChanged;
        }

        OnPropertyChanged(nameof(HasActiveRootChildren));
        PersistOpenWorkspaces();
    }

    private void OnActiveRootChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(HasActiveRootChildren));

    /// <summary>
    /// Saves which workspaces are open and which is active to <see cref="IAppSettingsService"/> so
    /// <see cref="RestoreLastSessionAsync"/> can reopen this same set next launch. Fires on every
    /// <see cref="ActiveRoot"/> change (covers opening/switching tabs) and explicitly from
    /// <see cref="CloseWorkspace"/> (a close that doesn't happen to touch the active tab wouldn't
    /// otherwise trigger it). Suppressed during <see cref="RestoreLastSessionAsync"/> itself, since
    /// re-saving the exact set we just loaded is pointless churn.
    /// </summary>
    private void PersistOpenWorkspaces()
    {
        if (_suppressPersist || !PersistsSession)
        {
            return;
        }

        var settings = _settingsService.Load();
        settings.OpenWorkspacePaths = Roots.Select(r => r.FullPath).ToList();
        settings.ActiveWorkspacePath = ActiveRoot?.FullPath;
        _ = _settingsService.SaveAsync(settings);
    }

    /// <summary>
    /// Reopens every workspace that was open last session (silently skipping any whose directory
    /// no longer exists or is no longer a valid workspace) and restores whichever one was active.
    /// Called once from <c>App.axaml.cs</c> after the main window is constructed.
    /// </summary>
    public async Task RestoreLastSessionAsync()
    {
        var settings = await _settingsService.LoadAsync();
        if (settings.OpenWorkspacePaths.Count == 0)
        {
            return;
        }

        _suppressPersist = true;
        try
        {
            foreach (var path in settings.OpenWorkspacePaths)
            {
                if (!_workspaceStore.IsWorkspaceRoot(path))
                {
                    continue;
                }

                var workspace = await _workspaceStore.LoadWorkspaceAsync(path);
                Roots.Add(new WorkspaceRootViewModel(workspace, _requestStore));
            }

            ActiveRoot = (settings.ActiveWorkspacePath is not null
                ? Roots.FirstOrDefault(r => string.Equals(r.FullPath, settings.ActiveWorkspacePath, StringComparison.OrdinalIgnoreCase))
                : null) ?? Roots.LastOrDefault();
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    /// <summary>
    /// Removes the open workspace at <paramref name="path"/> from this window WITHOUT disposing it, so
    /// the same <see cref="WorkspaceRootViewModel"/> instance (and its live file-watcher) can be handed
    /// to another window's <see cref="AttachRoot"/> during a cross-window tab drag. Returns the removed
    /// root, or null if it wasn't open here.
    /// </summary>
    public WorkspaceRootViewModel? DetachRoot(string path)
    {
        var root = Roots.FirstOrDefault(r => string.Equals(r.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return null;
        }

        var wasActive = ReferenceEquals(ActiveRoot, root);
        Roots.Remove(root);
        if (wasActive)
        {
            ActiveRoot = Roots.Count > 0 ? Roots[^1] : null;
        }

        PersistOpenWorkspaces();
        WorkspaceClosed?.Invoke(root.Workspace);
        return root;
    }

    /// <summary>
    /// Adopts a <see cref="WorkspaceRootViewModel"/> detached from another window (see
    /// <see cref="DetachRoot"/>) at <paramref name="index"/> in the strip (clamped; -1 appends) and
    /// makes it active. The instance is reused as-is, so nothing reloads.
    /// </summary>
    public void AttachRoot(WorkspaceRootViewModel root, int index)
    {
        if (index >= 0 && index < Roots.Count)
        {
            Roots.Insert(index, root);
        }
        else
        {
            Roots.Add(root);
        }

        ActiveRoot = root;
        PersistOpenWorkspaces();
    }

    /// <summary>Whether the active workspace's REQUESTS group has anything to show - drives the
    /// Left Pane's "No requests yet." empty state, the same way <c>EnvironmentsSectionViewModel.HasRows</c>
    /// and <c>AuthProfilesSectionViewModel.HasRows</c> do for their own groups.</summary>
    public bool HasActiveRootChildren => ActiveRoot is { Children.Count: > 0 };

    [ObservableProperty]
    public partial WorkspaceNodeViewModel? SelectedNode { get; set; }

    /// <summary>Raised when the user closes a workspace tab - MainViewModel clears the main canvas
    /// if it was showing something from that workspace.</summary>
    public event Action<Workspace>? WorkspaceClosed;

    /// <summary>Raised when the user closes the last workspace tab in this window (via the tab close
    /// button, not a tear-off) - the WindowManager closes the window unless it's the only one left.</summary>
    public event Action? WorkspacesEmptied;

    [RelayCommand]
    private void SelectWorkspace(WorkspaceRootViewModel? root)
    {
        if (root is not null)
        {
            ActiveRoot = root;
        }
    }

    [RelayCommand]
    private void CloseWorkspace(WorkspaceRootViewModel? root)
    {
        if (root is null || !Roots.Remove(root))
        {
            return;
        }

        if (ReferenceEquals(ActiveRoot, root))
        {
            // Setting ActiveRoot (even to the same reference, e.g. null->null when Roots is now
            // empty) fires OnActiveRootChanged, which persists - but that path is skipped when the
            // closed tab wasn't active, hence the explicit PersistOpenWorkspaces() below covering
            // that case too.
            ActiveRoot = Roots.Count > 0 ? Roots[^1] : null;
        }

        PersistOpenWorkspaces();
        WorkspaceClosed?.Invoke(root.Workspace);
        root.Dispose();
        _statusLog.Log($"Closed workspace \"{root.Name}\".");

        if (Roots.Count == 0)
        {
            WorkspacesEmptied?.Invoke();
        }
    }

    /// <summary>
    /// Lets the user pick a workspace's <c>fubar.json</c> file directly (rather than its containing
    /// folder) - a folder picker can't show files, so there was no way to actually see which
    /// directory was really a workspace before committing to it.
    /// </summary>
    [RelayCommand]
    private async Task OpenWorkspaceDirectoryAsync()
    {
        var manifestPath = await _folderPicker.PickFileAsync("Open Workspace", "fubar.json");
        if (manifestPath is null)
        {
            return;
        }

        var path = Path.GetDirectoryName(manifestPath)!;

        var existing = Roots.FirstOrDefault(r => string.Equals(r.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveRoot = existing;
            return;
        }

        var workspace = await _workspaceStore.LoadWorkspaceAsync(path);
        var root = new WorkspaceRootViewModel(workspace, _requestStore);
        Roots.Add(root);
        ActiveRoot = root;
        _statusLog.Log($"Opened workspace \"{workspace.Manifest.Name}\" at {path}.");
    }

    /// <summary>
    /// Lets the user pick a directory (creating a new empty one via the OS dialog's own "New
    /// folder" if needed) and initializes it as a workspace: writes a blank fubar.json and a
    /// collections/ folder, then opens it. If the chosen directory is already a workspace, this
    /// just opens it - same as <see cref="OpenWorkspaceDirectoryAsync"/>.
    /// </summary>
    [RelayCommand]
    private async Task NewWorkspaceAsync()
    {
        var path = await _folderPicker.PickFolderAsync("New Workspace Directory");
        if (path is null)
        {
            return;
        }

        var existing = Roots.FirstOrDefault(r => string.Equals(r.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveRoot = existing;
            return;
        }

        var isNew = !_workspaceStore.IsWorkspaceRoot(path);

        // What a new workspace CONSISTS OF is a fact about the format, not a decision for a click
        // handler - see IWorkspaceStore.CreateWorkspaceAsync. It also could not be tested while it
        // lived here.
        var workspace = await _workspaceStore.CreateWorkspaceAsync(path);

        if (isNew)
        {
            _statusLog.Log($"Initialized new workspace \"{workspace.Manifest.Name}\" at {path}.");
        }

        var root = new WorkspaceRootViewModel(workspace, _requestStore);
        Roots.Add(root);
        ActiveRoot = root;
        _statusLog.Log($"Opened workspace \"{workspace.Manifest.Name}\" at {path}.");
    }

    [RelayCommand]
    private void NewRequest()
    {
        var parent = ResolveTargetDirectory();
        if (parent is null)
        {
            _statusLog.Log("Open a workspace before creating a request.");
            return;
        }

        var path = _requestStore.CreateRequest(parent, "New Request");
        _statusLog.Log($"Created request: {path}");
        RefreshRootFor(parent);
    }

    [RelayCommand]
    private void NewFolder()
    {
        var parent = ResolveTargetDirectory();
        if (parent is null)
        {
            _statusLog.Log("Open a workspace before creating a folder.");
            return;
        }

        var path = _requestStore.CreateFolder(parent, "New Folder");
        _statusLog.Log($"Created folder: {path}");
        RefreshRootFor(parent);
    }

    /// <summary>Raised after an OpenAPI import mutates the active workspace on disk, with a freshly
    /// reloaded <see cref="Workspace"/> - MainViewModel re-activates its context so the new
    /// environments / auth profiles / active-environment show up without reopening the workspace.</summary>
    public event Action<Workspace>? WorkspaceContentImported;

    /// <summary>
    /// Imports an OpenAPI 3.x spec into the active workspace: pick a spec file, materialise its
    /// operations as requests (grouped by tag), its servers as environments, and its security schemes
    /// as auth profiles + variables, then refresh the tree and the Environments/Auth Profiles groups.
    /// </summary>
    [RelayCommand]
    private async Task ImportOpenApiAsync()
    {
        if (ActiveRoot is not { } root)
        {
            _statusLog.Log("Open a workspace before importing an OpenAPI spec.");
            return;
        }

        // The dialog does the picking/URL entry + parse + options; it returns null if cancelled.
        var choice = await _importDialog.ShowAsync(root.FullPath);
        if (choice is null)
        {
            return;
        }

        try
        {
            var result = await _openApiImport.ApplyDiffAsync(
                choice.Plan, choice.SelectedRequests, choice.SelectedVariables, choice.Options, root.FullPath);
            _statusLog.Log(
                $"Imported \"{result.ApiTitle}\": {result.CreatedCount} new + {result.UpdatedCount} updated/removed requests, " +
                $"{result.VariableCount} variables, {result.AuthProfileCount} auth profiles.");
            foreach (var warning in result.Warnings)
            {
                _statusLog.Log($"  ⚠ {warning}");
            }

            RefreshRootFor(root.FullPath);

            // Re-read the manifest (import may have set the active environment) and hand the fresh
            // workspace to whoever reloads the Environments / Auth Profiles side panels.
            var reloaded = await _workspaceStore.LoadWorkspaceAsync(root.FullPath);
            root.UpdateWorkspace(reloaded);
            WorkspaceContentImported?.Invoke(reloaded);
        }
        catch (Exception ex)
        {
            _statusLog.Log($"OpenAPI import failed: {ex.Message}");
        }
    }

    /// <summary>Imports a pasted curl command as a single new request at the workspace's collections root.</summary>
    [RelayCommand]
    private async Task ImportCurlAsync()
    {
        if (ActiveRoot is not { } root)
        {
            _statusLog.Log("Open a workspace before importing.");
            return;
        }

        var curl = await _importDialog.ShowCurlAsync();
        if (string.IsNullOrWhiteSpace(curl))
        {
            return;
        }

        try
        {
            var model = _curlImport.Parse(curl);
            var collections = Path.Combine(root.FullPath, "collections");
            Directory.CreateDirectory(collections);
            var path = _requestStore.CreateRequest(collections, model.Name);
            await _requestStore.SaveRequestAsync(path, model);
            _statusLog.Log($"Imported curl request: {model.Method} {model.Url}");
            RefreshRootFor(root.FullPath);
        }
        catch (Exception ex)
        {
            _statusLog.Log($"curl import failed: {ex.Message}");
        }
    }

    /// <summary>Imports a Postman Collection v2.1 JSON file: its folder/request tree plus an environment
    /// built from the collection variables.</summary>
    [RelayCommand]
    private async Task ImportPostmanAsync()
    {
        if (ActiveRoot is not { } root)
        {
            _statusLog.Log("Open a workspace before importing.");
            return;
        }

        var file = await _filePicker.PickOpenFileAsync("Import Postman Collection (v2.1 JSON)");
        if (file is null)
        {
            return;
        }

        try
        {
            var result = await _postmanImport.ImportAsync(file, root.FullPath);
            _statusLog.Log($"Imported Postman collection \"{result.CollectionName}\": " +
                $"{result.RequestCount} requests, {result.FolderCount} folders, {result.VariableCount} variables.");
            foreach (var warning in result.Warnings)
            {
                _statusLog.Log($"  ⚠ {warning}");
            }

            RefreshRootFor(root.FullPath);

            var reloaded = await _workspaceStore.LoadWorkspaceAsync(root.FullPath);
            root.UpdateWorkspace(reloaded);
            WorkspaceContentImported?.Invoke(reloaded);
        }
        catch (Exception ex)
        {
            _statusLog.Log($"Postman import failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void BeginRename()
    {
        if (SelectedNode is not { } node || node is WorkspaceRootViewModel)
        {
            return;
        }

        node.EditName = node.Name;
        node.IsEditing = true;
    }

    [RelayCommand]
    private void CommitRename(WorkspaceNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is null || !node.IsEditing)
        {
            return;
        }

        node.IsEditing = false;
        var newName = node.EditName.Trim();
        if (string.IsNullOrEmpty(newName) || newName == node.Name)
        {
            return;
        }

        try
        {
            var newPath = _requestStore.RenamePath(node.FullPath, newName);
            _statusLog.Log($"Renamed to: {newPath}");
            RefreshRootFor(node.FullPath);
        }
        catch (Exception ex)
        {
            _statusLog.Log($"Rename failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CancelRename(WorkspaceNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is not null)
        {
            node.IsEditing = false;
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedNode is not { } node || node is WorkspaceRootViewModel)
        {
            return;
        }

        try
        {
            _requestStore.DeletePath(node.FullPath);
            _statusLog.Log($"Deleted: {node.FullPath}");
            RefreshRootFor(node.FullPath);
        }
        catch (Exception ex)
        {
            _statusLog.Log($"Delete failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedNode is not { } node || node is WorkspaceRootViewModel)
        {
            return;
        }

        try
        {
            var newPath = _requestStore.DuplicatePath(node.FullPath);
            _statusLog.Log($"Duplicated to: {newPath}");
            RefreshRootFor(node.FullPath);
        }
        catch (Exception ex)
        {
            _statusLog.Log($"Duplicate failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RevealInFileManager()
    {
        if (SelectedNode is { } node)
        {
            FileManagerLauncher.Reveal(node.FullPath);
        }
    }

    /// <summary>Raised when the user double-clicks a request file node - <see cref="MainViewModel"/> wires this to opening a tab.</summary>
    public event Action<string>? RequestFileActivated;

    [RelayCommand]
    private void ActivateSelection()
    {
        if (SelectedNode is { IsDirectory: false } node)
        {
            RequestFileActivated?.Invoke(node.FullPath);
        }
    }

    /// <summary>
    /// The directory a new file/folder should be created in, based on the current selection:
    /// inside the selected directory, alongside the selected file, or the active workspace's
    /// collections root if nothing is selected.
    /// </summary>
    private string? ResolveTargetDirectory() => SelectedNode switch
    {
        { IsDirectory: true } dir => dir.FullPath,
        { IsDirectory: false } file => Path.GetDirectoryName(file.FullPath),
        null when ActiveRoot is not null => Path.Combine(ActiveRoot.FullPath, "collections"),
        _ => null,
    };

    /// <summary>Rescans whichever open workspace root owns <paramref name="path"/>, if any -
    /// used both internally after file operations and by MainViewModel after a request Save
    /// (RequestEditorViewModel.Saved), so the tree's method/auth badges stay current.</summary>
    public void RefreshRootFor(string path)
    {
        var root = Roots.FirstOrDefault(r => path.StartsWith(r.FullPath, StringComparison.OrdinalIgnoreCase));
        root?.Refresh();
    }

    /// <summary>Finds the open workspace that owns <paramref name="path"/> (a file or folder
    /// somewhere under one of <see cref="Roots"/>), or null if it isn't under any open workspace.
    /// Searches every open root, not just <see cref="ActiveRoot"/> - the main canvas can still hold
    /// a request from a workspace whose tab isn't currently selected.</summary>
    public Workspace? FindWorkspaceForPath(string path) =>
        Roots.FirstOrDefault(r => path.StartsWith(r.FullPath, StringComparison.OrdinalIgnoreCase))?.Workspace;

    /// <summary>Finds the tree node for an exact file/folder path across all open workspace roots.</summary>
    public WorkspaceNodeViewModel? FindNodeByPath(string path) =>
        Roots.Select(root => FindNodeRecursive(root, path)).FirstOrDefault(node => node is not null);

    private static WorkspaceNodeViewModel? FindNodeRecursive(WorkspaceNodeViewModel node, string path)
    {
        if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            if (FindNodeRecursive(child, path) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var root in Roots)
        {
            root.Dispose();
        }
    }
}
