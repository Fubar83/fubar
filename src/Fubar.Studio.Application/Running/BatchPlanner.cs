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
    /// <param name="ownerPath">The endpoint whose batch this is, relative to <c>collections/</c>.</param>
    /// <exception cref="InvalidOperationException">There is no batch by that name. Reported rather
    /// than run as nothing: a typo in a CI script must not pass.</exception>
    Task<ResolvedBatch> ExpandAsync(
        Workspace workspace,
        string batchName,
        string? ownerPath = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <inheritdoc cref="IBatchPlanner"/>
/// </summary>
/// <remarks>
/// <para>A batch is a DIRECTORY of items, and expanding it is listing them. There is nothing to
/// resolve any more: a step used to be a PATH into the tree, which is how a batch came to name an
/// endpoint that had since been renamed and had to be reported as an error rather than skipped. An
/// item cannot point at anything that is not in the batch, because it IS in the batch.</para>
/// <para>The order is the file names', naturally sorted - see <see cref="NaturalOrder"/>. Renaming a
/// file is how a batch is reordered, so there is no index anywhere to fall out of step with what the
/// directory holds.</para>
/// </remarks>
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

        var directory = _batches.FindBatchDirectory(owner, batchName)
            ?? throw new InvalidOperationException(Missing(owner, batchName, ownerPath));

        var batch = await _batches.LoadBatchAsync(directory, cancellationToken).ConfigureAwait(false);

        // The endpoint this batch belongs to: <endpoint>/batches/<name>, so two levels up.
        var endpointDirectory = Path.GetDirectoryName(Path.GetDirectoryName(directory)) ?? owner;
        var endpointFile = Path.Combine(endpointDirectory, IEndpointStore.EndpointFileName);
        var endpointName = Path.GetFileName(endpointDirectory);
        var folder = Path.GetDirectoryName(endpointDirectory) ?? collections;

        var steps = _batches.ListItems(directory)
            .Select((item, index) => new RunStep(
                index + 1, endpointName, endpointFile, folder, item.Name, item.FilePath))
            .ToList();

        return new ResolvedBatch(batch, new RunPlan(steps));
    }

    /// <summary>
    /// The directory that HOLDS the <c>batches/</c> a name is looked up in - one endpoint's directory,
    /// or the workspace root for a batch of the workspace's own.
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

        return node.FullPath;
    }

    /// <summary>
    /// Says what there IS, because the commonest reason to reach here is a typo and the answer is
    /// usually on the list.
    /// </summary>
    private string Missing(string owner, string name, string? ownerPath)
    {
        var known = _batches.ListBatches(owner);
        var where = ownerPath is { Length: > 0 } ? $"\"{ownerPath}\"" : "this workspace";

        return known.Count == 0
            ? $"{where} has no batches, so there is no \"{name}\"."
            : $"{where} has no batch called \"{name}\". It has: "
              + string.Join(", ", known.Select(b => b.Name)) + ".";
    }
}
