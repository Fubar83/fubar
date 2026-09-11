using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Core.Tests.Running;

/// <summary>
/// Endpoints nested inside endpoints.
///
/// <para>REST paths nest, so the directories named after them do: <c>orders/</c> is a call AND the
/// place <c>orders/by-id/</c> lives. The disk then matches the tree, which is the point.</para>
/// </summary>
public class NestedEndpointPlanTests
{
    private const string Root = "/w/collections";

    private static WorkspaceTreeNode Endpoint(
        string name, string path, WorkspaceTreeNode[]? cases = null, WorkspaceTreeNode[]? nested = null) =>
        new(name, path, true, cases ?? [])
        {
            Kind = WorkspaceNodeKind.Endpoint,
            Nested = nested ?? [],
        };

    private static WorkspaceTreeNode Case(string name, string endpointPath) =>
        new(name, $"{endpointPath}/cases/{name}.json", false, []) { Kind = WorkspaceNodeKind.Case };

    private static WorkspaceTreeNode Folder(string name, params WorkspaceTreeNode[] children) =>
        new(name, $"{Root}/{name}", true, children);

    /// <summary>orders (an endpoint) holding by-id (another one).</summary>
    private static WorkspaceTreeNode Orders() =>
        Endpoint(
            "orders",
            $"{Root}/orders",
            nested: [Endpoint("by-id", $"{Root}/orders/by-id")]);

    // ---- Running the endpoint itself -------------------------------------------------------------

    /// <summary>
    /// Clicking Run on a specific call and getting everything beneath it is the surprising reading.
    /// The folder above is still there for anyone who wants them all.
    /// </summary>
    [Fact]
    public void Running_an_endpoint_sends_that_endpoint_and_not_what_is_nested_in_it()
    {
        var plan = RunPlan.From(Orders());

        Assert.Equal(["orders"], plan.Steps.Select(s => s.Name));
    }

    /// <summary>An endpoint whose only child is another ENDPOINT is still sent itself. It would not be
    /// if nested endpoints lived in Children, because a run only sends an endpoint "as it stands" when
    /// it has none.</summary>
    [Fact]
    public void An_endpoint_holding_only_another_endpoint_is_still_sent()
    {
        var plan = RunPlan.From(Orders());

        Assert.Single(plan.Steps);
        Assert.EndsWith("endpoint.json", plan.Steps[0].FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Its_own_cases_are_still_what_it_sends()
    {
        var orders = Endpoint(
            "orders",
            $"{Root}/orders",
            cases: [Case("all", $"{Root}/orders"), Case("page-two", $"{Root}/orders")],
            nested: [Endpoint("by-id", $"{Root}/orders/by-id")]);

        var plan = RunPlan.From(orders);

        Assert.Equal(["all", "page-two"], plan.Steps.Select(s => s.CaseName));
    }

    // ---- Running the folder above ----------------------------------------------------------------

    /// <summary>Running a folder has always meant everything underneath it, and a nested endpoint is
    /// underneath it.</summary>
    [Fact]
    public void Running_the_folder_above_reaches_the_nested_endpoint()
    {
        var plan = RunPlan.From(Folder("api", Orders()));

        Assert.Equal(["orders", "by-id"], plan.Steps.Select(s => s.Name));
    }

    [Fact]
    public void Nesting_goes_as_deep_as_the_directories_do()
    {
        var deep = Endpoint(
            "orders",
            $"{Root}/orders",
            nested:
            [
                Endpoint(
                    "by-id",
                    $"{Root}/orders/by-id",
                    nested: [Endpoint("items", $"{Root}/orders/by-id/items")]),
            ]);

        var plan = RunPlan.From(Folder("api", deep));

        Assert.Equal(["orders", "by-id", "items"], plan.Steps.Select(s => s.Name));
    }

    /// <summary>The order is the tree's, depth first - captures chain, so a run's order is not
    /// cosmetic.</summary>
    [Fact]
    public void The_order_is_the_trees_own()
    {
        var plan = RunPlan.From(Folder(
            "api",
            Endpoint("a", $"{Root}/a", nested: [Endpoint("a-child", $"{Root}/a/a-child")]),
            Endpoint("b", $"{Root}/b")));

        Assert.Equal(["a", "a-child", "b"], plan.Steps.Select(s => s.Name));
        Assert.Equal([1, 2, 3], plan.Steps.Select(s => s.Order));
    }
}
