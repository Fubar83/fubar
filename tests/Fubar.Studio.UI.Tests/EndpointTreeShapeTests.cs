using Fubar.Studio.Core.Models;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// What the tree SHOWS beneath an endpoint, which is not always what it holds.
///
/// <para>The split matters: <c>Children</c> feeds <c>ToTreeNode</c> and therefore <c>RunPlan</c>, so
/// an endpoint whose single case were hidden from the MODEL would be sent with no case at all - a
/// different request from the one on screen.</para>
/// </summary>
public class EndpointTreeShapeTests
{
    private sealed class Root : WorkspaceNodeViewModel
    {
        public Root() : base("collections", "/w/collections", true) { }

        public void Load(params WorkspaceTreeNode[] children) => SyncChildren(children);
    }

    private static WorkspaceTreeNode Case(string name) =>
        new(name, $"/w/collections/get-order/cases/{name}.json", false, []) { Kind = WorkspaceNodeKind.Case };

    private static WorkspaceTreeNode Endpoint(params WorkspaceTreeNode[] cases) =>
        new("get-order", "/w/collections/get-order", true, cases) { Kind = WorkspaceNodeKind.Endpoint };

    private static WorkspaceNodeViewModel Load(WorkspaceTreeNode node)
    {
        var root = new Root();
        root.Load(node);
        return root.Children.Single();
    }

    /// <summary>The common endpoint has exactly one case, and growing a level to say "there is
    /// nothing more here" costs a row and a fold on every endpoint in the workspace.</summary>
    [Fact]
    public void An_endpoint_with_one_case_is_a_leaf()
    {
        var endpoint = Load(Endpoint(Case("default")));

        Assert.Empty(endpoint.DisplayChildren);
        Assert.Equal("", endpoint.ContentsText);
    }

    /// <summary>...but the case is still THERE, because a run sends what the tree holds.</summary>
    [Fact]
    public void The_hidden_case_is_still_what_a_run_sends()
    {
        var endpoint = Load(Endpoint(Case("default")));

        Assert.Single(endpoint.Children);
        Assert.Equal("default", endpoint.ToTreeNode().Children.Single().Name);
    }

    [Fact]
    public void Two_cases_are_shown_and_counted()
    {
        var endpoint = Load(Endpoint(Case("default"), Case("not-found")));

        Assert.Equal(2, endpoint.DisplayChildren.Count());
        Assert.Equal("2 cases", endpoint.ContentsText);
    }

    /// <summary>Adding a second case turns a leaf into a branch, so the tree has to be told.</summary>
    [Fact]
    public void Adding_a_second_case_makes_the_endpoint_expand()
    {
        var root = new Root();
        root.Load(Endpoint(Case("default")));

        var raised = new List<string>();
        root.Children.Single().PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        root.Load(Endpoint(Case("default"), Case("not-found")));

        // DisplayChildren is a live collection now rather than a recomputed projection - it is
        // MUTATED rather than re-raised, which is what lets the tree keep a selected row.
        Assert.Contains(nameof(WorkspaceNodeViewModel.ContentsText), raised);
        Assert.Equal(2, root.Children.Single().DisplayChildren.Count);
    }

    /// <summary>A folder is never collapsed this way - only an endpoint's cases are.</summary>
    [Fact]
    public void A_folder_with_one_child_still_shows_it()
    {
        var folder = Load(new WorkspaceTreeNode("orders", "/w/collections/orders", true,
            [new WorkspaceTreeNode("Ping.json", "/w/collections/orders/Ping.json", false, [])]));

        Assert.Single(folder.DisplayChildren);
    }

    // ---- An endpoint's own batches --------------------------------------------------------------

    private static WorkspaceTreeNode Batch(string name) =>
        new(name, $"/w/collections/get-order/batches/{name}.json", false, [])
        { Kind = WorkspaceNodeKind.Batch };

    private static WorkspaceTreeNode WithBatches(
        WorkspaceTreeNode[] cases, params WorkspaceTreeNode[] batches) =>
        new("get-order", "/w/collections/get-order", true, cases)
        { Kind = WorkspaceNodeKind.Endpoint, Batches = batches };

    /// <summary>Cases first, then batches: the parts before the arrangements.</summary>
    [Fact]
    public void Cases_come_before_batches_in_the_tree()
    {
        var endpoint = Load(WithBatches([Case("default"), Case("not-found")], Batch("happy")));

        Assert.Equal(
            ["default", "not-found", "happy"],
            endpoint.DisplayChildren.Select(c => c.DisplayName));
    }

    /// <summary>Shown, but never as a case: Children feeds ToTreeNode and therefore RunPlan, so a
    /// batch in there would change what running the endpoint SENDS.</summary>
    [Fact]
    public void A_batch_is_shown_but_is_not_a_child()
    {
        var endpoint = Load(WithBatches([Case("default")], Batch("happy")));

        Assert.Single(endpoint.Children);
        Assert.Single(endpoint.Batches);
        Assert.Single(endpoint.ToTreeNode().Children);
    }

    /// <summary>One case is a leaf; one case AND a batch is two rows worth showing.</summary>
    [Fact]
    public void One_case_plus_a_batch_is_no_longer_a_leaf()
    {
        Assert.Empty(Load(WithBatches([Case("default")])).DisplayChildren);
        Assert.Equal(2, Load(WithBatches([Case("default")], Batch("happy"))).DisplayChildren.Count());
    }

    /// <summary>
    /// The counts take turns rather than sharing the row.
    /// </summary>
    /// <remarks>
    /// Two chips did not fit: the pane is 260px and every row already carries a method badge and an
    /// auth badge, so a second count pushed the auth badge off the edge - and stopping that overflow
    /// then ellipsed the NAME to "ge..." instead, which is the worse trade.
    /// </remarks>
    [Fact]
    public void Cases_win_the_count_chip_when_there_are_several()
    {
        Assert.Equal(
            "2 cases",
            Load(WithBatches([Case("default"), Case("not-found")], Batch("happy"))).ContentsText);
    }

    [Fact]
    public void The_batch_count_gets_the_chip_when_there_is_no_case_count()
    {
        Assert.Equal("1 batch", Load(WithBatches([Case("default")], Batch("happy"))).ContentsText);
        Assert.Equal("2 batches", Load(WithBatches([], Batch("happy"), Batch("regression"))).ContentsText);
    }

    [Fact]
    public void An_endpoint_holding_nothing_worth_counting_shows_no_chip()
    {
        Assert.False(Load(WithBatches([Case("default")])).HasContents);
    }

    /// <summary>Adding a batch changes the row's shape, so the tree has to be told.</summary>
    [Fact]
    public void Adding_a_batch_re_asks_for_the_row()
    {
        var root = new Root();
        root.Load(WithBatches([Case("default")]));

        var raised = new List<string>();
        root.Children.Single().PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        root.Load(WithBatches([Case("default")], Batch("happy")));

        Assert.Contains(nameof(WorkspaceNodeViewModel.ContentsText), raised);
        Assert.Equal(2, root.Children.Single().DisplayChildren.Count);
    }
    /// <summary>
    /// The display list is ONE collection for the life of the node, mutated in place.
    /// </summary>
    /// <remarks>
    /// It used to be a computed projection returning a fresh list on every read, so every
    /// notification handed the TreeView a different collection: the child containers were rebuilt and
    /// a selected case or batch lost its selection - which meant opening one deselected the very row
    /// being edited, and every command keyed off the selection stopped being offered.
    /// </remarks>
    [Fact]
    public void The_display_list_survives_a_rescan_as_the_same_collection()
    {
        var root = new Root();
        root.Load(WithBatches([Case("default"), Case("not-found")], Batch("happy")));

        var endpoint = root.Children.Single();
        var before = endpoint.DisplayChildren;

        root.Load(WithBatches([Case("default"), Case("not-found")], Batch("happy"), Batch("regression")));

        Assert.Same(before, endpoint.DisplayChildren);
        Assert.Equal(
            ["default", "not-found", "happy", "regression"],
            endpoint.DisplayChildren.Select(c => c.DisplayName));
    }

    /// <summary>A row that did not change keeps its identity, which is what the TreeView holds a
    /// selection by.</summary>
    [Fact]
    public void A_row_that_did_not_change_is_the_same_node_after_a_rescan()
    {
        var root = new Root();
        root.Load(WithBatches([Case("default"), Case("not-found")]));

        var kept = root.Children.Single().DisplayChildren[0];

        root.Load(WithBatches([Case("default"), Case("not-found")], Batch("happy")));

        Assert.Same(kept, root.Children.Single().DisplayChildren[0]);
    }

    /// <summary>A draft is added straight to Children by the explorer, so the display list follows
    /// the collections themselves rather than each caller remembering to announce it.</summary>
    [Fact]
    public void Adding_a_draft_directly_shows_it()
    {
        var root = new Root();
        root.Load(WithBatches([Case("default")]));

        var endpoint = root.Children.Single();
        Assert.Empty(endpoint.DisplayChildren);

        endpoint.Children.Add(new WorkspaceNodeViewModel(
            "new-case.json", "/w/collections/get-order/cases/new-case.json", false, WorkspaceNodeKind.Case)
        {
            IsDraft = true,
        });

        Assert.Equal(2, endpoint.DisplayChildren.Count);
    }
}
