using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Running;

/// <summary>
/// Finds what a selector's path names in a collections tree.
/// </summary>
/// <remarks>
/// <para>By NAME, walking the tree, rather than by joining the path onto the workspace directory: the
/// tree is what the user is looking at and what a run's order comes from, and a selector that resolved
/// against the file system could name something the tree does not show. It also means
/// <c>../../etc/passwd</c> resolves to nothing rather than to a file.</para>
/// <para>Pure, and in Core, so the grammar can be tested without a workspace on disk.</para>
/// </remarks>
public static class TreeLookup
{
    /// <summary>
    /// The node a path names, or null.
    /// </summary>
    /// <remarks>
    /// A request is stored as <c>&lt;name&gt;.json</c> and nobody types the extension, so a segment
    /// matches a node either with it or without it.
    /// </remarks>
    public static WorkspaceTreeNode? Find(IReadOnlyList<WorkspaceTreeNode> roots, string path)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var segments = (path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var current = roots;
        WorkspaceTreeNode? found = null;

        foreach (var segment in segments)
        {
            found = current.FirstOrDefault(n => Matches(n, segment));
            if (found is null)
            {
                return null;
            }

            current = found.Children;
        }

        return found;
    }

    /// <summary>
    /// The steps a selector asks for, in the order they will run.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The path names nothing, or the case does. Reported rather than run as an empty plan: "nothing
    /// matched, so nothing ran, so everything passed" is how a suite silently stops testing, and a
    /// typo in a CI script is exactly how it happens.
    /// </exception>
    public static RunPlan Expand(IReadOnlyList<WorkspaceTreeNode> roots, RunSelector selector)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(selector);

        if (selector.Kind == RunSelectorKind.Everything)
        {
            return RunPlan.From(roots);
        }

        if (selector.Kind != RunSelectorKind.Path)
        {
            throw new InvalidOperationException(
                $"\"{selector}\" is a batch and has to be expanded through the batch store.");
        }

        var node = Find(roots, selector.Path!)
            ?? throw new InvalidOperationException(
                $"\"{selector.Path}\" is not a folder, endpoint or request in this workspace.");

        if (selector.Case is not { Length: > 0 } wanted)
        {
            return RunPlan.From(node);
        }

        if (node.Kind != WorkspaceNodeKind.Endpoint)
        {
            throw new InvalidOperationException(
                $"\"{selector.Path}\" is not an endpoint, so it has no case \"{wanted}\".");
        }

        var caseNode = node.Children.FirstOrDefault(
            c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                node.Children.Count == 0
                    ? $"\"{selector.Path}\" has no cases."
                    : $"\"{selector.Path}\" has no case \"{wanted}\". It has: "
                      + string.Join(", ", node.Children.Select(c => c.Name)) + ".");

        return RunPlan.From(caseNode);
    }

    private static bool Matches(WorkspaceTreeNode node, string segment) =>
        string.Equals(node.Name, segment, StringComparison.OrdinalIgnoreCase)
        || (!node.IsDirectory
            && string.Equals(
                System.IO.Path.GetFileNameWithoutExtension(node.Name),
                segment,
                StringComparison.OrdinalIgnoreCase));
}
