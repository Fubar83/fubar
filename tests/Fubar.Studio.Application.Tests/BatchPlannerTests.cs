using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Application.Tests;

/// <summary>
/// Turning a batch into a plan.
///
/// <para>The rule that shapes all of it: a batch that names something no longer there must ERROR, not
/// shrink. A batch that quietly ran two steps instead of three would keep passing while testing one
/// thing fewer, and nothing in the report would say so.</para>
/// </summary>
public class BatchPlannerTests
{
    private static readonly Workspace Ws = new()
    {
        RootPath = "/w",
        Manifest = new AppManifest { Name = "t", Format = WorkspaceFormat.Endpoints },
    };

    private static WorkspaceTreeNode Case(string name, string endpoint) =>
        new(name, $"{endpoint}/cases/{name}.json", false, []) { Kind = WorkspaceNodeKind.Case };

    private static WorkspaceTreeNode Endpoint(string name, string path, params WorkspaceTreeNode[] cases) =>
        new(name, path, true, cases) { Kind = WorkspaceNodeKind.Endpoint };

    private static BatchPlanner Sut(Batch batch) =>
        new(new FakeBatchStore(batch), new TreeOnlyRequests());

    private static Batch Batch(params BatchStep[] steps) =>
        new() { Name = "smoke", Steps = [.. steps] };

    // ---- Order and expansion ---------------------------------------------------------------------

    /// <summary>The batch's order, not the tree's: a batch that starts with a login and then calls
    /// three endpoints is stating a dependency, and sorting it would break exactly the batches worth
    /// having.</summary>
    [Fact]
    public async Task The_order_is_the_batchs_own()
    {
        var plan = (await Sut(Batch(
            new BatchStep("orders/get-order", "not-found"),
            new BatchStep("auth/login"))).ExpandAsync(Ws, "smoke")).Plan;

        Assert.Equal(["get-order#not-found", "login#default"], plan.Steps.Select(s => s.QualifiedName));
        Assert.Equal([1, 2], plan.Steps.Select(s => s.Order));
    }

    [Fact]
    public async Task A_step_with_no_case_runs_every_case_the_endpoint_has()
    {
        var plan = (await Sut(Batch(new BatchStep("orders/get-order"))).ExpandAsync(Ws, "smoke")).Plan;

        Assert.Equal(["default", "not-found"], plan.Steps.Select(s => s.CaseName));
    }

    [Fact]
    public async Task A_step_naming_a_case_runs_that_one()
    {
        var plan = (await Sut(Batch(new BatchStep("orders/get-order", "not-found"))).ExpandAsync(Ws, "smoke")).Plan;

        Assert.Equal("not-found", Assert.Single(plan.Steps).CaseName);
    }

    // ---- Missing pieces --------------------------------------------------------------------------

    [Fact]
    public async Task An_endpoint_that_is_not_there_becomes_a_step_that_errors()
    {
        var plan = (await Sut(Batch(
            new BatchStep("orders/get-order"),
            new BatchStep("orders/renamed-away"))).ExpandAsync(Ws, "smoke")).Plan;

        // Three steps, not two: the two cases of get-order, plus one that cannot run.
        Assert.Equal(3, plan.Count);

        var broken = plan.Steps[^1];
        Assert.NotNull(broken.Unresolved);
        Assert.Contains("renamed-away", broken.Unresolved!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_case_that_is_not_there_becomes_a_step_that_errors_and_says_which_exist()
    {
        var plan = (await Sut(Batch(new BatchStep("orders/get-order", "nope"))).ExpandAsync(Ws, "smoke")).Plan;

        var broken = Assert.Single(plan.Steps);
        Assert.Contains("not-found", broken.Unresolved!, StringComparison.Ordinal);
    }

    /// <summary>A typo in a CI script must not pass by running nothing.</summary>
    [Fact]
    public async Task A_batch_that_does_not_exist_is_refused_and_lists_the_ones_that_do()
    {
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sut(Batch()).ExpandAsync(Ws, "nightly"));

        Assert.Contains("smoke", thrown.Message, StringComparison.Ordinal);
    }

    // ---- Fakes ------------------------------------------------------------------------------------

    private sealed class FakeBatchStore(Batch batch) : IBatchStore
    {
        public IReadOnlyList<BatchSummary> ListBatches(string workspaceRoot) =>
            [new BatchSummary(batch.Name, $"/w/batches/{batch.Name}.json")];

        public Task<Batch> LoadBatchAsync(string batchFilePath, CancellationToken ct = default) =>
            Task.FromResult(batch);

        public Task<Batch?> FindBatchAsync(string workspaceRoot, string name, CancellationToken ct = default) =>
            Task.FromResult(string.Equals(name, batch.Name, StringComparison.OrdinalIgnoreCase) ? batch : null);

        public Task SaveBatchAsync(string batchFilePath, Batch value, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public string CreateBatch(string workspaceRoot, string name) => throw new NotSupportedException();
    }

    /// <summary>
    ///   collections/
    ///     auth/login/        (default)
    ///     orders/get-order/  (default, not-found)
    /// </summary>
    private sealed class TreeOnlyRequests : IRequestStore
    {
        public event Action<string, IReadOnlyList<string>>? RequestMigrated { add { } remove { } }

        public IReadOnlyList<WorkspaceTreeNode> BuildCollectionsTree(string rootPath) =>
        [
            new WorkspaceTreeNode("auth", "/w/collections/auth", true,
            [
                Endpoint("login", "/w/collections/auth/login", Case("default", "/w/collections/auth/login")),
            ]),
            new WorkspaceTreeNode("orders", "/w/collections/orders", true,
            [
                Endpoint("get-order", "/w/collections/orders/get-order",
                    Case("default", "/w/collections/orders/get-order"),
                    Case("not-found", "/w/collections/orders/get-order")),
            ]),
        ];

        public Task<RequestModel> LoadRequestAsync(string path, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveRequestAsync(string path, RequestModel request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public string CreateRequest(string parentDirectory, string requestName) => throw new NotSupportedException();

        public string CreateFolder(string parentDirectory, string folderName) => throw new NotSupportedException();

        public string DuplicatePath(string path) => throw new NotSupportedException();

        public string RenamePath(string path, string newName) => throw new NotSupportedException();

        public void DeletePath(string path) => throw new NotSupportedException();
    }
}
