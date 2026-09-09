namespace Fubar.Studio.Core.Models;

/// <summary>
/// Which shape a workspace's <c>collections/</c> tree is in.
/// </summary>
/// <remarks>
/// <para><b>The field decides, never the files.</b> One value, read once when the workspace opens, so
/// nothing anywhere has to guess which half of the product it is in from what it happens to find on
/// disk. Sniffing would make the answer depend on which directory was looked at first, and a
/// half-converted tree would then behave differently in two windows.</para>
/// <para>Existing workspaces are never converted on open (docs/spec-endpoints.md §10.4). The cost is
/// that the product is in two halves, and the thing that closes the split is
/// <c>WorkspaceFormatConverter</c> - an explicit act, offered, with a backup and a preview.</para>
/// </remarks>
public enum WorkspaceFormat
{
    /// <summary>One <c>&lt;name&gt;.json</c> per request. What every workspace written before endpoints
    /// existed holds, and what an absent <c>format</c> field means.</summary>
    Requests,

    /// <summary>A directory per endpoint, holding <c>endpoint.json</c>, <c>cases/</c> and
    /// <c>snapshots/</c>.</summary>
    Endpoints,
}
