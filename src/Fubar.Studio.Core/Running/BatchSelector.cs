using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Running;

/// <summary>
/// What a batch step calls a node in the tree.
/// </summary>
/// <remarks>
/// <para>The inverse of <see cref="TreeLookup.Find"/>, and in Core beside it for the same reason: it
/// is the same grammar read the other way round, and a selector this produces must be one that
/// resolves. Pure, so it can be tested without a workspace on disk.</para>
/// <para>Deliberately format-agnostic. Only a CASE needs endpoints; a step naming a plain request is
/// resolved by <c>TreeLookup.Matches</c>, which goes out of its way to match
/// <c>&lt;name&gt;.json</c> with or without the extension.</para>
/// </remarks>
public static class BatchSelector
{
    /// <summary>
    /// The selector for a node, or null when it is not something a step can name.
    /// </summary>
    /// <param name="fullPath">The node's path on disk.</param>
    /// <param name="kind">What the node is.</param>
    /// <param name="collectionsPath">The workspace's <c>collections/</c> directory, which every
    /// selector is relative to.</param>
    /// <remarks>
    /// Two nodes resolve to something other than themselves. A CASE resolves to its ENDPOINT: a step
    /// addresses a case as an endpoint plus a case name, and <c>cases/created.json</c> is not a path
    /// the planner can find. A BATCH resolves to nothing - a batch listing a batch is a nesting
    /// neither the format nor the planner has.
    /// </remarks>
    public static string? For(string? fullPath, WorkspaceNodeKind kind, string collectionsPath)
    {
        ArgumentNullException.ThrowIfNull(collectionsPath);

        if (fullPath is not { Length: > 0 } || kind == WorkspaceNodeKind.Batch)
        {
            return null;
        }

        // <endpoint>/cases/<name>.json, so the endpoint is two levels up.
        var target = kind == WorkspaceNodeKind.Case
            ? Path.GetDirectoryName(Path.GetDirectoryName(fullPath))
            : fullPath;

        if (target is not { Length: > 0 })
        {
            return null;
        }

        var relative = Path.GetRelativePath(collectionsPath, target).Replace('\\', '/');

        // Outside the tree, or the collections directory itself - neither is something a step names.
        // "Everything" is what a batch with no steps at all would mean, and that is a different thing
        // to write down.
        return relative.StartsWith("..", StringComparison.Ordinal) || relative is "." or ""
            ? null
            : relative;
    }
}
