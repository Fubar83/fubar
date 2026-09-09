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
    RequestSummary? RequestSummary = null)
{
    private readonly WorkspaceNodeKind? _kind;

    /// <summary>
    /// What this node IS, which a directory flag alone can no longer say: an endpoint is a directory
    /// and is not a folder, and a case is a file and is not a request.
    /// </summary>
    /// <remarks>
    /// Defaults to the old reading - directory means folder, file means request - so every caller and
    /// test written before endpoints existed still describes the tree it meant. Only the scanner for
    /// the endpoints format sets it explicitly.
    /// </remarks>
    public WorkspaceNodeKind Kind
    {
        get => _kind ?? (IsDirectory ? WorkspaceNodeKind.Folder : WorkspaceNodeKind.Request);
        init => _kind = value;
    }

    /// <summary>Whether this node is something a run can send - as opposed to a container of them.</summary>
    public bool IsRunnable => Kind is WorkspaceNodeKind.Request or WorkspaceNodeKind.Endpoint or WorkspaceNodeKind.Case;
}

/// <summary>What a node in the collections tree is.</summary>
public enum WorkspaceNodeKind
{
    /// <summary>A plain directory. Its children are whatever is beneath it.</summary>
    Folder,

    /// <summary>A <c>&lt;name&gt;.json</c> in the requests format.</summary>
    Request,

    /// <summary>A directory holding <c>endpoint.json</c>. Its children are its cases, and nothing
    /// else: a directory inside an endpoint is not a folder.</summary>
    Endpoint,

    /// <summary>One <c>cases/&lt;name&gt;.json</c>.</summary>
    Case,
}

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
