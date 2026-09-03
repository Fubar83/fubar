using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Running;

/// <summary>One request in a run, in the order it will be sent.</summary>
/// <param name="Order">1-based position, so a report can name "request 4 of 12" without the reader
/// counting rows.</param>
/// <param name="Name">The request's name. A request is stored as <c>Ping.json</c>, so the extension is
/// dropped: it is not part of what anyone calls the request, and it would otherwise show on every row of
/// a run and in every JUnit test name.</param>
/// <param name="FilePath">Absolute path to the <c>request.json</c>. The plan carries paths rather than
/// loaded <see cref="RequestModel"/>s: a run of any size would otherwise hold every request in memory
/// before sending the first one, and the runner has to re-read from disk anyway (see below).</param>
/// <param name="FolderPath">The containing folder, for grouping in the report.</param>
public sealed record RunStep(int Order, string Name, string FilePath, string FolderPath);

/// <summary>
/// The ordered list of requests a run will send, flattened from a workspace subtree.
///
/// <para><b>The order is the left pane's order, exactly.</b> Depth-first, each folder's own entries in
/// the order the tree scan produced them. A runner that sorted differently - alphabetically, or requests
/// before subfolders - would send them in an order the user cannot see anywhere, and ordering is not
/// cosmetic here: captures chain, so request 3 routinely depends on request 1 having run. The tree is
/// the only place that dependency is expressed, so the tree is what is obeyed.</para>
///
/// <para><b>Requests are addressed by PATH, and read from disk when their turn comes.</b> That means a
/// run sends what is saved, not what is open in an editor - which is the honest behaviour for something
/// whose whole purpose is to be repeatable, and the same thing that will happen when it runs in CI. The
/// UI is responsible for saying so.</para>
/// </summary>
public sealed record RunPlan(IReadOnlyList<RunStep> Steps)
{
    public static readonly RunPlan Empty = new([]);

    public int Count => Steps.Count;

    public bool IsEmpty => Steps.Count == 0;

    /// <summary>Flattens a subtree into the order it will run. A directory node contributes its
    /// descendants; a request node contributes itself, so "run this one request" needs no separate
    /// path through the runner.</summary>
    public static RunPlan From(WorkspaceTreeNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var steps = new List<RunStep>();
        Walk(root, root.IsDirectory ? root.FullPath : ParentOf(root.FullPath), steps);
        return new RunPlan(steps);
    }

    /// <summary>Flattens several selected nodes in the order they were given. Selecting a folder AND a
    /// request inside it would otherwise send that request twice, which is not what a multi-select
    /// means - so a request already contributed by an earlier node is not added again.</summary>
    public static RunPlan From(IEnumerable<WorkspaceTreeNode> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var steps = new List<RunStep>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var branch = new List<RunStep>();
            Walk(root, root.IsDirectory ? root.FullPath : ParentOf(root.FullPath), branch);
            foreach (var step in branch)
            {
                if (seen.Add(step.FilePath))
                {
                    steps.Add(step);
                }
            }
        }

        return new RunPlan(Renumbered(steps));
    }

    /// <summary>Keeps only the steps whose name contains <paramref name="filter"/>, renumbering what
    /// survives so the report counts what was attempted rather than what was in the folder. A blank
    /// filter is not a filter.</summary>
    public RunPlan Filtered(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return this;
        }

        var kept = Steps
            .Where(s => s.Name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new RunPlan(Renumbered(kept));
    }

    private static void Walk(WorkspaceTreeNode node, string folderPath, List<RunStep> into)
    {
        if (!node.IsDirectory)
        {
            into.Add(new RunStep(into.Count + 1, RequestName(node.Name), node.FullPath, folderPath));
            return;
        }

        foreach (var child in node.Children)
        {
            Walk(child, child.IsDirectory ? child.FullPath : node.FullPath, into);
        }
    }

    private static List<RunStep> Renumbered(List<RunStep> steps) =>
        [.. steps.Select((s, i) => s with { Order = i + 1 })];

    private static string ParentOf(string path) => Path.GetDirectoryName(path) ?? path;

    /// <summary>A request file's name without its extension. The tree carries the file name because the
    /// left pane lists files; a run is about requests.</summary>
    private static string RequestName(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName) is { Length: > 0 } name ? name : fileName;
}
