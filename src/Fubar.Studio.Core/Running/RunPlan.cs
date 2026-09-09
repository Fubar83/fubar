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
/// <param name="CaseName">Which case of the endpoint this step is, or null in the requests format and
/// for an endpoint sent as it stands. A case is a way of CALLING the endpoint, so it is a second
/// coordinate rather than a different <paramref name="FilePath"/>.</param>
/// <param name="CaseFilePath">Absolute path to the <c>cases/&lt;name&gt;.json</c>, read when the step's
/// turn comes for the same reason the request is.</param>
public sealed record RunStep(
    int Order,
    string Name,
    string FilePath,
    string FolderPath,
    string? CaseName = null,
    string? CaseFilePath = null)
{
    /// <summary>
    /// What identifies this step to a reader - "Get order" or "Get order#not-found".
    /// </summary>
    /// <remarks>
    /// A JUnit test name, so it must be stable and unique within a run: an index would leave CI
    /// unable to say which test started failing the moment anything was reordered.
    /// </remarks>
    public string QualifiedName => CaseName is { Length: > 0 } name ? $"{Name}#{name}" : Name;

    /// <summary>
    /// The file that identifies WHAT WAS SENT - the case when there is one, the request or endpoint
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// What a snapshot is keyed by. Two cases of one endpoint answer differently by design, so keying
    /// snapshots on the endpoint alone would have each case overwrite the other's recording and every
    /// run report the other case's answer as a regression.
    /// </remarks>
    public string SubjectPath => CaseFilePath is { Length: > 0 } path ? path : FilePath;

    /// <summary>
    /// Why this step cannot be sent at all - a batch naming an endpoint or a case that is not there.
    /// </summary>
    /// <remarks>
    /// Carried as a step rather than dropped when the plan is built. A batch that quietly shrank when
    /// someone renamed an endpoint would keep passing while testing one thing fewer, and the report
    /// would not say so. This way the run reports it as Errored, which is what it is.
    /// </remarks>
    public string? Unresolved { get; init; }
}

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
                // Keyed on the case as well as the endpoint: several cases share one endpoint.json,
                // and keying on the file alone would silently run the first case and drop the rest.
                if (seen.Add($"{step.FilePath}#{step.CaseFilePath}"))
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
            .Where(s => s.QualifiedName.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new RunPlan(Renumbered(kept));
    }

    private static void Walk(WorkspaceTreeNode node, string folderPath, List<RunStep> into)
    {
        switch (node.Kind)
        {
            case WorkspaceNodeKind.Request:
                into.Add(new RunStep(into.Count + 1, RequestName(node.Name), node.FullPath, folderPath));
                return;

            // An endpoint runs every case it has - "run this endpoint" means "check the ways it is
            // called", not "send one of them and hope it was the interesting one". An endpoint with no
            // cases is sent as it stands, which is a legitimate state and not an empty run.
            case WorkspaceNodeKind.Endpoint:
                if (node.Children.Count == 0)
                {
                    into.Add(new RunStep(
                        into.Count + 1, node.Name, EndpointFile(node.FullPath), ParentOf(node.FullPath)));
                    return;
                }

                foreach (var child in node.Children)
                {
                    Walk(child, folderPath, into);
                }

                return;

            case WorkspaceNodeKind.Case:
                var endpointDirectory = EndpointDirectoryOfCase(node.FullPath);
                into.Add(new RunStep(
                    into.Count + 1,
                    Path.GetFileName(endpointDirectory),
                    EndpointFile(endpointDirectory),
                    ParentOf(endpointDirectory),
                    node.Name,
                    node.FullPath));
                return;

            default:
                foreach (var child in node.Children)
                {
                    Walk(child, child.IsDirectory ? child.FullPath : node.FullPath, into);
                }

                return;
        }
    }

    private static string EndpointFile(string endpointDirectory) =>
        Path.Combine(endpointDirectory, "endpoint.json");

    /// <summary>A case lives at <c>&lt;endpoint&gt;/cases/&lt;name&gt;.json</c>, so its endpoint is two
    /// levels up. Derived rather than carried: the tree already says where the file is, and a second
    /// copy of that fact is a second thing to keep in step.</summary>
    private static string EndpointDirectoryOfCase(string caseFilePath) =>
        ParentOf(ParentOf(caseFilePath));

    private static List<RunStep> Renumbered(List<RunStep> steps) =>
        [.. steps.Select((s, i) => s with { Order = i + 1 })];

    private static string ParentOf(string path) => Path.GetDirectoryName(path) ?? path;

    /// <summary>A request file's name without its extension. The tree carries the file name because the
    /// left pane lists files; a run is about requests.</summary>
    private static string RequestName(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName) is { Length: > 0 } name ? name : fileName;
}
