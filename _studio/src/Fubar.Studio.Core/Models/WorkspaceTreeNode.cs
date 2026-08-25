namespace Fubar.Studio.Core.Models;

/// <summary>
/// An immutable snapshot of one folder or request file under a workspace's <c>collections/</c>
/// directory, as returned by <c>IWorkspaceService.BuildCollectionsTree</c>. The UI layer wraps
/// these in mutable, bindable view model nodes and reconciles them against the previous tree on
/// every refresh, rather than binding to this type directly.
/// </summary>
public sealed record WorkspaceTreeNode(
    string Name,
    string FullPath,
    bool IsDirectory,
    IReadOnlyList<WorkspaceTreeNode> Children,
    RequestSummary? RequestSummary = null);

/// <summary>
/// Lightweight metadata about a request file, read alongside the tree scan so the Left Pane's
/// method/auth badges (LeftPane.md §5) don't need a second pass over every <c>request.json</c>.
/// Null for directory nodes.
/// </summary>
public sealed record RequestSummary(string Method, bool HasAuthOverride);
