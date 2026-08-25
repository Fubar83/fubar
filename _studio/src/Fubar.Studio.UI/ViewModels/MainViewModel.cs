using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Controls;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.History;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
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

    public MainViewModel(
        WorkspaceExplorerViewModel workspaceExplorer,
        EnvironmentManagerViewModel environmentManager,
        StatusLogViewModel statusLog,
        LeftPaneViewModel leftPane,
        IRequestStore workspaceService,
        IProtocolRegistry protocolRegistry,
        IEditorViewModelFactory editorFactory,
        ITabDragHost tabDragHost)
    {
        WorkspaceExplorer = workspaceExplorer;
        TabDragHost = tabDragHost;
        EnvironmentManager = environmentManager;
        StatusLog = statusLog;
        LeftPane = leftPane;
        _workspaceService = workspaceService;
        _protocolRegistry = protocolRegistry;
        _editorFactory = editorFactory;

        WorkspaceExplorer.PropertyChanged += OnWorkspaceExplorerPropertyChanged;
        WorkspaceExplorer.WorkspaceClosed += OnWorkspaceClosed;
        WorkspaceExplorer.WorkspaceContentImported += workspace => _ = ActivateWorkspaceContextAsync(workspace);

        WorkspaceExplorer.RequestFileActivated += path => _ = OpenRequestAsync(path);
        LeftPane.EnvironmentsSection.EditRequested += OpenEnvironmentEditor;
        LeftPane.AuthProfilesSection.EditRequested += OpenAuthProfileEditor;

        StatusLog.Log("Fubar shell ready.");
    }

    [RelayCommand]
    private void ToggleLog() => IsLogVisible = !IsLogVisible;

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
    /// Loads <paramref name="filePath"/> into the single main-canvas surface, replacing whatever
    /// was open. Re-activating the already-open request is a no-op. Unlike the old tabbed dock,
    /// there's nowhere to keep a second buffer around - switching away from unsaved changes just
    /// logs a warning rather than blocking, matching the single-canvas/no-tabs design.
    /// </summary>
    public async Task OpenRequestAsync(string filePath)
    {
        if (ActiveRequest is { } current && string.Equals(current.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (ActiveRequest is { IsDirty: true } dirty)
        {
            StatusLog.Log($"Switched away from \"{dirty.Name}\" with unsaved changes (Ctrl+S to save before switching).");
        }

        var workspace = WorkspaceExplorer.FindWorkspaceForPath(filePath);
        if (workspace is null)
        {
            StatusLog.Log($"Could not find the owning workspace for \"{filePath}\".");
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
            StatusLog.Log($"Failed to open \"{filePath}\": {ex.Message}");
        }
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
