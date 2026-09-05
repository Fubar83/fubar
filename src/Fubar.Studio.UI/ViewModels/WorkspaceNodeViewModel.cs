using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// One folder or request file node in the Workspace Explorer TreeView. Wraps the immutable
/// <see cref="WorkspaceTreeNode"/> snapshot from <c>IWorkspaceService.BuildCollectionsTree</c> in
/// a mutable, bindable form that <see cref="SyncChildren"/> reconciles in place on every refresh -
/// preserving node identity (and so selection state) for anything that
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

    /// <summary>
    /// Projects this node and its descendants back into the immutable <see cref="WorkspaceTreeNode"/>
    /// shape, so domain code (<c>RunPlan</c>) can work on the tree without knowing about view models.
    ///
    /// <para>Built from the VIEW MODEL tree rather than by re-scanning the directory, deliberately: a
    /// run sends requests in the order they appear here, and taking that order from anywhere else would
    /// let the two disagree. What the user sees is the contract.</para>
    /// </summary>
    /// <remarks>
    /// Projects every child, filtered or not. A run must send what the collection HOLDS, not what the
    /// left pane happens to be showing while someone types in the filter box - the filter is a way to
    /// find things, never a way to select them.
    /// </remarks>
    public WorkspaceTreeNode ToTreeNode() =>
        new(Name, FullPath, IsDirectory, [.. Children.Select(c => c.ToTreeNode())],
            IsDirectory ? null : new RequestSummary(Method ?? "GET", HasAuthOverride, Url, SendsNoAuth));

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

    /// <summary>
    /// True when this request's own auth is explicitly None - it sends nothing, on purpose.
    ///
    /// <para>Distinguished from any other override because the tree used to badge it "Auth", which
    /// says the opposite of the truth: the one request that must go out unauthenticated looked
    /// exactly like the ones carrying a token.</para>
    /// </summary>
    [ObservableProperty]
    public partial bool SendsNoAuth { get; set; }

    /// <summary>True while this request is the active canvas and has unsaved edits.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    /// <summary>The request's URL, null for a folder. Carried so the filter can match a host or a path
    /// segment, not only a file name - the OpenAPI import names files after operation ids, which are
    /// often the least memorable part of an endpoint.</summary>
    [ObservableProperty]
    public partial string? Url { get; set; }

    /// <summary>Whether this node survives the current filter. True when there is no filter.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    /// <summary>
    /// Applies <paramref name="filter"/> to this node and its descendants, returning whether anything
    /// here survived.
    ///
    /// <para>A folder matches when ANY descendant does. It used to force itself open as well, against
    /// "a filtered tree that stays collapsed shows the user nothing" - which was never actually a risk
    /// here, because that <c>IsExpanded</c> was bound to no container and the force-expand did nothing
    /// at all. The tree is always open now, so there is nothing left to force. A folder that matches by
    /// its own name keeps all its children, because "show me the Orders folder" means the folder, not
    /// an empty one.</para>
    /// </summary>
    public bool ApplyFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            IsVisible = true;

            foreach (var child in Children)
            {
                child.ApplyFilter(null);
            }

            return true;
        }

        var selfMatches = Matches(filter);

        var anyChildMatches = false;
        foreach (var child in Children)
        {
            // Not short-circuited: every child needs its own visibility set, so this must not stop at
            // the first match.
            anyChildMatches |= child.ApplyFilter(selfMatches ? null : filter);
        }

        IsVisible = selfMatches || anyChildMatches;

        return IsVisible;
    }

    private bool Matches(string filter) =>
        Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (Url?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
        || (Method?.StartsWith(filter, StringComparison.OrdinalIgnoreCase) ?? false);

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
                    SendsNoAuth = node.RequestSummary?.SendsNoAuth ?? false,
                    Url = node.RequestSummary?.Url,
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
                existing.Url = node.RequestSummary?.Url;
                existing.HasAuthOverride = node.RequestSummary?.HasAuthOverride ?? false;
                existing.SendsNoAuth = node.RequestSummary?.SendsNoAuth ?? false;
                existing.SyncChildren(node.Children);
            }
        }
    }
}
