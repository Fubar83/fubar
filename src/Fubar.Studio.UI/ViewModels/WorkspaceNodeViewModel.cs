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
    private const string RequestExtension = ".json";

    public WorkspaceNodeViewModel(
        string name,
        string fullPath,
        bool isDirectory,
        WorkspaceNodeKind? kind = null)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Kind = kind ?? (isDirectory ? WorkspaceNodeKind.Folder : WorkspaceNodeKind.Request);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Name { get; set; }

    /// <summary>
    /// What the tree shows: <see cref="Name"/> without the <c>.json</c> a request file is stored as.
    /// How a workspace persists a request is not something the person reading the list has to carry,
    /// and the extension is the same six characters on every row - it distinguishes nothing while
    /// eating the width that the actual names need.
    ///
    /// <para>Only <c>.json</c>, and only on files: anything else on disk keeps its full name, because
    /// then the extension is telling you something.</para>
    /// </summary>
    public string DisplayName =>
        !IsDirectory && Name.EndsWith(RequestExtension, StringComparison.OrdinalIgnoreCase)
            ? Name[..^RequestExtension.Length]
            : Name;

    [ObservableProperty]
    public partial string FullPath { get; set; }

    public bool IsDirectory { get; }

    /// <summary>What this node is - folder, request, endpoint or case. A directory flag alone can no
    /// longer say: an endpoint is a directory and is not a folder.</summary>
    public WorkspaceNodeKind Kind { get; }

    /// <summary>An endpoint directory. Bound by the tree for its own glyph, and by the context menu,
    /// which offers "Add case" here and nowhere else.</summary>
    public bool IsEndpoint => Kind == WorkspaceNodeKind.Endpoint;

    /// <summary>One case of an endpoint.</summary>
    public bool IsCase => Kind == WorkspaceNodeKind.Case;

    /// <summary>A plain folder - which an endpoint is NOT, though both are directories. What the
    /// "New folder"/"New request" menu items key off, so neither is offered inside an endpoint.</summary>
    public bool IsPlainFolder => Kind == WorkspaceNodeKind.Folder;

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
            IsDirectory && !IsEndpoint ? null : new RequestSummary(Method ?? "GET", HasAuthOverride, Url, SendsNoAuth))
        {
            // Carried, not re-derived: RunPlan expands an endpoint into its cases and a folder into
            // its descendants, and getting that from the directory flag alone would send a case file
            // as if it were a request.
            Kind = Kind,
        };

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
    /// Whether this folder is unfolded. Two-way bound to the row's container, so folding survives the
    /// refresh that <see cref="SyncChildren"/> runs on every file-system change - the container may be
    /// rebuilt, this node is not.
    ///
    /// <para>Open to begin with: a workspace is a few dozen requests, and opening one to a wall of
    /// folded folders hides the only thing the pane is for.</para>
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>
    /// Applies <paramref name="filter"/> to this node and its descendants, returning whether anything
    /// here survived.
    ///
    /// <para>A folder matches when ANY descendant does, and unfolds itself so the match is on screen -
    /// a filter that finds a request inside a folded folder and leaves it folded has shown you nothing.
    /// This is live again now that folding is: it was written once before, against an
    /// <see cref="IsExpanded"/> that was bound to no container, and did nothing at all. A folder that
    /// matches by its own name keeps all its children, because "show me the Orders folder" means the
    /// folder, not an empty one.</para>
    ///
    /// <para>Clearing the filter leaves everything it opened open. Re-folding would undo the folding
    /// the person did by hand, and there is no way to tell the two apart afterwards.</para>
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

        if (anyChildMatches)
        {
            IsExpanded = true;
        }

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

            // A folder becomes an endpoint the moment an endpoint.json lands in it, and Kind is not
            // something a node can change its mind about - so that one is rebuilt rather than
            // reconciled. Everything else keeps its identity, and its selection and folding with it.
            if (existing is not null && existing.Kind != node.Kind)
            {
                Children.Remove(existing);
                existing = null;
            }

            if (existing is null)
            {
                var child = new WorkspaceNodeViewModel(node.Name, node.FullPath, node.IsDirectory, node.Kind)
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
