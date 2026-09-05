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
    private readonly IProtocolRegistry _protocolRegistry;
    private readonly IEditorViewModelFactory _editorFactory;
    private readonly IRunDialogService _runDialog;

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
        IProtocolRegistry protocolRegistry,
        IEditorViewModelFactory editorFactory,
        IRunDialogService runDialog,
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
        _protocolRegistry = protocolRegistry;
        _editorFactory = editorFactory;
        _runDialog = runDialog;
        _confirmation = confirmation;
        _clipboard = clipboard;
        _logSink = logSink;
        _policy = policy;
        _appSettings = appSettings;

        WorkspaceExplorer.PropertyChanged += OnWorkspaceExplorerPropertyChanged;
        WorkspaceExplorer.WorkspaceClosed += OnWorkspaceClosed;
        WorkspaceExplorer.WorkspaceContentImported += workspace => _ = ActivateWorkspaceContextAsync(workspace);

        WorkspaceExplorer.RequestFileActivated += path => _ = OpenRequestAsync(path);
        WorkspaceExplorer.RunRequested += OnRunRequested;
        LeftPane.EnvironmentsSection.EditRequested += OpenEnvironmentEditor;
        LeftPane.AuthProfilesSection.EditRequested += OpenAuthProfileEditor;

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

        var plan = RunPlan.From(node.ToTreeNode());
        if (plan.IsEmpty)
        {
            // Nothing to run is worth SAYING. A window listing nothing looks like a failure to load.
            StatusLog.Log($"Nothing to run in \"{node.Name}\" - it holds no requests.");
            return;
        }

        _runDialog.Show(plan, root.Workspace, EnvironmentManager.ActiveEnvironment, node.Name);
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
    }

    /// <summary>
    /// Loads <paramref name="filePath"/> into the single main-canvas surface, replacing whatever was
    /// open. Re-activating the already-open request is a no-op.
    ///
    /// <para>There is nowhere to keep a second buffer, so replacing a dirty editor DESTROYS the edits -
    /// which is why it now asks. It used to write a line to the status log and carry on, and that log
    /// was collapsed by default, so the only notice of losing work went somewhere invisible.</para>
    /// </summary>
    public async Task OpenRequestAsync(string filePath)
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

            var request = await _workspaceService.LoadRequestAsync(filePath);
            var provider = _protocolRegistry.Resolve(request.Kind);
            var editor = _editorFactory.CreateRequestEditor(request, filePath, provider, workspace);

            editor.Saved += () => WorkspaceExplorer.RefreshRootFor(editor.FilePath);
            ActiveEditor = editor;
        }
        catch (Exception ex)
        {
            StatusLog.LogError($"Failed to open \"{filePath}\": {ex.Message}");
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
        if (value is not RequestEditorViewModel)
        {
            WorkspaceExplorer.SelectedNode = null;
        }

        if (_dirtyTrackedRequest is { } previous)
        {
            previous.PropertyChanged -= OnActiveRequestPropertyChanged;
            ClearDirtyMarker(previous.FilePath);
            previous.Dispose();
        }

        _dirtyTrackedRequest = value as RequestEditorViewModel;

        if (_dirtyTrackedRequest is { } current)
        {
            current.PropertyChanged += OnActiveRequestPropertyChanged;
            SyncDirtyMarker(current);
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
