using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Core.Workspaces;
using Fubar.Controls;
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
    private readonly IEndpointStore _endpointStore;
    private readonly IBatchStore _batchStore;
    private readonly IWorkspaceFormatConverter _formatConverter;
    private readonly IConfirmationService? _confirmation;
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
        IAppSettingsService settingsService,
        IEndpointStore endpointStore,
        IBatchStore batchStore,
        IWorkspaceFormatConverter formatConverter,
        IConfirmationService? confirmation = null)
    {
        _endpointStore = endpointStore;
        _batchStore = batchStore;
        _formatConverter = formatConverter;
        _confirmation = confirmation;
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

        // Which format this workspace is in decides what the menu offers, so switching tabs has to
        // re-ask - the two workspaces open side by side need not be in the same one.
        OnPropertyChanged(nameof(ActiveFormat));
        OnPropertyChanged(nameof(UsesEndpoints));
        OnPropertyChanged(nameof(UsesRequests));
        OnPropertyChanged(nameof(CanAddCase));
        OnPropertyChanged(nameof(CanAddBatch));
        OnPropertyChanged(nameof(IsScratchActive));
        RefreshMoveTargets();

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
        settings.Session.OpenWorkspacePaths = Roots.Select(r => r.FullPath).ToList();
        settings.Session.ActiveWorkspacePath = ActiveRoot?.FullPath;
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
        if (settings.Session.OpenWorkspacePaths.Count == 0)
        {
            return;
        }

        _suppressPersist = true;
        try
        {
            foreach (var path in settings.Session.OpenWorkspacePaths)
            {
                if (!_workspaceStore.IsWorkspaceRoot(path))
                {
                    continue;
                }

                var workspace = await _workspaceStore.LoadWorkspaceAsync(path);
                Roots.Add(new WorkspaceRootViewModel(workspace, _requestStore));
            }

            ActiveRoot = (settings.Session.ActiveWorkspacePath is not null
                ? Roots.FirstOrDefault(r => string.Equals(r.FullPath, settings.Session.ActiveWorkspacePath, StringComparison.OrdinalIgnoreCase))
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
    [NotifyPropertyChangedFor(nameof(CanAddCase))]
    [NotifyPropertyChangedFor(nameof(CanAddBatch))]
    public partial WorkspaceNodeViewModel? SelectedNode { get; set; }

    partial void OnSelectedNodeChanged(WorkspaceNodeViewModel? value) => RefreshMoveTargets();

    /// <summary>
    /// Narrows the tree to nodes matching by name, URL or method.
    ///
    /// <para>There was no way to find anything: an OpenAPI import routinely produces a hundred requests
    /// in nested folders, so the app's flagship import created the one tree it could not navigate.</para>
    /// </summary>
    [ObservableProperty]
    public partial string? Filter { get; set; }

    partial void OnFilterChanged(string? value)
    {
        ApplyFilter();
        OnPropertyChanged(nameof(IsFiltering));
        OnPropertyChanged(nameof(FilterMatchedNothing));
    }

    /// <summary>Re-applies the current filter to every open workspace. Called on a filter change and
    /// after a refresh, since a rescan brings in nodes that have never been filtered.</summary>
    public void ApplyFilter()
    {
        foreach (var root in Roots)
        {
            root.ApplyFilter(Filter);
        }
    }

    /// <summary>True when a filter is narrowing the tree - drives the "no matches" message, which is
    /// what stops an empty tree reading as a workspace that failed to load.</summary>
    public bool IsFiltering => !string.IsNullOrWhiteSpace(Filter);

    /// <summary>True when a filter is in force and nothing survived it.</summary>
    public bool FilterMatchedNothing => IsFiltering && Roots.All(r => !r.Children.Any(c => c.IsVisible));

    /// <summary>Raised when the user closes a workspace tab - MainViewModel clears the main canvas
    /// if it was showing something from that workspace.</summary>
    public event Action<Workspace>? WorkspaceClosed;

    /// <summary>Raised when the user closes the last workspace tab in this window (via the tab close
    /// button, not a tear-off) - the WindowManager closes the window unless it's the only one left.</summary>
    public event Action? WorkspacesEmptied;

    // SelectWorkspaceCommand used to live here, setting ActiveRoot. Deleted: the title bar's TabStrip
    // binds SelectedItem to ActiveRoot two-way, so selecting a tab already sets it - the command was
    // a second route to the same state that nothing ever called. Found by WiringTests, which is what
    // that test is for.

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

        // Explicitly, because closing a tab that was NOT the active one changes nothing else here -
        // and a closed workspace left in the list is a "Move to workspace" entry that would move
        // something into a tree nobody is watching.
        RefreshMoveTargets();

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

    /// <summary>
    /// Where a request goes when you have not chosen anywhere to put it.
    /// </summary>
    /// <remarks>
    /// Under the app's own data directory, never in a folder the user picked - the point is that
    /// trying something out costs no decisions and litters nothing. It is an ordinary workspace in
    /// every other respect, so environments, auth, history and rules all work in it without a second
    /// code path, and a request that turns out to be worth keeping can be moved into a real one.
    /// </remarks>
    public static string ScratchWorkspacePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fubar", "scratch");

    /// <summary>
    /// Opens the scratch workspace (creating it the first time) and drafts a request in it.
    /// </summary>
    /// <remarks>
    /// The answer to "I just want to send one request". Everything else here needs a workspace on
    /// disk before it will do anything at all, which put four filing decisions between launching the
    /// app and seeing a response - and none of them can be made sensibly before you know whether the
    /// request was worth keeping. The request is a DRAFT, so even here nothing is written until Save.
    /// </remarks>
    [RelayCommand]
    public async Task NewScratchRequestAsync()
    {
        try
        {
            var root = Roots.FirstOrDefault(r => string.Equals(
                r.FullPath, ScratchWorkspacePath, StringComparison.OrdinalIgnoreCase));

            if (root is null)
            {
                var workspace = await _workspaceStore.CreateWorkspaceAsync(ScratchWorkspacePath);
                root = new WorkspaceRootViewModel(workspace, _requestStore);
                Roots.Add(root);
            }

            ActiveRoot = root;
            SelectedNode = null;

            NewRequestCommand.Execute(null);
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Could not open the scratch workspace: {ex.Message}");
        }
    }

    /// <summary>Whether the active workspace is the scratch one - the shell says so, because a request
    /// saved somewhere you did not choose is worth knowing about.</summary>
    public bool IsScratchActive => ActiveRoot is not null && string.Equals(
        ActiveRoot.FullPath, ScratchWorkspacePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The open workspaces the selection could be moved into - every one except its own.
    /// </summary>
    /// <remarks>
    /// The other half of the scratch pad: something tried out with no filing decisions has to be able
    /// to become a real request later, or "try it here first" is a dead end. Open workspaces only,
    /// because those are the ones whose format is known and whose tree is already watching.
    /// </remarks>
    public ObservableCollection<WorkspaceRootViewModel> MoveTargets { get; } = [];

    /// <summary>
    /// Whether the selection is a thing that can move between workspaces.
    /// </summary>
    /// <remarks>
    /// A request, an endpoint or a folder - the things that stand on their own. A case and a batch
    /// belong to their endpoint and go where it goes; moving one alone would leave a case with no
    /// endpoint, which is not a state this format has. A draft has no file to move at all.
    /// </remarks>
    public bool CanMove =>
        MoveTargets.Count > 0
        && SelectedNode is
        {
            IsDraft: false,
            Kind: WorkspaceNodeKind.Request or WorkspaceNodeKind.Endpoint or WorkspaceNodeKind.Folder,
        }
        && SelectedNode is not WorkspaceRootViewModel;

    private void RefreshMoveTargets()
    {
        var owner = SelectedNode is null
            ? null
            : Roots.FirstOrDefault(r => SelectedNode.FullPath.StartsWith(r.FullPath, StringComparison.OrdinalIgnoreCase));

        MoveTargets.Clear();
        foreach (var root in Roots.Where(r => r != owner))
        {
            MoveTargets.Add(root);
        }

        OnPropertyChanged(nameof(CanMove));
    }

    /// <summary>
    /// Moves the selection into <paramref name="target"/>'s <c>collections/</c>.
    /// </summary>
    /// <remarks>
    /// Into the root of the target rather than a folder chosen here: picking the destination folder
    /// needs a tree of its own, and the tree it lands in already has Rename and its own context menu
    /// for putting it where it belongs. Landing somewhere findable beats a dialog.
    /// </remarks>
    [RelayCommand]
    private async Task MoveToWorkspaceAsync(WorkspaceRootViewModel? target)
    {
        if (target is null || SelectedNode is not { } node || !CanMove)
        {
            return;
        }

        var owner = Roots.FirstOrDefault(
            r => node.FullPath.StartsWith(r.FullPath, StringComparison.OrdinalIgnoreCase));

        // The two formats store an endpoint and a request differently, and a directory holding
        // endpoint.json dropped into a requests-format workspace is a folder full of files that
        // workspace cannot send. Refused rather than half-working.
        if (owner is not null
            && owner.Workspace.Manifest.Format != target.Workspace.Manifest.Format
            && node.Kind != WorkspaceNodeKind.Folder)
        {
            _statusLog.LogError(
                $"\"{node.DisplayName}\" is in the {owner.Workspace.Manifest.Format.ToString().ToLowerInvariant()} "
                + $"format and \"{target.Workspace.Manifest.Name}\" is in the "
                + $"{target.Workspace.Manifest.Format.ToString().ToLowerInvariant()} format.");
            return;
        }

        if (!await ConfirmMoveAsync(node, target))
        {
            return;
        }

        try
        {
            var from = node.FullPath;
            var moved = _requestStore.MovePath(from, Path.Combine(target.FullPath, "collections"));

            _statusLog.Log($"Moved \"{node.DisplayName}\" into \"{target.Workspace.Manifest.Name}\".");

            SelectedNode = null;
            RefreshRootFor(from);
            target.Refresh();
            ActiveRoot = target;

            PathMoved?.Invoke(from, moved);
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Could not move \"{node.DisplayName}\": {ex.Message}");
        }
    }

    /// <summary>Raised after a successful move, with where it was and where it is now - the shell
    /// re-opens whatever was showing the old path, which no longer exists.</summary>
    public event Action<string, string>? PathMoved;

    /// <summary>
    /// A move takes something out of one repository and puts it in another, and both are usually
    /// committed. Asked about rather than done quietly - with no confirmation service wired the answer
    /// is yes, matching every other file operation here.
    /// </summary>
    private async Task<bool> ConfirmMoveAsync(WorkspaceNodeViewModel node, WorkspaceRootViewModel target)
    {
        if (_confirmation is null)
        {
            return true;
        }

        return await _confirmation.ConfirmAsync(
            "Move to workspace",
            $"Move \"{node.DisplayName}\" into \"{target.Workspace.Manifest.Name}\"? "
            + "It stops being where it is now.",
            "Move");
    }

    /// <summary>
    /// Which shape the active workspace's collections are in. Read from the manifest, never sniffed
    /// from the files - one field, so every screen agrees about which half of the product it is in.
    /// </summary>
    public WorkspaceFormat ActiveFormat => ActiveRoot?.Workspace.Manifest.Format ?? WorkspaceFormat.Requests;

    /// <summary>Drives the menu: "New endpoint" in one format, "New request" in the other. Both are
    /// never offered at once - two ways to make the same thing is how a tree ends up half converted.</summary>
    public bool UsesEndpoints => ActiveFormat == WorkspaceFormat.Endpoints;

    public bool UsesRequests => !UsesEndpoints;

    /// <summary>"Add case" only inside an endpoint, which is the only place a case can live.</summary>
    public bool CanAddCase => UsesEndpoints && SelectedNode is { Kind: WorkspaceNodeKind.Endpoint or WorkspaceNodeKind.Case };

    /// <summary>"Add batch" in the same three places, for the same reason: an endpoint's batches live
    /// beside its cases, so anything inside an endpoint can offer one.</summary>
    public bool CanAddBatch => UsesEndpoints
        && SelectedNode is { Kind: WorkspaceNodeKind.Endpoint or WorkspaceNodeKind.Case or WorkspaceNodeKind.Batch };

    /// <summary>The endpoint directory the selection sits in, or null when it is not inside one.</summary>
    private string? SelectedEndpointDirectory => SelectedNode switch
    {
        { Kind: WorkspaceNodeKind.Endpoint } endpoint => endpoint.FullPath,

        // A case is <endpoint>/cases/<name>.json and a batch is <endpoint>/batches/<name>.json, so
        // both are two levels down. Walked rather than string-trimmed so the two stay in step.
        { Kind: WorkspaceNodeKind.Case or WorkspaceNodeKind.Batch } child =>
            Path.GetDirectoryName(Path.GetDirectoryName(child.FullPath)),

        _ => null,
    };

    [RelayCommand]
    private void NewRequest()
    {
        var parent = ResolveTargetDirectory();
        if (parent is null)
        {
            _statusLog.Log("Open a workspace before creating a request.");
            return;
        }

        // Proposed, not created - the same rule cases and batches follow. An endpoint is a DIRECTORY
        // holding endpoint.json, so a drafted one is a directory that does not exist yet either;
        // SaveRequestAsync makes it on the way to writing the file.
        var path = UsesEndpoints
            ? ProposeDirectory(parent, "New Endpoint")
            : ProposeFile(parent, "New Request");

        var draft = AddRootDraft(
            parent, path, UsesEndpoints ? WorkspaceNodeKind.Endpoint : WorkspaceNodeKind.Request);

        if (draft is null)
        {
            return;
        }

        RequestDrafted?.Invoke(
            UsesEndpoints ? Path.Combine(path, IEndpointStore.EndpointFileName) : path);
    }

    /// <summary>Raised when a new, unwritten request or endpoint is made - the shell opens an editor
    /// on a blank one rather than reading a file that is not there yet.</summary>
    public event Action<string>? RequestDrafted;

    /// <summary>A free file path under <paramref name="parent"/>, writing nothing.</summary>
    private static string ProposeFile(string parent, string name)
    {
        var candidate = Path.Combine(parent, name + ".json");
        var n = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(parent, $"{name} {n++}.json");
        }

        return candidate;
    }

    private static string ProposeDirectory(string parent, string name)
    {
        var candidate = Path.Combine(parent, name);
        var n = 2;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(parent, $"{name} {n++}");
        }

        return candidate;
    }

    /// <summary>Puts an unwritten request or endpoint into the tree under the folder that will hold
    /// it. Unlike a case or a batch, its parent may be the workspace root itself.</summary>
    private WorkspaceNodeViewModel? AddRootDraft(string parentDirectory, string path, WorkspaceNodeKind kind)
    {
        var parent = FindNodeByPath(parentDirectory)
                     ?? Roots.FirstOrDefault(r => string.Equals(
                         Path.Combine(r.FullPath, "collections"), parentDirectory, StringComparison.OrdinalIgnoreCase));

        if (parent is null)
        {
            _statusLog.LogError($"\"{parentDirectory}\" is not in the tree.");
            return null;
        }

        var draft = new WorkspaceNodeViewModel(
            Path.GetFileName(path), path, isDirectory: kind == WorkspaceNodeKind.Endpoint, kind)
        {
            IsDraft = true,
            Method = "GET",
        };

        parent.Children.Add(draft);
        parent.IsExpanded = true;
        SelectedNode = draft;

        return draft;
    }

    [RelayCommand]
    private void NewCase()
    {
        if (SelectedNode is not { } node
            || _endpointStore.EndpointDirectoryOf(node.FullPath) is not { } endpointDirectory)
        {
            _statusLog.Log("Select an endpoint to add a case to.");
            return;
        }

        // Proposed, not created: a new case lives in memory until it is saved, so opening one and
        // changing your mind leaves nothing behind.
        var path = _endpointStore.ProposeCasePath(endpointDirectory, "new-case");

        if (AddDraft(endpointDirectory, path, WorkspaceNodeKind.Case) is null)
        {
            return;
        }

        CaseDrafted?.Invoke(path);
    }

    /// <summary>Raised when a new, unwritten case is made - the shell opens an editor on a blank one
    /// rather than reading a file that is not there yet.</summary>
    public event Action<string>? CaseDrafted;

    /// <summary>
    /// Puts an unwritten node into the tree under the endpoint that will hold it, and selects it.
    /// </summary>
    /// <remarks>
    /// The tree is otherwise a reflection of the file system, so this is the one thing in it that disk
    /// does not account for - see <see cref="WorkspaceNodeViewModel.IsDraft"/>, which is what keeps a
    /// rescan from throwing it away a moment later.
    /// </remarks>
    private WorkspaceNodeViewModel? AddDraft(string endpointDirectory, string path, WorkspaceNodeKind kind)
    {
        if (FindNodeByPath(endpointDirectory) is not { } endpoint)
        {
            _statusLog.LogError($"\"{endpointDirectory}\" is not in the tree.");
            return null;
        }

        var draft = new WorkspaceNodeViewModel(
            Path.GetFileName(path), path, isDirectory: false, kind)
        {
            IsDraft = true,
        };

        if (kind == WorkspaceNodeKind.Batch)
        {
            endpoint.Batches.Add(draft);
        }
        else
        {
            endpoint.Children.Add(draft);
        }

        endpoint.IsExpanded = true;
        SelectedNode = draft;

        return draft;
    }

    /// <summary>
    /// Adds a batch to the selected endpoint - a way of running THIS endpoint, as opposed to the
    /// workspace's cross-cutting occasions in the Left Pane's Batches group.
    /// </summary>
    [RelayCommand]
    private void NewBatch()
    {
        if (SelectedEndpointDirectory is not { } endpointDirectory)
        {
            _statusLog.Log("Select an endpoint to add a batch to.");
            return;
        }

        var path = _batchStore.ProposeBatchPath(endpointDirectory, "new-batch");

        if (AddDraft(endpointDirectory, path, WorkspaceNodeKind.Batch) is null)
        {
            return;
        }

        // Opened straight away, for the same reason the Left Pane's does: a row saying "0 steps" with
        // no way in but a text editor is what made batches a JSON-editing job.
        BatchOpened?.Invoke(path, new Batch { Name = Path.GetFileNameWithoutExtension(path) });
    }

    /// <summary>
    /// Splits every request in the active workspace into an endpoint and one case.
    /// </summary>
    /// <remarks>
    /// The thing that closes the split between the two formats. Without it an existing workspace
    /// would never get cases, batches or snapshots, and the decision to convert nothing on open would
    /// be permanent rather than merely cautious (spec §10.4).
    /// </remarks>
    [RelayCommand]
    private async Task ConvertToEndpointsAsync()
    {
        if (ActiveRoot is not { } root)
        {
            return;
        }

        var plan = _formatConverter.Preview(root.Workspace);

        if (!plan.CanRun)
        {
            foreach (var blocker in plan.Blockers)
            {
                _statusLog.LogWarning(blocker);
            }

            return;
        }

        // Asked, and told exactly what will happen first. This rewrites committed files, and the one
        // thing a preview must not do is undersell the size of what follows.
        if (_confirmation is not null)
        {
            var confirmed = await _confirmation.ConfirmAsync(
                "Convert to endpoints",
                $"{plan.Steps.Count} request(s) become an endpoint directory with one case each.\n\n"
                + "The originals are copied to .fubar/backup/ first. Snapshots move with their request.",
                "Convert");

            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            var result = await _formatConverter.ConvertAsync(root.Workspace);

            foreach (var warning in result.Warnings)
            {
                _statusLog.LogWarning(warning);
            }

            _statusLog.Log(
                $"Converted {result.EndpointsCreated} request(s) to endpoints. Backup: {result.BackupPath}");

            // Reloaded rather than patched in memory: the manifest on disk is now the authority on
            // which format this workspace is in, and every screen reads it from there.
            var reloaded = await _workspaceStore.LoadWorkspaceAsync(root.FullPath);
            root.UpdateWorkspace(reloaded);
            OnPropertyChanged(nameof(ActiveFormat));
            OnPropertyChanged(nameof(UsesEndpoints));
            OnPropertyChanged(nameof(UsesRequests));
            root.Refresh();
            WorkspaceContentImported?.Invoke(reloaded);
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Could not convert the workspace: {ex.Message}");
        }
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
        if (await _importDialog.ShowAsync(root.FullPath) is { } choice)
        {
            await ApplyImportAsync(choice, root, "OpenAPI");
        }
    }

    /// <summary>
    /// Writes the items the user ticked in the import dialog, whichever format they came from.
    ///
    /// <para>Shared because everything past parsing is the same work - which is the point of the
    /// planner/apply split: the Postman import gets the preview, the per-item choice and the "your
    /// manual edits survive" guarantee that only the OpenAPI one had.</para>
    /// </summary>
    private async Task ApplyImportAsync(ImportDialogResult choice, WorkspaceRootViewModel root, string format)
    {
        try
        {
            var result = await _openApiImport.ApplyDiffAsync(
                choice.Plan, choice.SelectedRequests, choice.SelectedVariables, choice.Options, root.FullPath);
            _statusLog.Log(
                $"Imported \"{result.ApiTitle}\": {result.CreatedCount} new + {result.UpdatedCount} updated/removed requests, " +
                $"{result.VariableCount} variables, {result.AuthProfileCount} auth profiles.");
            foreach (var warning in result.Warnings)
            {
                // Warnings, not Info: an import that silently dropped a script or a body is exactly
                // what the user needs to see, and this is where Postman's untranslated lines arrive.
                _statusLog.LogWarning(warning);
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
            _statusLog.LogError($"{format} import failed: {ex.Message}");
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
            _statusLog.LogError($"curl import failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Imports a Postman collection through the same preview the OpenAPI import has always had, or an
    /// environment export directly.
    ///
    /// <para>A collection used to be written straight into the workspace, with the outcome reported
    /// into a status log that was collapsed by default - so re-importing silently overwrote whatever
    /// had been edited since the last time, and the only notice was somewhere nobody was looking.</para>
    ///
    /// <para>An environment export has no requests, so there is nothing to preview and it still goes
    /// through the direct path.</para>
    /// </summary>
    [RelayCommand]
    private async Task ImportPostmanAsync()
    {
        if (ActiveRoot is not { } root)
        {
            _statusLog.Log("Open a workspace before importing.");
            return;
        }

        // The dialog picks the file, parses it, and shows the diff; null means cancelled, which means
        // cancelled - offering a different file picker next would be answering a question nobody asked.
        if (await _importDialog.ShowPostmanAsync(root.FullPath) is { } choice)
        {
            await ApplyImportAsync(choice, root, "Postman");
        }
    }

    /// <summary>
    /// Imports a Postman ENVIRONMENT or globals export.
    ///
    /// <para>Its own action rather than a branch inside the collection import: an environment produces
    /// no requests, so the add/update/remove preview would list nothing and read as a failed parse.
    /// Two file types, two menu items.</para>
    /// </summary>
    [RelayCommand]
    private async Task ImportPostmanEnvironmentAsync()
    {
        if (ActiveRoot is not { } root)
        {
            _statusLog.Log("Open a workspace before importing.");
            return;
        }

        var file = await _filePicker.PickOpenFileAsync("Import a Postman environment export (JSON)");
        if (file is null)
        {
            return;
        }

        try
        {
            var result = await _postmanImport.ImportAsync(file, root.FullPath);
            _statusLog.Log($"Imported Postman \"{result.CollectionName}\": " +
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
            _statusLog.LogError($"Postman import failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void BeginRename()
    {
        if (SelectedNode is not { } node || node is WorkspaceRootViewModel)
        {
            return;
        }

        // Seeded with what the row SHOWS, not the file name. Renaming should start from the text you
        // were looking at; WorkspaceService.RenamePath puts the original extension back when the new
        // name has none, so typing "Login" over "Login" still lands on Login.json.
        node.EditName = node.DisplayName;
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
        if (string.IsNullOrEmpty(newName) || newName == node.DisplayName)
        {
            return;
        }

        // A draft has no file to move - renaming one only changes which file it will take. Going
        // through RenamePath would throw about a path that was never meant to exist yet.
        if (node.IsDraft)
        {
            RenameDraft(node, newName);
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
            _statusLog.LogError($"Rename failed: {ex.Message}");
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

        // A draft has no file to delete - discarding it is just forgetting it. Going through
        // DeletePath would throw about a path that was never meant to exist.
        if (node.IsDraft)
        {
            DiscardDraft(node);
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
            _statusLog.LogError($"Delete failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Retires the draft that was reserving <paramref name="draftPath"/>, then rescans.
    /// </summary>
    /// <remarks>
    /// A draft reserves the file it WOULD occupy, and saving can write a different one: the name box
    /// is the file's name, so saving a renamed draft creates <c>not-found.json</c> while the node is
    /// still holding <c>new-case.json</c>. The scan then adds the real row beside a draft that will
    /// never resolve, and the tree shows the same case twice - once forever unsaved.
    /// </remarks>
    public void DraftSaved(string draftPath)
    {
        if (FindNodeByPath(draftPath) is { IsDraft: true } draft)
        {
            foreach (var root in Roots)
            {
                if (RemoveDraft(root, draft))
                {
                    break;
                }
            }
        }

        RefreshRootFor(draftPath);
    }

    /// <summary>
    /// Points an unwritten node at a different file. Nothing moves, because nothing was written.
    /// </summary>
    /// <remarks>
    /// The editor holding this draft was opened on the OLD path and will still save there, so it is
    /// told to follow - otherwise renaming a draft before saving it would write the file under the
    /// name you replaced.
    /// </remarks>
    private void RenameDraft(WorkspaceNodeViewModel draft, string newName)
    {
        if (!DocumentName.IsValid(newName))
        {
            _statusLog.LogError($"\"{newName}\" cannot be a name: it has to work as a file name.");
            return;
        }

        var parent = Path.GetDirectoryName(draft.FullPath)!;
        var from = draft.FullPath;

        draft.FullPath = draft.IsDirectory
            ? Path.Combine(parent, newName)
            : Path.Combine(parent, newName + ".json");

        draft.Name = Path.GetFileName(draft.FullPath);

        DraftRenamed?.Invoke(from, draft.FullPath);
    }

    /// <summary>Raised when an unwritten node is renamed, with where it was and where it now points -
    /// so an editor already open on it saves to the new file rather than the old one.</summary>
    public event Action<string, string>? DraftRenamed;

    /// <summary>Takes an unwritten node back out of the tree. Nothing on disk is touched, because
    /// nothing on disk was ever made.</summary>
    private void DiscardDraft(WorkspaceNodeViewModel draft)
    {
        foreach (var root in Roots)
        {
            if (RemoveDraft(root, draft))
            {
                break;
            }
        }

        SelectedNode = null;
        _statusLog.Log($"Discarded \"{draft.DisplayName}\" - it was never saved.");
    }

    private static bool RemoveDraft(WorkspaceNodeViewModel parent, WorkspaceNodeViewModel draft)
    {
        if (parent.Children.Remove(draft) || parent.Batches.Remove(draft))
        {
            return true;
        }

        return parent.Children.Any(child => RemoveDraft(child, draft));
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
            _statusLog.LogError($"Duplicate failed: {ex.Message}");
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

    /// <summary>Raised when the user opens a case - <see cref="MainViewModel"/> wires this to the case
    /// editor, which is a different surface from the request editor on purpose (spec §9.2).</summary>
    public event Action<string>? CaseFileActivated;

    [RelayCommand]
    private void ActivateSelection()
    {
        switch (SelectedNode)
        {
            case { Kind: WorkspaceNodeKind.Request } request:
                RequestFileActivated?.Invoke(request.FullPath);
                break;

            // An endpoint IS a request, stored under a different name, so it opens in the request
            // editor. Its directory is what the tree holds; endpoint.json is what the editor loads.
            case { Kind: WorkspaceNodeKind.Endpoint } endpoint:
                RequestFileActivated?.Invoke(
                    Path.Combine(endpoint.FullPath, Core.Workspaces.IEndpointStore.EndpointFileName));
                break;

            case { Kind: WorkspaceNodeKind.Case } endpointCase:
                CaseFileActivated?.Invoke(endpointCase.FullPath);
                break;

            case { Kind: WorkspaceNodeKind.Batch } batch:
                _ = OpenBatchAsync(batch.FullPath);
                break;
        }
    }

    /// <summary>
    /// Raised when one of an endpoint's own batches is opened, with a freshly read copy of it -
    /// <c>MainViewModel</c> puts it in the same editor the Left Pane's Batches group uses.
    /// </summary>
    /// <remarks>
    /// Read here rather than by the shell, which has no batch store, and read fresh rather than kept
    /// on the node: an editor opened on a stale copy would save it back over whatever has happened to
    /// the file since.
    /// </remarks>
    public event Action<string, Batch>? BatchOpened;

    private async Task OpenBatchAsync(string filePath)
    {
        try
        {
            BatchOpened?.Invoke(filePath, await _batchStore.LoadBatchAsync(filePath));
        }
        catch (Exception ex)
        {
            _statusLog.LogError(
                $"Could not open the batch \"{Path.GetFileNameWithoutExtension(filePath)}\": {ex.Message}");
        }
    }

    /// <summary>Raised when the user asks to run the selection - <see cref="MainViewModel"/> wires this
    /// to the Run window, because deciding what to run needs the ACTIVE ENVIRONMENT, which lives beside
    /// this view model rather than in it.</summary>
    public event Action<WorkspaceNodeViewModel>? RunRequested;

    [RelayCommand]
    private void Run()
    {
        // The selection, or the whole active workspace when nothing is selected - "run everything" is
        // the commonest thing to want and should not need a click on the root first.
        var node = SelectedNode ?? ActiveRoot;
        if (node is not null)
        {
            RunRequested?.Invoke(node);
        }
    }

    /// <summary>Raised when the user asks to compare the selection across two environments. Wired by
    /// <see cref="MainViewModel"/> for the same reason as <see cref="RunRequested"/>: it needs the
    /// workspace's environments, which live beside this view model rather than in it.</summary>
    public event Action<WorkspaceNodeViewModel>? CompareEnvironmentsRequested;

    [RelayCommand]
    private void CompareEnvironments()
    {
        var node = SelectedNode ?? ActiveRoot;
        if (node is not null)
        {
            CompareEnvironmentsRequested?.Invoke(node);
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
