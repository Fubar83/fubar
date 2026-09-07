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
/// <param name="Method">The HTTP method, for the tree's method badge.</param>
/// <param name="HasAuthOverride">
/// Whether this request states its own auth rather than inheriting. Both an explicit scheme AND an
/// explicit "none" count: each is a decision someone made about this request, and each is worth
/// seeing in the tree.
/// </param>
/// <param name="Url">The URL, for the tree filter to match on.</param>
/// <param name="SendsNoAuth">
/// Whether that override is <c>AuthType.None</c> - a request deliberately sending nothing.
///
/// <para>Its own flag because the tree used to badge it "Auth", which says the opposite of the truth:
/// the one request in a collection that must go out unauthenticated looked exactly like the ones that
/// carry a token.</para>
/// </param>
public sealed record RequestSummary(
    string Method,
    bool HasAuthOverride,
    string? Url = null,
    bool SendsNoAuth = false);
