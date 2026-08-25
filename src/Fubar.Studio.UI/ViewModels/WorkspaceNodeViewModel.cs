using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// One folder or request file node in the Workspace Explorer TreeView. Wraps the immutable
/// <see cref="WorkspaceTreeNode"/> snapshot from <c>IWorkspaceService.BuildCollectionsTree</c> in
/// a mutable, bindable form that <see cref="SyncChildren"/> reconciles in place on every refresh -
/// preserving node identity (and so <see cref="IsExpanded"/>/selection state) for anything that
/// didn't actually change on disk, rather than rebuilding the whole subtree. For request file
/// nodes, <see cref="Method"/>/<see cref="HasAuthOverride"/> back the Left Pane's method/auth
/// badges (LeftPane.md §5) and <see cref="IsDirty"/> its unsaved-changes dot, kept live by
/// MainViewModel while this node's request is the active canvas.
/// </summary>
public partial class WorkspaceNodeViewModel : ViewModelBase
{
    public WorkspaceNodeViewModel(string name, string fullPath, bool isDirectory, int depth = 0)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Depth = depth;
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string FullPath { get; set; }

    public bool IsDirectory { get; }

    /// <summary>Nesting depth within the visible tree - 0 for a workspace's top-level
    /// collections/ entries, incrementing per folder level. Drives the Left Pane's own indent step
    /// (see <c>TreeLevelIndentConverter</c>) rather than relying on FluentTheme's built-in
    /// TreeViewItem indentation, which an app-level resource override couldn't reach.</summary>
    public int Depth { get; }

    public ObservableCollection<WorkspaceNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Inline-rename state: when true, the TreeView shows an editable TextBox instead of the label.</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditName { get; set; } = "";

    /// <summary>The request's HTTP method (e.g. "GET"), null for directory nodes.</summary>
    [ObservableProperty]
    public partial string? Method { get; set; }

    /// <summary>True when the request's Auth tab is set to something other than Inherit.</summary>
    [ObservableProperty]
    public partial bool HasAuthOverride { get; set; }

    /// <summary>True while this request is the active canvas and has unsaved edits.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>Reconciles <see cref="Children"/> against a freshly scanned snapshot, by path identity.</summary>
    protected void SyncChildren(IReadOnlyList<WorkspaceTreeNode> incoming)
    {
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (!incoming.Any(n => n.FullPath == Children[i].FullPath))
            {
                Children.RemoveAt(i);
            }
        }

        for (var i = 0; i < incoming.Count; i++)
        {
            var node = incoming[i];
            var existing = Children.FirstOrDefault(c => c.FullPath == node.FullPath);

            if (existing is null)
            {
                var child = new WorkspaceNodeViewModel(node.Name, node.FullPath, node.IsDirectory, Depth + 1)
                {
                    Method = node.RequestSummary?.Method,
                    HasAuthOverride = node.RequestSummary?.HasAuthOverride ?? false,
                };
                child.SyncChildren(node.Children);
                Children.Insert(Math.Min(i, Children.Count), child);
            }
            else
            {
                var currentIndex = Children.IndexOf(existing);
                if (currentIndex != i)
                {
                    Children.Move(currentIndex, i);
                }

                existing.Name = node.Name;
                existing.Method = node.RequestSummary?.Method;
                existing.HasAuthOverride = node.RequestSummary?.HasAuthOverride ?? false;
                existing.SyncChildren(node.Children);
            }
        }
    }
}
