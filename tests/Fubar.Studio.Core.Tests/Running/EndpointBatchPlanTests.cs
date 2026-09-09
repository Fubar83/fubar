using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Core.Tests.Running;

/// <summary>
/// What an endpoint's own batches must NOT do to a run of that endpoint.
///
/// <para>An endpoint's <c>Children</c> are the cases a run of it SENDS - <c>RunPlan.Walk</c> expands
/// them, and treats an endpoint with none as one to send as it stands. Batches therefore hang off
/// <see cref="WorkspaceTreeNode.Batches"/> instead. Put one in Children and an endpoint whose only
/// child was a batch stops being sent at all and contributes nothing: an empty run reported as a
/// pass, which is the exact failure this area exists to refuse.</para>
/// </summary>
public class EndpointBatchPlanTests
{
    private const string Root = "/w/collections";

    private static WorkspaceTreeNode Case(string name) =>
        new(name, $"{Root}/orders/get-order/cases/{name}.json", false, [])
        {
            Kind = WorkspaceNodeKind.Case,
        };

    private static WorkspaceTreeNode Batch(string name) =>
        new(name, $"{Root}/orders/get-order/batches/{name}.json", false, [])
        {
            Kind = WorkspaceNodeKind.Batch,
        };

    private static WorkspaceTreeNode Endpoint(
        IReadOnlyList<WorkspaceTreeNode> cases, IReadOnlyList<WorkspaceTreeNode> batches) =>
        new("get-order", $"{Root}/orders/get-order", true, cases)
        {
            Kind = WorkspaceNodeKind.Endpoint,
            Batches = batches,
        };

    /// <summary>The one that would have been silent.</summary>
    [Fact]
    public void An_endpoint_with_batches_and_no_cases_is_still_sent_as_it_stands()
    {
        var plan = RunPlan.From(Endpoint([], [Batch("happy")]));

        var step = Assert.Single(plan.Steps);
        Assert.Equal("get-order", step.Name);
        Assert.Null(step.CaseName);
    }

    [Fact]
    public void Batches_do_not_add_steps_to_a_run_of_the_endpoint()
    {
        var withBatches = RunPlan.From(Endpoint([Case("default"), Case("not-found")], [Batch("happy")]));
        var without = RunPlan.From(Endpoint([Case("default"), Case("not-found")], []));

        Assert.Equal(
            without.Steps.Select(s => s.CaseName),
            withBatches.Steps.Select(s => s.CaseName));
    }

    /// <summary>A folder run walks everything under it, and must not pick batches up on the way.</summary>
    [Fact]
    public void A_folder_run_sends_the_cases_and_not_the_batches()
    {
        var folder = new WorkspaceTreeNode(
            "orders",
            $"{Root}/orders",
            true,
            [Endpoint([Case("default")], [Batch("happy"), Batch("regression")])]);

        var plan = RunPlan.From(folder);

        Assert.Equal(["default"], plan.Steps.Select(s => s.CaseName));
    }
}
