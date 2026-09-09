using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Application.Running;

/// <summary>A batch and the steps it expands to, in its own order.</summary>
public sealed record ResolvedBatch(Batch Batch, RunPlan Plan);

/// <summary>Turns a batch into the plan a run executes.</summary>
public interface IBatchPlanner
{
    /// <summary>Finds a batch by name and expands it.</summary>
    /// <param name="ownerPath">The endpoint whose batch this is, relative to <c>collections/</c> -
    /// null for one of the workspace's own. Null is not "look everywhere": a name is unique only
    /// within one home, and searching both would make <c>@happy</c> mean whichever endpoint happened
    /// to be scanned first.</param>
    /// <exception cref="InvalidOperationException">There is no batch by that name. Reported rather
    /// than run as nothing: a typo in a CI script must not pass.</exception>
    Task<ResolvedBatch> ExpandAsync(
        Workspace workspace,
        string batchName,
        string? ownerPath = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IBatchPlanner"/>
public sealed class BatchPlanner : IBatchPlanner
{
    private readonly IBatchStore _batches;
    private readonly IRequestStore _requests;

    public BatchPlanner(IBatchStore batches, IRequestStore requests)
    {
        _batches = batches;
        _requests = requests;
    }

    public async Task<ResolvedBatch> ExpandAsync(
        Workspace workspace,
        string batchName,
        string? ownerPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var tree = _requests.BuildCollectionsTree(workspace.RootPath);
        var collections = Path.Combine(workspace.RootPath, "collections");

        var owner = OwnerDirectory(tree, workspace, ownerPath);

        var batch = await _batches.FindBatchAsync(owner, batchName, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(Missing(owner, batchName, ownerPath));
        var steps = new List<RunStep>();

        // Teardown after the tested steps, in one list, flagged - the runner holds them back and runs
        // them whatever happened above (see RunStep.IsTeardown).
        var wanted = batch.Steps
            .Select(s => (Step: s, IsTeardown: false))
            .Concat(batch.Teardown.Select(s => (Step: s, IsTeardown: true)));

        foreach (var (step, isTeardown) in wanted)
        {
            var node = TreeLookup.Find(tree, (step.Endpoint ?? "").Replace('\\', '/').Trim('/'));

            if (node is null)
            {
                // Reported as a step that ERRORS, never skipped. A batch that quietly shrank when
                // someone renamed an endpoint would keep passing while testing one thing fewer, which
                // is the failure this whole feature exists to refuse.
                steps.Add(new RunStep(
                    steps.Count + 1,
                    step.Endpoint ?? "(unnamed)",
                    collections,
                    collections,
                    step.Case)
                {
                    Unresolved = $"This batch names \"{step.Endpoint}\", which is not in this workspace.",
                    IsTeardown = isTeardown,
                });

                continue;
            }

            var expanded = step.Case is { Length: > 0 } named
                ? Expand(node, named, collections)
                : RunPlan.From(node).Steps;

            foreach (var expandedStep in expanded)
            {
                steps.Add(expandedStep with { Order = steps.Count + 1, IsTeardown = isTeardown });
            }
        }

        return new ResolvedBatch(batch, new RunPlan(steps));
    }

    /// <summary>One named case of an endpoint - or a step that errors saying which cases there are,
    /// for the same reason a missing endpoint does.</summary>
    private static IReadOnlyList<RunStep> Expand(WorkspaceTreeNode node, string wanted, string collections)
    {
        var caseNode = node.Kind == WorkspaceNodeKind.Endpoint
            ? node.Children.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase))
            : null;

        if (caseNode is not null)
        {
            return RunPlan.From(caseNode).Steps;
        }

        var known = node.Children.Count == 0
            ? "it has none"
            : "it has: " + string.Join(", ", node.Children.Select(c => c.Name));

        return
        [
            new RunStep(1, node.Name, node.FullPath, collections, wanted)
            {
                Unresolved = $"This batch names case \"{wanted}\" of \"{node.Name}\", and {known}.",
            },
        ];
    }

    /// <summary>
    /// The directory that HOLDS the <c>batches/</c> a name is looked up in - the workspace root, or
    /// one endpoint's directory.
    /// </summary>
    private static string OwnerDirectory(
        IReadOnlyList<WorkspaceTreeNode> tree, Workspace workspace, string? ownerPath)
    {
        if (ownerPath is not { Length: > 0 })
        {
            return workspace.RootPath;
        }

        var node = TreeLookup.Find(tree, ownerPath.Replace('\\', '/').Trim('/'))
            ?? throw new InvalidOperationException(
                $"\"{ownerPath}\" is not in this workspace, so it has no batches.");

        return node.Kind == WorkspaceNodeKind.Endpoint
            ? node.FullPath
            : throw new InvalidOperationException(
                $"\"{ownerPath}\" is not an endpoint. Only an endpoint and the workspace hold batches.");
    }

    private string Missing(string owner, string batchName, string? ownerPath)
    {
        var available = _batches.ListBatches(owner);
        var where = ownerPath is { Length: > 0 } ? $"\"{ownerPath}\"" : "This workspace";

        return available.Count == 0
            ? $"{where} has no batches. Create one under {IBatchStore.BatchesDirName}/."
            : $"There is no batch called \"{batchName}\" in {where.ToLowerInvariant()}. There is: "
              + string.Join(", ", available.Select(b => b.Name)) + ".";
    }
}
