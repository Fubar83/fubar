using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// One open workspace, shown as its own Chrome-style tab in the title bar's <c>fc:TabStrip</c> and,
/// while active, as the Left Pane's tree/Environments/Auth Profiles context. Owns a
/// <see cref="FileSystemWatcher"/> over the whole
/// workspace directory so external changes - <c>git pull</c>, manual edits, branch switches -
/// refresh the tree without an app restart. Watcher events are debounced (multiple rapid
/// file-system events, e.g. from a branch switch, collapse into a single rescan) and marshaled to
/// the UI thread before touching any bound collection.
/// </summary>
public sealed partial class WorkspaceRootViewModel : WorkspaceNodeViewModel, IDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

    private readonly IRequestStore _workspaceService;
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _debounceTimer;

    public WorkspaceRootViewModel(Workspace workspace, IRequestStore workspaceService)
        : base(workspace.Manifest.Name, workspace.RootPath, isDirectory: true, depth: -1)
    {
        Workspace = workspace;
        _workspaceService = workspaceService;

        Refresh();

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = DebounceInterval,
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            Refresh();
        };

        _watcher = new FileSystemWatcher(workspace.RootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Renamed += OnFileSystemEvent;
        _watcher.EnableRaisingEvents = true;
    }

    public Workspace Workspace { get; private set; }

    /// <summary>Swaps in a freshly reloaded <see cref="Workspace"/> (same directory) after its
    /// manifest changed on disk - e.g. an OpenAPI import that added an active environment - so later
    /// context activations read the current manifest rather than the one loaded at open time.</summary>
    public void UpdateWorkspace(Workspace workspace) => Workspace = workspace;

    // Tab selection highlight and drag visuals used to live here as IsActive/IsDragging/
    // IsDragFloating/TabOpacity flags; they're now owned entirely by the reusable fc:TabStrip
    // (selection via SelectedItem<->WorkspaceExplorer.ActiveRoot, drag dimming via its own
    // container pseudo-classes), so this stays a pure domain model of one open workspace.

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });

    /// <summary>Rescans <c>collections/</c> from disk and reconciles it into the bound tree.</summary>
    public void Refresh() => SyncChildren(_workspaceService.BuildCollectionsTree(Workspace.RootPath));

    public void Dispose()
    {
        _watcher.Changed -= OnFileSystemEvent;
        _watcher.Created -= OnFileSystemEvent;
        _watcher.Deleted -= OnFileSystemEvent;
        _watcher.Renamed -= OnFileSystemEvent;
        _watcher.Dispose();
        _debounceTimer.Stop();
    }
}
