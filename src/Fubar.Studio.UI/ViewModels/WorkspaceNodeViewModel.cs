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
    /// What the TREE shows beneath this node, which is not always what it holds.
    /// </summary>
    /// <remarks>
    /// An endpoint with one case is a leaf. The common endpoint has exactly one, and growing a level
    /// to say "there is nothing more here" costs a row and a fold on every endpoint in the workspace.
    /// Two or more get the expander and their names.
    ///
    /// <para>A VIEW concern only: <see cref="Children"/> stays complete, because <c>ToTreeNode</c>
    /// feeds <c>RunPlan</c> - and an endpoint whose single case were hidden from the MODEL would be
    /// sent with no case at all, which is a different request.</para>
    /// </remarks>
    public IEnumerable<WorkspaceNodeViewModel> DisplayChildren
    {
        get
        {
            if (Kind != WorkspaceNodeKind.Endpoint)
            {
                return Children;
            }

            // Cases first, then batches: a case is what the endpoint IS called with, a batch is a way
            // of running several of them, so the parts come before the arrangements. An endpoint with
            // exactly one case and no batches stays a leaf - "1 case" under an expander is a row that
            // costs a click to learn nothing.
            return Children.Count + Batches.Count < 2 ? [] : [.. Children, .. Batches];
        }
    }

    /// <summary>
    /// This endpoint's own batches.
    /// </summary>
    /// <remarks>
    /// Its own collection, never merged into <see cref="Children"/>, because <c>ToTreeNode</c> feeds
    /// <c>RunPlan</c>: an endpoint's children are the cases a run of it SENDS, and a batch in there
    /// would make an endpoint whose only child was a batch send nothing at all.
    /// </remarks>
    public ObservableCollection<WorkspaceNodeViewModel> Batches { get; } = [];

    /// <summary>How many ways this endpoint is called, shown as a badge only when there is more than
    /// one - "1 case" on every row would be noise standing in for the ordinary.</summary>
    public int CaseCount => Kind == WorkspaceNodeKind.Endpoint ? Children.Count : 0;

    public int BatchCount => Kind == WorkspaceNodeKind.Endpoint ? Batches.Count : 0;

    public bool IsBatch => Kind == WorkspaceNodeKind.Batch;

    /// <summary>
    /// Made, but not written yet: this node has no file on disk.
    /// </summary>
    /// <remarks>
    /// <para>A draft is held in memory until its editor is saved, so that "New case" does not leave a
    /// <c>new-case.json</c> behind every time someone opens one and changes their mind.</para>
    /// <para>The tree is otherwise a reflection of the file system, so a draft has to be exempted from
    /// <see cref="SyncChildren"/>'s reconciliation twice over: it is never removed for being absent
    /// from a scan, and the moment the scan DOES report it the flag clears and it becomes an ordinary
    /// node. Everything else about it is real - its path is the file it will occupy, which is what
    /// reserves the name.</para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnsaved))]
    public partial bool IsDraft { get; set; }

    /// <summary>What the tree's dot means: edited and not saved, or never saved at all.</summary>
    public bool IsUnsaved => IsDirty || IsDraft;

    /// <summary>
    /// What this endpoint holds, in ONE chip - never two.
    /// </summary>
    /// <remarks>
    /// <para>The pane is 260px and every row already carries a method badge and an auth badge. A
    /// second count chip pushed the auth badge off the right edge; stopping that overflow then made
    /// the NAME ellipse to "ge..." instead, which is the worse trade - the name is what the row is
    /// for. So the counts take turns rather than sharing.</para>
    /// <para>Cases win the slot when there are several, because they are what an endpoint IS; the
    /// batch count gets it only when there is no case count to show. Either way, expanding shows
    /// both, tagged.</para>
    /// </remarks>
    public string ContentsText => (CaseCount, BatchCount) switch
    {
        // One case is not worth a chip - it is the ordinary thing an endpoint has. One batch is,
        // because it is something you can run rather than a way this endpoint is called.
        ( > 1, _) => $"{CaseCount} cases",
        (_, > 0) => BatchCount == 1 ? "1 batch" : $"{BatchCount} batches",
        _ => "",
    };

    public bool HasContents => ContentsText.Length > 0;

    /// <summary>Whether there is a recorded answer here, and whether it can still be believed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSnapshot))]
    [NotifyPropertyChangedFor(nameof(HasSnapshot))]
    [NotifyPropertyChangedFor(nameof(HasStaleSnapshot))]
    [NotifyPropertyChangedFor(nameof(SnapshotTooltip))]
    public partial SnapshotState Snapshots { get; set; }

    public bool HasSnapshot => Snapshots == SnapshotState.Recorded;

    public bool HasNoSnapshot => Snapshots == SnapshotState.None;

    /// <summary>Recorded before the endpoint or case was last edited. The badge that matters: a green
    /// run against one of these is a lie.</summary>
    public bool HasStaleSnapshot => Snapshots == SnapshotState.Stale;

    public string SnapshotTooltip => Snapshots switch
    {
        SnapshotState.Recorded => "A snapshot is recorded, and was recorded after the last edit",
        SnapshotState.None => "No snapshot recorded. A regression run here reports that and fails.",
        SnapshotState.Stale => "Recorded BEFORE this was last edited - re-record it, or a green run means nothing",
        _ => "",
    };

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
    /// <remarks>
    /// Drafts are left out. This is what <c>RunPlan</c> walks, and the runner reads from disk - a case
    /// that has not been saved yet has no file to send, so including it would turn "run this endpoint"
    /// into a run with a step that cannot possibly work.
    /// </remarks>
    public WorkspaceTreeNode ToTreeNode() =>
        new(Name, FullPath, IsDirectory, [.. Children.Where(c => !c.IsDraft).Select(c => c.ToTreeNode())],
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
    [NotifyPropertyChangedFor(nameof(IsUnsaved))]
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
    [NotifyPropertyChangedFor(nameof(HasContents))]
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
        // A draft has no file, so no scan will ever mention it. Removing it for that would delete the
        // thing the user is in the middle of writing on the next file-system event.
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (!Children[i].IsDraft && !incoming.Any(n => n.FullPath == Children[i].FullPath))
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
                    Snapshots = node.Snapshots,
                };
                child.SyncChildren(node.Children);
                child.SyncBatches(node.Batches);
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
                existing.Snapshots = node.Snapshots;

                // The scan found it, so it is no longer a draft - saving is what makes one real, and
                // this is the only place that can know it happened.
                existing.IsDraft = false;

                existing.SyncChildren(node.Children);
                existing.SyncBatches(node.Batches);
            }
        }

        MoveDraftsLast(Children);

        // What the tree SHOWS depends on how many children there are - an endpoint becomes a leaf at
        // one case and grows an expander at two - so adding or removing one has to re-ask.
        RaiseShapeChanged();
    }

    /// <summary>
    /// Reconciles this endpoint's batches, the same way <see cref="SyncChildren"/> does its cases.
    /// </summary>
    /// <remarks>
    /// A batch node has no children of its own - a batch names steps, it does not contain them - so
    /// this does not recurse. Batches do not nest, and a batch of batches is a scheduler.
    /// </remarks>
    protected void SyncBatches(IReadOnlyList<WorkspaceTreeNode> incoming)
    {
        for (var i = Batches.Count - 1; i >= 0; i--)
        {
            if (!Batches[i].IsDraft && !incoming.Any(n => n.FullPath == Batches[i].FullPath))
            {
                Batches.RemoveAt(i);
            }
        }

        for (var i = 0; i < incoming.Count; i++)
        {
            var node = incoming[i];
            var existing = Batches.FirstOrDefault(b => b.FullPath == node.FullPath);

            if (existing is null)
            {
                Batches.Insert(
                    Math.Min(i, Batches.Count),
                    new WorkspaceNodeViewModel(node.Name, node.FullPath, node.IsDirectory, node.Kind));

                continue;
            }

            var currentIndex = Batches.IndexOf(existing);
            if (currentIndex != i)
            {
                Batches.Move(currentIndex, i);
            }

            existing.Name = node.Name;
            existing.IsDraft = false;
        }

        MoveDraftsLast(Batches);
        RaiseShapeChanged();
    }

    /// <summary>
    /// Drafts sit at the end, in the order they were made.
    /// </summary>
    /// <remarks>
    /// The scan does not mention them, so the reconciliation above moves the real nodes into scan
    /// order around whatever position a draft happened to hold - which would shuffle it up the list
    /// on every unrelated file change. Last is the one position nothing else competes for.
    /// </remarks>
    private static void MoveDraftsLast(ObservableCollection<WorkspaceNodeViewModel> nodes)
    {
        foreach (var draft in nodes.Where(n => n.IsDraft).ToList())
        {
            var from = nodes.IndexOf(draft);
            if (from >= 0 && from != nodes.Count - 1)
            {
                nodes.Move(from, nodes.Count - 1);
            }
        }
    }

    private void RaiseShapeChanged()
    {
        OnPropertyChanged(nameof(DisplayChildren));
        OnPropertyChanged(nameof(CaseCount));
        OnPropertyChanged(nameof(BatchCount));
        OnPropertyChanged(nameof(ContentsText));
        OnPropertyChanged(nameof(HasContents));
    }
}
