using Fubar.Studio.Application.Running;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Controls;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.History;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Secrets;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Root MVVM context for the shell's fixed 3-pane layout (Left | Request Editor | Response - see
/// MainWindow.axaml): owns <see cref="WorkspaceExplorer"/> (Left Pane), <see cref="ActiveEditor"/>
/// (the single main-canvas surface - a <see cref="RequestEditorViewModel"/>,
/// <see cref="EnvironmentEditorViewModel"/>, or <see cref="AuthProfileEditorViewModel"/>; never more
/// than one at a time, mirroring RequestEditorPane.md §1's "no tabs" design), <see cref="EnvironmentManager"/>
/// (the shell control bar's active-environment selector + secrets toggle), and
/// <see cref="StatusLog"/> (collapsible bottom strip). Also keeps the Left Pane's tree in sync with
/// whichever request is open: the active node's unsaved-changes dot (<see cref="SyncDirtyMarker"/>)
/// and, after a Save, its method/auth badges - and activates the Environments/Auth Profiles groups
/// (<see cref="ActivateWorkspaceContextAsync"/>) for whichever workspace is currently in focus.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IRequestStore _workspaceService;
    private readonly Fubar.Studio.Core.Workspaces.IEndpointStore _endpointStore;
    private readonly IBatchPlanner _batchPlanner;
    private readonly IAuthProfileStore _authProfiles;
    private readonly IProtocolRegistry _protocolRegistry;
    private readonly IEditorViewModelFactory _editorFactory;
    private readonly IRunDialogService _runDialog;
    private readonly IEnvironmentComparisonDialogService _comparisonDialog;

    /// <summary>Optional so a headless test can construct the shell without a windowing stack. Null
    /// means no prompt can be shown, which is treated as "do not discard" rather than as consent.</summary>
    private readonly IConfirmationService? _confirmation;
    private readonly IClipboardService? _clipboard;
    private readonly Fubar.Studio.Core.Diagnostics.ILogSink? _logSink;
    private readonly Fubar.Studio.Core.Settings.IMachinePolicyService? _policy;
    private readonly Fubar.Studio.Core.Settings.IAppSettingsService? _appSettings;
    private RequestEditorViewModel? _dirtyTrackedRequest;

    public WorkspaceExplorerViewModel WorkspaceExplorer { get; }
    public EnvironmentManagerViewModel EnvironmentManager { get; }
    public StatusLogViewModel StatusLog { get; }
    public LeftPaneViewModel LeftPane { get; }

    /// <summary>The reusable title-bar tab strip's drag bridge (move-between-windows / tear-off),
    /// bound to <c>fc:TabStrip.DragHost</c> in MainWindow. App-wide singleton, same instance in every
    /// window.</summary>
    public ITabDragHost TabDragHost { get; }

    /// <summary>
    /// The single main-canvas surface, or null when nothing is open. Replaced wholesale (not added
    /// alongside) whenever a different request/environment/profile is activated - there is
    /// deliberately nowhere to keep a second one around.
    /// </summary>
    [ObservableProperty]
    public partial object? ActiveEditor { get; set; }

    /// <summary>Convenience typed view of <see cref="ActiveEditor"/> - null unless it's currently a
    /// request (not an environment/auth-profile editor). Drives the Response Pane's visibility and
    /// the Left Pane dirty-dot tracking below.</summary>
    public RequestEditorViewModel? ActiveRequest => ActiveEditor as RequestEditorViewModel;

    /// <summary>Whether the bottom Status &amp; Log strip is expanded.</summary>
    [ObservableProperty]
    public partial bool IsLogVisible { get; set; }

    // Kept in step so the log can clear its unread badge when the user actually looks at it, rather
    // than when something merely tried to raise it.
    partial void OnIsLogVisibleChanged(bool value) => StatusLog.IsVisible = value;

    public MainViewModel(
        WorkspaceExplorerViewModel workspaceExplorer,
        EnvironmentManagerViewModel environmentManager,
        StatusLogViewModel statusLog,
        LeftPaneViewModel leftPane,
        IRequestStore workspaceService,
        Fubar.Studio.Core.Workspaces.IEndpointStore endpointStore,
        IBatchPlanner batchPlanner,
        IAuthProfileStore authProfiles,
        IProtocolRegistry protocolRegistry,
        IEditorViewModelFactory editorFactory,
        IRunDialogService runDialog,
        IEnvironmentComparisonDialogService comparisonDialog,
        ITabDragHost tabDragHost,
        IConfirmationService? confirmation = null,
        IClipboardService? clipboard = null,
        Fubar.Studio.Core.Diagnostics.ILogSink? logSink = null,
        Fubar.Studio.Core.Settings.IMachinePolicyService? policy = null,
        Fubar.Studio.Core.Settings.IAppSettingsService? appSettings = null)
    {
        WorkspaceExplorer = workspaceExplorer;
        TabDragHost = tabDragHost;
        EnvironmentManager = environmentManager;
        StatusLog = statusLog;
        LeftPane = leftPane;
        _workspaceService = workspaceService;
        _endpointStore = endpointStore;
        _batchPlanner = batchPlanner;
        _authProfiles = authProfiles;
        _protocolRegistry = protocolRegistry;
        _editorFactory = editorFactory;
        _runDialog = runDialog;
        _comparisonDialog = comparisonDialog;
        _confirmation = confirmation;
        _clipboard = clipboard;
        _logSink = logSink;
        _policy = policy;
        _appSettings = appSettings;

        WorkspaceExplorer.PropertyChanged += OnWorkspaceExplorerPropertyChanged;
        WorkspaceExplorer.WorkspaceClosed += OnWorkspaceClosed;
        WorkspaceExplorer.WorkspaceContentImported += workspace => _ = ActivateWorkspaceContextAsync(workspace);

        WorkspaceExplorer.RequestFileActivated += path => _ = OpenRequestAsync(path);
        WorkspaceExplorer.CaseFileActivated += path => _ = OpenCaseAsync(path);
        WorkspaceExplorer.CaseDrafted += path => _ = OpenCaseAsync(path, isDraft: true);
        WorkspaceExplorer.RequestDrafted += path => _ = OpenRequestAsync(path, isDraft: true);
        WorkspaceExplorer.DraftRenamed += OnDraftRenamed;
        WorkspaceExplorer.PathMoved += OnPathMoved;
        WorkspaceExplorer.BatchOpened += OpenBatchEditor;
        WorkspaceExplorer.FolderOpened += (path, config) => _ = OpenFolderEditorAsync(path, config);
        WorkspaceExplorer.RunRequested += OnRunRequested;
        WorkspaceExplorer.CompareEnvironmentsRequested += OnCompareEnvironmentsRequested;
        LeftPane.EnvironmentsSection.EditRequested += OpenEnvironmentEditor;
        LeftPane.AuthProfilesSection.EditRequested += OpenAuthProfileEditor;
        LeftPane.BatchesSection.RunRequested += row => _ = OnRunBatchRequestedAsync(row);
        LeftPane.BatchesSection.EditRequested += OpenBatchEditor;

        // A failure reported into a collapsed panel is not reported. The strip opens itself the first
        // time something actually goes wrong; the badge on the shell covers everything after that.
        StatusLog.RaiseRequested += () => IsLogVisible = true;
        StatusLog.IsVisible = IsLogVisible;

        // Opening a pre-floor request rewrites it into the current format. That is an edit to a
        // committed file, so it is announced rather than done quietly - the user is about to see it in
        // a diff and is entitled to know why.
        _workspaceService.RequestMigrated += (path, changes) =>
            StatusLog.LogWarning(
                $"Updated \"{System.IO.Path.GetFileName(path)}\" to the current format: {string.Join("; ", changes)}.");

        StatusLog.Log("Fubar shell ready.");
    }

    /// <summary>
    /// Opens the Run window for a selected folder or request.
    ///
    /// <para>Lives here rather than on the explorer because a run needs the ACTIVE ENVIRONMENT, which
    /// decides every {{variable}} in the collection - and this is the view model that holds both the
    /// tree and the environment manager.</para>
    /// </summary>
    private void OnRunRequested(WorkspaceNodeViewModel node)
    {
        // Which workspace the node belongs to: several can be open, and the selection is not always in
        // the active one.
        var root = WorkspaceExplorer.Roots.FirstOrDefault(
                       r => node.FullPath.StartsWith(r.FullPath, StringComparison.OrdinalIgnoreCase))
                   ?? WorkspaceExplorer.ActiveRoot;

        if (root is null)
        {
            return;
        }

        // A batch in the tree runs through the planner, exactly as the Left Pane's do - it names its
        // own steps, so expanding the NODE would send nothing.
        if (node.Kind == WorkspaceNodeKind.Batch)
        {
            _ = OnRunBatchRequestedAsync(node.DisplayName, EndpointPathOf(node, root), root);
            return;
        }

        var plan = RunPlan.From(node.ToTreeNode());
        if (plan.IsEmpty)
        {
            // Nothing to run is worth SAYING. A window listing nothing looks like a failure to load.
            StatusLog.Log($"Nothing to run in \"{node.Name}\" - it holds no requests.");
            return;
        }

        _runDialog.Show(
            plan,
            root.Workspace,
            EnvironmentManager.ActiveEnvironment,
            [.. EnvironmentManager.Environments],
            node.Name);
    }

    /// <summary>
    /// Opens the Run window for a batch, with the batch's own oracle already chosen.
    /// </summary>
    /// <remarks>
    /// The batch says what it is for; the window still lets it be changed before running, which is the
    /// same rule the command line follows - a batch written for staging has to be runnable against a
    /// branch deployment without editing the file.
    /// </remarks>
    private Task OnRunBatchRequestedAsync(BatchRowViewModel row) =>
        OnRunBatchRequestedAsync(row.Name, null, WorkspaceExplorer.ActiveRoot);

    /// <param name="ownerPath">The endpoint this batch belongs to, relative to <c>collections/</c>, or
    /// null for one of the workspace's own. It is also what the window is titled with, since
    /// <c>@happy</c> and <c>orders/get-order@happy</c> are different batches.</param>
    private async Task OnRunBatchRequestedAsync(
        string name, string? ownerPath, WorkspaceRootViewModel? root)
    {
        if (root is null)
        {
            return;
        }

        var qualified = ownerPath is { Length: > 0 } ? $"{ownerPath}@{name}" : $"@{name}";

        try
        {
            var resolved = await _batchPlanner.ExpandAsync(root.Workspace, name, ownerPath);

            _runDialog.Show(
                resolved.Plan,
                root.Workspace,
                EnvironmentManager.ActiveEnvironment,
                [.. EnvironmentManager.Environments],
                qualified,
                resolved.Batch);
        }
        catch (Exception ex)
        {
            // A batch naming an endpoint that is not there still RUNS - those steps error. Reaching
            // here means the batch itself could not be read at all.
            StatusLog.LogError($"Could not run \"{qualified}\": {ex.Message}");
        }
    }

    /// <summary>The endpoint a batch node belongs to, as a path relative to <c>collections/</c> - the
    /// form a selector and the planner both use.</summary>
    private static string? EndpointPathOf(WorkspaceNodeViewModel batch, WorkspaceRootViewModel root)
    {
        // <endpoint>/batches/<name>.json, so the endpoint is two levels up.
        var endpoint = Path.GetDirectoryName(Path.GetDirectoryName(batch.FullPath));
        if (endpoint is null)
        {
            return null;
        }

        return Path.GetRelativePath(Path.Combine(root.FullPath, "collections"), endpoint)
            .Replace('\\', '/');
    }

    /// <summary>
    /// Opens the environment-comparison window for a selected folder or request.
    ///
    /// <para>Here rather than on the explorer for the same reason as <see cref="OnRunRequested"/>, and
    /// more so: this one needs EVERY environment, not just the active one, because choosing the pair is
    /// the question the window exists to ask.</para>
    /// </summary>
    private void OnCompareEnvironmentsRequested(WorkspaceNodeViewModel node)
    {
        var root = WorkspaceExplorer.Roots.FirstOrDefault(
                       r => node.FullPath.StartsWith(r.FullPath, StringComparison.OrdinalIgnoreCase))
                   ?? WorkspaceExplorer.ActiveRoot;

        if (root is null)
        {
            return;
        }

        // Two environments are needed for there to be a comparison at all, and saying so beats a window
        // whose Run button is disabled for a reason nobody can see.
        if (EnvironmentManager.Environments.Count < 2)
        {
            StatusLog.Log("Comparing environments needs two of them - this workspace has "
                          + $"{EnvironmentManager.Environments.Count}.");
            return;
        }

        var plan = RunPlan.From(node.ToTreeNode());
        if (plan.IsEmpty)
        {
            StatusLog.Log($"Nothing to compare in \"{node.Name}\" - it holds no requests.");
            return;
        }

        _comparisonDialog.Show(plan, root.Workspace, [.. EnvironmentManager.Environments], node.Name);
    }

    [RelayCommand]
    private void ToggleLog() => IsLogVisible = !IsLogVisible;

    /// <summary>Raised by Ctrl+P; the shell puts the caret in the left pane's filter box.</summary>
    public event Action? FilterFocusRequested;

    /// <summary>Raised by Ctrl+Shift+P; the shell opens the palette over the window.</summary>
    public event Action<CommandPaletteViewModel>? PaletteRequested;

    /// <summary>Raised by the palette's About entry; the shell shows the diagnostics window.</summary>
    public event Action? AboutRequested;

    /// <summary>Raised by Ctrl+, or the palette; the shell shows the settings window.</summary>
    public event Action? SettingsRequested;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    // ---- The two shortcuts the docs already promised -----------------------------------------------
    //
    // docs/api-studio.md's Keyboard table has listed Ctrl+Enter as Send and Ctrl+S as Save since it was
    // written. Neither appeared in any KeyBindings block or key handler: the two most-used actions in
    // an API client had no shortcut at all, in an app with no menu bar to find one from. Fourth
    // documented instance of built-and-never-wired here - this time the thing that was never wired was
    // in the documentation rather than the code.

    /// <summary>
    /// Sends whatever is open, for Ctrl+Enter.
    ///
    /// <para>Silently does nothing when the canvas holds an environment or an auth profile: there is
    /// no request to send, and a shortcut that reports an error for being pressed on the wrong screen
    /// is worse than one that does nothing.</para>
    /// </summary>
    [RelayCommand]
    private async Task SendActiveAsync()
    {
        if (ActiveRequest is { } request)
        {
            await request.SendCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Saves whatever is open, for Ctrl+S - a request, an environment or an auth profile.
    ///
    /// <para>All three, not just requests: Ctrl+S is muscle memory, and one that works on two screens
    /// out of three is worse than none, because the two that work teach you to trust it.</para>
    /// </summary>
    [RelayCommand]
    private async Task SaveActiveAsync()
    {
        if (ActiveEditor is ISaveableEditor editor)
        {
            await editor.SaveAsync();
        }
    }

    /// <summary>Closes the active workspace tab, for Ctrl+W.</summary>
    [RelayCommand]
    private void CloseActiveWorkspace()
    {
        if (WorkspaceExplorer.ActiveRoot is { } root)
        {
            WorkspaceExplorer.CloseWorkspaceCommand.Execute(root);
        }
    }

    /// <summary>
    /// Whether the open editor has unsaved changes, for the Save button's own marker.
    ///
    /// <para>The dirty state was tracked and shown only as a dot on the tree row - which is the one
    /// place you are not looking while typing into the editor. The Save button looked identical
    /// whether or not there was anything to save.</para>
    /// </summary>
    public bool IsActiveDirty => ActiveRequest?.IsDirty == true;

    /// <summary>
    /// Whether there is a response to show, so the shell can give the whole canvas to the editor
    /// until there is.
    ///
    /// <para>Before the first send the response pane was three stacked empty states for one message:
    /// a strip saying "No response yet", four view tabs that could do nothing, and an empty editor
    /// showing line number 1. Collapsing it outright roughly doubles the request editor on the screen
    /// people spend the most time on. The splitter stays, so it can be dragged back at any time.</para>
    /// </summary>
    public bool HasResponse => ActiveRequest?.Response.HasResponse == true;

    /// <summary>Diagnostics for the About window. Built here because the shell owns the log and the
    /// clipboard; the window itself only displays what it is given.</summary>
    public AboutViewModel CreateAbout() => new(_clipboard, _logSink, _policy, StatusLog);

    /// <summary>
    /// The settings window's context. Built here for the same reason About's is: the shell owns the
    /// services, and the window only displays what it is given.
    ///
    /// <para>Saving re-applies the theme. Settings is the only place it is chosen now - it used to be
    /// a switcher pinned to the sidebar footer, which applied as it changed - so without this the
    /// choice would be written to the file and not appear until the next launch, which reads as the
    /// setting not working.</para>
    /// </summary>
    public SettingsViewModel CreateSettings()
    {
        var settings = new SettingsViewModel(_appSettings!, _policy);

        settings.Saved += () => LeftPane.Theme.Initialize();

        return settings;
    }

    [RelayCommand]
    private void OpenPalette() => PaletteRequested?.Invoke(new CommandPaletteViewModel(PaletteEntries()));

    /// <summary>
    /// Everything the palette offers: the shell's own commands, then every request in every open
    /// workspace.
    ///
    /// <para>Requests are included because "open the request called X" is the commonest thing anyone
    /// wants and, before the tree filter existed, took scrolling. Built fresh each time the palette
    /// opens rather than cached - workspaces come and go, and a stale palette entry that opens a
    /// deleted request is worse than a moment's work.</para>
    /// </summary>
    private IEnumerable<PaletteEntry> PaletteEntries()
    {
        yield return new PaletteEntry("New Request", "Command", "Ctrl+N",
            () => { WorkspaceExplorer.NewRequestCommand.Execute(null); return Task.CompletedTask; });

        yield return new PaletteEntry("New Folder", "Command", "Ctrl+Shift+N",
            () => { WorkspaceExplorer.NewFolderCommand.Execute(null); return Task.CompletedTask; });

        // The palette is the discovery surface, and everything below had lived only in the tree's
        // right-click menu - which you have to already know to look in.
        yield return new PaletteEntry("New scratch request", "Command", null,
            () => WorkspaceExplorer.NewScratchRequestCommand.ExecuteAsync(null));

        // Offered only where they can do anything, matching the menu's own rules: a case and a batch
        // need an endpoint selected, and a move needs somewhere to move to.
        if (WorkspaceExplorer.CanAddCase)
        {
            yield return new PaletteEntry("New Case", "Command", null,
                () => { WorkspaceExplorer.NewCaseCommand.Execute(null); return Task.CompletedTask; });
        }

        if (WorkspaceExplorer.CanAddBatch)
        {
            yield return new PaletteEntry("New Batch", "Command", null,
                () => { WorkspaceExplorer.NewBatchCommand.Execute(null); return Task.CompletedTask; });
        }

        if (WorkspaceExplorer.CanMove)
        {
            foreach (var target in WorkspaceExplorer.MoveTargets)
            {
                var destination = target;
                yield return new PaletteEntry(
                    $"Move to workspace: {destination.Workspace.Manifest.Name}", "Command", null,
                    () => WorkspaceExplorer.MoveToWorkspaceCommand.ExecuteAsync(destination));
            }
        }

        if (WorkspaceExplorer.UsesRequests && WorkspaceExplorer.ActiveRoot is not null)
        {
            yield return new PaletteEntry("Convert to endpoints...", "Command", null,
                () => WorkspaceExplorer.ConvertToEndpointsCommand.ExecuteAsync(null));
        }

        yield return new PaletteEntry("New Workspace...", "Command", null,
            () => WorkspaceExplorer.NewWorkspaceCommand.ExecuteAsync(null));

        yield return new PaletteEntry("Open Workspace...", "Command", null,
            () => WorkspaceExplorer.OpenWorkspaceDirectoryCommand.ExecuteAsync(null));

        yield return new PaletteEntry("Import OpenAPI / Swagger...", "Command", null,
            () => WorkspaceExplorer.ImportOpenApiCommand.ExecuteAsync(null));

        yield return new PaletteEntry("Import Postman collection...", "Command", null,
            () => WorkspaceExplorer.ImportPostmanCommand.ExecuteAsync(null));

        yield return new PaletteEntry("Import Postman environment...", "Command", null,
            () => WorkspaceExplorer.ImportPostmanEnvironmentCommand.ExecuteAsync(null));

        yield return new PaletteEntry("Import from curl...", "Command", null,
            () => WorkspaceExplorer.ImportCurlCommand.ExecuteAsync(null));

        yield return new PaletteEntry("Run selection", "Command", "Ctrl+R",
            () => { RunActiveCommand.Execute(null); return Task.CompletedTask; });

        // Where the request editor's one-item overflow menu went. Offered only with a request open,
        // because with anything else on the canvas there is nothing to copy - a palette entry that
        // does nothing when picked is worse than one that is absent.
        if (ActiveRequest is { } copyable)
        {
            yield return new PaletteEntry("Copy as cURL", "Command", null,
                () => copyable.CopyAsCurlCommand.ExecuteAsync(null));
        }

        yield return new PaletteEntry("Find in response", "Command", "Ctrl+F",
            () => { FindInResponseCommand.Execute(null); return Task.CompletedTask; });

        yield return new PaletteEntry("Filter requests", "Command", "Ctrl+P",
            () => { FocusFilterCommand.Execute(null); return Task.CompletedTask; });

        yield return new PaletteEntry("Toggle Status & Log", "Command", "Ctrl+`",
            () => { ToggleLogCommand.Execute(null); return Task.CompletedTask; });

        yield return new PaletteEntry("Settings...", "Command", "Ctrl+,",
            () => { SettingsRequested?.Invoke(); return Task.CompletedTask; });

        yield return new PaletteEntry("About Fubar API Studio", "Command", null,
            () => { AboutRequested?.Invoke(); return Task.CompletedTask; });

        foreach (var root in WorkspaceExplorer.Roots)
        {
            foreach (var request in Requests(root))
            {
                var path = request.FullPath;
                yield return new PaletteEntry(request.Name, root.Name, null, () => OpenRequestAsync(path));
            }
        }
    }

    private static IEnumerable<WorkspaceNodeViewModel> Requests(WorkspaceNodeViewModel node)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsDirectory)
            {
                yield return child;
            }

            foreach (var descendant in Requests(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Raised by Ctrl+F; the shell opens the response editor's find bar.</summary>
    public event Action? FindRequested;

    [RelayCommand]
    private void FocusFilter() => FilterFocusRequested?.Invoke();

    /// <summary>
    /// Ctrl+F. An event rather than something the view model does itself, because the find bar belongs
    /// to AvaloniaEdit and lives inside a control - a view model that reached for it would be reaching
    /// into the view.
    /// </summary>
    [RelayCommand]
    private void FindInResponse()
    {
        if (ActiveRequest?.Response.HasResponse == true)
        {
            FindRequested?.Invoke();
        }
    }

    /// <summary>Ctrl+R - runs whatever the left pane has selected, or the whole workspace when nothing
    /// is. Same path as the context menu, so the two cannot disagree about what "run" means.</summary>
    [RelayCommand]
    private void RunActive()
    {
        var node = WorkspaceExplorer.SelectedNode ?? WorkspaceExplorer.ActiveRoot;
        if (node is not null)
        {
            OnRunRequested(node);
        }
    }

    /// <summary>Whenever the active workspace tab changes (opened, switched, or closed down to
    /// none), reload the Environments/Auth Profiles groups and active-environment badge to match -
    /// or clear them if no workspace is open at all.</summary>
    private void OnWorkspaceExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceExplorerViewModel.ActiveRoot))
        {
            return;
        }

        if (WorkspaceExplorer.ActiveRoot is { } activeRoot)
        {
            _ = ActivateWorkspaceContextAsync(activeRoot.Workspace);
        }
        else
        {
            EnvironmentManager.ClearWorkspace();
            LeftPane.EnvironmentsSection.SetWorkspace(null);
            _ = LeftPane.AuthProfilesSection.SetWorkspaceAsync(null);
            _ = LeftPane.BatchesSection.SetWorkspaceAsync(null);
        }
    }

    /// <summary>Closing a workspace tab drops whatever the main canvas was showing if it belonged
    /// to that workspace - there's nothing sensible left to show it against.</summary>
    private void OnWorkspaceClosed(Workspace closed)
    {
        var activeWorkspace = ActiveEditor switch
        {
            RequestEditorViewModel r => r.Workspace,
            EnvironmentEditorViewModel e => e.Workspace,
            AuthProfileEditorViewModel a => a.Workspace,
            _ => null,
        };

        if (activeWorkspace is not null && string.Equals(activeWorkspace.RootPath, closed.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            ActiveEditor = null;
        }
    }

    /// <summary>Reloads the Environments/Auth Profiles groups (and the active-environment badge)
    /// for <paramref name="workspace"/> - called whenever the active workspace tab changes and
    /// whenever the active request switches to one belonging to a different workspace.</summary>
    private async Task ActivateWorkspaceContextAsync(Workspace workspace)
    {
        await EnvironmentManager.LoadForWorkspaceAsync(workspace);
        LeftPane.EnvironmentsSection.SetWorkspace(workspace);
        await LeftPane.AuthProfilesSection.SetWorkspaceAsync(workspace);
        await LeftPane.BatchesSection.SetWorkspaceAsync(workspace);
    }

    /// <summary>
    /// Loads <paramref name="filePath"/> into the single main-canvas surface, replacing whatever was
    /// open. Re-activating the already-open request is a no-op.
    ///
    /// <para>There is nowhere to keep a second buffer, so replacing a dirty editor DESTROYS the edits -
    /// which is why it now asks. It used to write a line to the status log and carry on, and that log
    /// was collapsed by default, so the only notice of losing work went somewhere invisible.</para>
    /// </summary>
    /// <summary>
    /// A draft was pointed at a different file before it was ever saved, so the editor open on it has
    /// to follow - otherwise Save writes the name that was replaced.
    /// </summary>
    private void OnDraftRenamed(string from, string to)
    {
        if (ActiveRequest is { } request
            && string.Equals(request.FilePath, from, StringComparison.OrdinalIgnoreCase))
        {
            request.FilePath = to;
            return;
        }

        // An endpoint's node is its DIRECTORY; the editor holds the endpoint.json inside it.
        var endpointFile = Path.Combine(to, Core.Workspaces.IEndpointStore.EndpointFileName);

        if (ActiveRequest is { } endpoint
            && string.Equals(
                endpoint.FilePath,
                Path.Combine(from, Core.Workspaces.IEndpointStore.EndpointFileName),
                StringComparison.OrdinalIgnoreCase))
        {
            endpoint.FilePath = endpointFile;
        }
    }

    /// <summary>
    /// Something open was moved to another workspace, so the file the canvas is showing is no longer
    /// there. Re-opened at its new home rather than closed: the move was a filing decision, not a
    /// decision to stop working on it - and re-opening is also what picks up the new workspace's
    /// environments, auth profiles and inherited rules, which are the whole reason it moved.
    /// </summary>
    private void OnPathMoved(string from, string to)
    {
        if (ActiveRequest is not { } open)
        {
            return;
        }

        // An endpoint's node is its directory; the editor holds the endpoint.json inside it. A folder
        // move carries whatever was open along with everything else under it.
        var endpointFile = Path.Combine(from, Core.Workspaces.IEndpointStore.EndpointFileName);

        if (string.Equals(open.FilePath, from, StringComparison.OrdinalIgnoreCase))
        {
            _ = OpenRequestAsync(to);
        }
        else if (string.Equals(open.FilePath, endpointFile, StringComparison.OrdinalIgnoreCase))
        {
            _ = OpenRequestAsync(Path.Combine(to, Core.Workspaces.IEndpointStore.EndpointFileName));
        }
        else if (open.FilePath.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            _ = OpenRequestAsync(to + open.FilePath[from.Length..]);
        }
    }

    /// <summary>The name a drafted document opens under, taken from the path reserved for it.</summary>
    private static string NameOf(string filePath) =>
        string.Equals(
            Path.GetFileName(filePath),
            Core.Workspaces.IEndpointStore.EndpointFileName,
            StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "New Endpoint"
            : Path.GetFileNameWithoutExtension(filePath);

    /// <summary>
    /// Opens a request, endpoint or case in the editor, or brings it forward when it is already the
    /// one open. Only one is open at a time, so this is also where the outgoing editor is asked about
    /// unsaved changes.
    /// </summary>
    /// <param name="filePath">The file to open. It need not exist yet - see <paramref name="isDraft"/>.</param>
    /// <param name="isDraft">A request or endpoint that has not been written yet. Its file does not
    /// exist, so the editor starts on a blank one and is dirty from the outset - it IS unsaved, and the
    /// tree says so beside it until the first Save creates the file.</param>
    public async Task OpenRequestAsync(string filePath, bool isDraft = false)
    {
        if (ActiveRequest is { } current && string.Equals(current.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await ConfirmDiscardingActiveEditAsync())
        {
            return;
        }

        var workspace = WorkspaceExplorer.FindWorkspaceForPath(filePath);
        if (workspace is null)
        {
            StatusLog.LogError($"Could not find the owning workspace for \"{filePath}\".");
            return;
        }

        try
        {
            // Always re-read: cheap, and keeps the environment/auth-profile lists honest if they
            // changed on disk since the last open.
            await ActivateWorkspaceContextAsync(workspace);

            var request = isDraft
                ? new RequestModel { Name = NameOf(filePath) }
                : await _workspaceService.LoadRequestAsync(filePath);

            var provider = _protocolRegistry.Resolve(request.Kind);
            var editor = _editorFactory.CreateRequestEditor(request, filePath, provider, workspace);

            editor.IsDirty = isDraft;
            editor.Saved += () => WorkspaceExplorer.DraftSaved(editor.FilePath);
            ActiveEditor = editor;
        }
        catch (Exception ex)
        {
            StatusLog.LogError($"Failed to open \"{filePath}\": {ex.Message}");
        }
    }

    /// <summary>
    /// Opens one case of an endpoint in the main canvas.
    /// </summary>
    /// <remarks>
    /// The endpoint is loaded too, and not only to display: the case editor seeds its path-parameter
    /// grid from the endpoint's URL, so a case opens with the questions it has to answer rather than
    /// an empty grid.
    /// </remarks>
    /// <param name="isDraft">A case that has not been written yet. Its file does not exist, so the
    /// editor starts on a blank one and is dirty from the outset - it IS unsaved, and the tree says so
    /// beside it until the first Save creates the file.</param>
    public async Task OpenCaseAsync(string caseFilePath, bool isDraft = false)
    {
        if (!await ConfirmDiscardingActiveEditAsync())
        {
            return;
        }

        var workspace = WorkspaceExplorer.FindWorkspaceForPath(caseFilePath);
        if (workspace is null)
        {
            StatusLog.LogError($"Could not find the owning workspace for \"{caseFilePath}\".");
            return;
        }

        try
        {
            await ActivateWorkspaceContextAsync(workspace);

            var endpointDirectory = _endpointStore.EndpointDirectoryOf(caseFilePath)
                ?? throw new InvalidOperationException("this case is not inside an endpoint");

            var endpoint = await _workspaceService.LoadRequestAsync(
                Path.Combine(endpointDirectory, Core.Workspaces.IEndpointStore.EndpointFileName));

            var endpointCase = isDraft
                ? new EndpointCase { Name = Path.GetFileNameWithoutExtension(caseFilePath) }
                : await _endpointStore.LoadCaseAsync(caseFilePath);

            var editor = _editorFactory.CreateCaseEditor(endpointCase, endpoint, caseFilePath, workspace);

            // A draft has nothing on disk to be clean against, so it opens dirty. Ctrl+S and the
            // unsaved prompt then treat it like any other unsaved editor, which is the point.
            editor.IsDirty = isDraft;

            // The path it was OPENED at, not the one it saved to: renaming on save writes a different
            // file, and the draft node still reserving the old one has to be retired by name.
            editor.Saved += () => WorkspaceExplorer.DraftSaved(caseFilePath);
            ActiveEditor = editor;
        }
        catch (Exception ex)
        {
            StatusLog.LogError($"Failed to open \"{caseFilePath}\": {ex.Message}");
        }
    }

    /// <summary>
    /// Asks before the active editor's unsaved changes are thrown away, and returns whether to go
    /// ahead. Save writes first; Cancel and a dismissed dialog both mean don't.
    ///
    /// <para>A dismissed dialog counting as "discard" is exactly the bug a prompt exists to prevent,
    /// so <c>ChooseAsync</c>'s -1 is treated as Cancel - the same rule Fubar Diff states and pins with
    /// its own tests. With no confirmation service wired at all the answer is also no: refusing to
    /// switch is recoverable, silently destroying an edit is not.</para>
    /// </summary>
    public async Task<bool> ConfirmDiscardingActiveEditAsync()
    {
        if (ActiveRequest is not { IsDirty: true } dirty)
        {
            return true;
        }

        var choice = await UnsavedChangesPrompt.AskAsync(
            isDirty: true,
            dirty.Name,
            _confirmation,
            async () =>
            {
                await dirty.SaveCommand.ExecuteAsync(null);
                return !dirty.IsDirty;
            });

        if (choice == UnsavedChoice.Keep)
        {
            StatusLog.LogWarning($"Kept the unsaved changes to \"{dirty.Name}\".");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Opens a folder's own settings in the main canvas - the level almost every shared rule belongs
    /// at, and until now the only one you had to edit by hand.
    /// </summary>
    private async Task OpenFolderEditorAsync(string folderPath, FolderConfig config)
    {
        if (WorkspaceExplorer.ActiveRoot is not { } root)
        {
            return;
        }

        try
        {
            await ActivateWorkspaceContextAsync(root.Workspace);

            var profiles = await _authProfiles.LoadAuthProfilesAsync(root.FullPath);
            var editor = _editorFactory.CreateFolderEditor(config, folderPath, root.Workspace, profiles);

            // A folder's rules are inherited by everything under it, so a save changes what those
            // requests resolve to - the tree is rebuilt rather than left showing the old answer.
            editor.Saved += () => WorkspaceExplorer.RefreshRootFor(folderPath);
            ActiveEditor = editor;
        }
        catch (Exception ex)
        {
            StatusLog.LogError($"Could not open \"{Path.GetFileName(folderPath)}\": {ex.Message}");
        }
    }

    /// <summary>Opens a batch in the main canvas - wired to both
    /// <see cref="BatchesSectionViewModel.EditRequested"/> and the tree's, each of which reads it
    /// fresh from disk first: an editor opened on a stale copy would save it back over whatever has
    /// happened to the file since.</summary>
    private void OpenBatchEditor(string filePath, Batch batch)
    {
        if (WorkspaceExplorer.ActiveRoot is not { } root)
        {
            return;
        }

        var editor = _editorFactory.CreateBatchEditor(
            batch, filePath, root.Workspace, [.. EnvironmentManager.Environments.Select(e => e.Name)]);

        // A batch is unsaved until its first Save, and until then there is no file to be clean
        // against - the same rule a drafted case follows.
        editor.IsDirty = !File.Exists(filePath);

        editor.Saved += () =>
        {
            // Both homes: the Left Pane lists the workspace's own, the tree holds an endpoint's - and
            // a rename moves the file, so each is rebuilt rather than relabelled.
            _ = LeftPane.BatchesSection.ReloadAsync();
            WorkspaceExplorer.DraftSaved(filePath);
        };

        ActiveEditor = editor;
    }

    /// <summary>Opens <paramref name="environment"/>'s variables for editing in the main canvas -
    /// wired to <see cref="EnvironmentsSectionViewModel.EditRequested"/>.</summary>
    private void OpenEnvironmentEditor(WorkspaceEnvironment environment)
    {
        if (EnvironmentManager.ActiveWorkspace is not { } workspace)
        {
            return;
        }

        var editor = _editorFactory.CreateEnvironmentEditor(environment, workspace);
        editor.Saved += () => _ = ActivateWorkspaceContextAsync(workspace);
        ActiveEditor = editor;
    }

    /// <summary>Opens <paramref name="profile"/> for editing in the main canvas - wired to
    /// <see cref="AuthProfilesSectionViewModel.EditRequested"/>.</summary>
    private void OpenAuthProfileEditor(AuthProfile profile)
    {
        if (EnvironmentManager.ActiveWorkspace is not { } workspace)
        {
            return;
        }

        var editor = _editorFactory.CreateAuthProfileEditor(profile, workspace);
        editor.Saved += () => _ = ActivateWorkspaceContextAsync(workspace);
        ActiveEditor = editor;
    }

    /// <summary>Mirrors whichever request is active onto its Left Pane tree node's <c>IsDirty</c>
    /// dot, unsubscribing from the previous one first.</summary>
    partial void OnActiveEditorChanged(object? value)
    {
        OnPropertyChanged(nameof(ActiveRequest));

        // Keep the Left Pane's row highlighting in sync - exactly one of these (or none) is
        // non-null/selected at a time, mirroring ActiveEditor's own "never more than one thing
        // open" rule. Without clearing SelectedNode, the tree would keep showing its last-clicked
        // request as "selected" even after switching to an environment/auth profile elsewhere.
        LeftPane.EnvironmentsSection.SelectedEnvironmentId = (value as EnvironmentEditorViewModel)?.EnvironmentId;
        LeftPane.AuthProfilesSection.SelectedProfileId = (value as AuthProfileEditorViewModel)?.ProfileId;

        // Cleared only for the two surfaces that are NOT in the tree. A case and a batch are rows in
        // it, so opening one used to deselect the very thing being edited - the highlight vanished,
        // and "New Case"/"New Batch"/"Move to workspace" all key off the selection, so they stopped
        // being offered the moment you opened the thing you wanted to add to.
        if (value is EnvironmentEditorViewModel or AuthProfileEditorViewModel)
        {
            WorkspaceExplorer.SelectedNode = null;
        }

        if (_dirtyTrackedRequest is { } previous)
        {
            previous.PropertyChanged -= OnActiveRequestPropertyChanged;
            previous.Response.PropertyChanged -= OnActiveResponsePropertyChanged;
            ClearDirtyMarker(previous.FilePath);
            previous.Dispose();
        }

        _dirtyTrackedRequest = value as RequestEditorViewModel;
        OnPropertyChanged(nameof(IsActiveDirty));
        OnPropertyChanged(nameof(HasResponse));

        if (_dirtyTrackedRequest is { } current)
        {
            current.PropertyChanged += OnActiveRequestPropertyChanged;

            // The response pane collapses until there is something to show, so the shell has to hear
            // about the FIRST response as well as about edits.
            current.Response.PropertyChanged += OnActiveResponsePropertyChanged;
            SyncDirtyMarker(current);
        }
    }

    private void OnActiveResponsePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResponsePanelViewModel.HasResponse))
        {
            OnPropertyChanged(nameof(HasResponse));
        }
    }

    private void OnActiveRequestPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RequestEditorViewModel.IsDirty) && sender is RequestEditorViewModel request)
        {
            SyncDirtyMarker(request);
        }
    }

    private void SyncDirtyMarker(RequestEditorViewModel request)
    {
        // The editor's own marker, as well as the tree's. The dot on the tree row is in the one place
        // you are NOT looking while typing into the editor, which is where the question "have I saved
        // this?" actually gets asked.
        OnPropertyChanged(nameof(IsActiveDirty));

        if (WorkspaceExplorer.FindNodeByPath(request.FilePath) is { } node)
        {
            node.IsDirty = request.IsDirty;
        }
    }

    private void ClearDirtyMarker(string filePath)
    {
        if (WorkspaceExplorer.FindNodeByPath(filePath) is { } node)
        {
            node.IsDirty = false;
        }
    }
}
