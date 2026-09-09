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
        Assert.False(endpoint.HasSeveralCases);
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
        Assert.True(endpoint.HasSeveralCases);
        Assert.Equal("2 cases", endpoint.CaseCountText);
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

        Assert.Contains(nameof(WorkspaceNodeViewModel.DisplayChildren), raised);
        Assert.Contains(nameof(WorkspaceNodeViewModel.CaseCountText), raised);
        Assert.Equal(2, root.Children.Single().DisplayChildren.Count());
    }

    /// <summary>A folder is never collapsed this way - only an endpoint's cases are.</summary>
    [Fact]
    public void A_folder_with_one_child_still_shows_it()
    {
        var folder = Load(new WorkspaceTreeNode("orders", "/w/collections/orders", true,
            [new WorkspaceTreeNode("Ping.json", "/w/collections/orders/Ping.json", false, [])]));

        Assert.Single(folder.DisplayChildren);
    }
}
