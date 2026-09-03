using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Core.Tests;

/// <summary>
/// Flattening a workspace subtree into the order its requests will be sent.
///
/// Order is not cosmetic here. Captures chain, so request 3 routinely depends on request 1 having run,
/// and the tree is the only place that dependency is written down.
/// </summary>
public class RunPlanTests
{
    private static WorkspaceTreeNode Request(string name, string path) =>
        new(name, path, IsDirectory: false, [], new RequestSummary("GET", false));

    private static WorkspaceTreeNode Folder(string name, string path, params WorkspaceTreeNode[] children) =>
        new(name, path, IsDirectory: true, children);

    /// <summary>
    ///   collections/
    ///     1 Login          (request)
    ///     Orders/          (folder)
    ///       2 Create
    ///       3 Get
    ///     4 Logout         (request)
    /// </summary>
    private static WorkspaceTreeNode Tree() => Folder("collections", "/w/collections",
        Request("1 Login", "/w/collections/1 Login/request.json"),
        Folder("Orders", "/w/collections/Orders",
            Request("2 Create", "/w/collections/Orders/2 Create/request.json"),
            Request("3 Get", "/w/collections/Orders/3 Get/request.json")),
        Request("4 Logout", "/w/collections/4 Logout/request.json"));

    [Fact]
    public void The_order_is_the_trees_order_depth_first()
    {
        // Not alphabetical, and not requests-before-folders. A run that sent them in an order the user
        // cannot see anywhere would break every collection whose later requests need an earlier login.
        var plan = RunPlan.From(Tree());

        Assert.Equal(["1 Login", "2 Create", "3 Get", "4 Logout"], plan.Steps.Select(s => s.Name));
    }

    [Fact]
    public void Steps_are_numbered_from_one()
    {
        Assert.Equal([1, 2, 3, 4], RunPlan.From(Tree()).Steps.Select(s => s.Order));
    }

    [Fact]
    public void A_request_node_on_its_own_is_a_plan_of_one()
    {
        // So "run just this request" needs no second path through the runner.
        var plan = RunPlan.From(Request("Solo", "/w/collections/Solo/request.json"));

        Assert.Equal(1, plan.Count);
        Assert.Equal("Solo", plan.Steps[0].Name);
    }

    [Fact]
    public void A_step_knows_the_folder_it_came_from()
    {
        var plan = RunPlan.From(Tree());

        Assert.Equal("/w/collections/Orders", plan.Steps.Single(s => s.Name == "2 Create").FolderPath);
    }

    [Fact]
    public void An_empty_folder_yields_an_empty_plan_rather_than_throwing()
    {
        var plan = RunPlan.From(Folder("empty", "/w/collections/empty"));

        Assert.True(plan.IsEmpty);
        Assert.Equal(0, plan.Count);
    }

    [Fact]
    public void Folders_holding_only_folders_are_walked_through()
    {
        var plan = RunPlan.From(Folder("a", "/a", Folder("b", "/a/b", Folder("c", "/a/b/c",
            Request("deep", "/a/b/c/deep/request.json")))));

        Assert.Equal("deep", Assert.Single(plan.Steps).Name);
    }

    // ---- Multi-select --------------------------------------------------------------------------

    [Fact]
    public void Selecting_a_folder_AND_a_request_inside_it_does_not_run_that_request_twice()
    {
        // What a multi-select means is "run these", not "run these, and again". A duplicate send is not
        // a cosmetic problem when the request is a POST.
        var tree = Tree();
        var orders = tree.Children.Single(c => c.Name == "Orders");
        var create = orders.Children.Single(c => c.Name == "2 Create");

        var plan = RunPlan.From([orders, create]);

        Assert.Equal(["2 Create", "3 Get"], plan.Steps.Select(s => s.Name));
    }

    [Fact]
    public void Multi_select_keeps_the_order_the_nodes_were_given_in()
    {
        var tree = Tree();
        var logout = tree.Children.Single(c => c.Name == "4 Logout");
        var login = tree.Children.Single(c => c.Name == "1 Login");

        Assert.Equal(["4 Logout", "1 Login"], RunPlan.From([logout, login]).Steps.Select(s => s.Name));
    }

    [Fact]
    public void Multi_select_renumbers_so_the_orders_stay_contiguous()
    {
        var tree = Tree();
        var orders = tree.Children.Single(c => c.Name == "Orders");
        var create = orders.Children.Single(c => c.Name == "2 Create");

        Assert.Equal([1, 2], RunPlan.From([orders, create]).Steps.Select(s => s.Order));
    }

    // ---- Filtering -----------------------------------------------------------------------------

    [Fact]
    public void A_name_filter_keeps_only_matching_steps_and_renumbers_them()
    {
        // The report's "N of M" has to mean what was attempted, not what was in the folder.
        var plan = RunPlan.From(Tree()).Filtered("o");

        Assert.Equal(["1 Login", "4 Logout"], plan.Steps.Select(s => s.Name));
        Assert.Equal([1, 2], plan.Steps.Select(s => s.Order));
    }

    [Fact]
    public void The_filter_is_case_insensitive()
    {
        Assert.Equal(2, RunPlan.From(Tree()).Filtered("LOG").Count);
    }

    [Fact]
    public void A_blank_filter_is_not_a_filter()
    {
        Assert.Equal(4, RunPlan.From(Tree()).Filtered(null).Count);
        Assert.Equal(4, RunPlan.From(Tree()).Filtered("   ").Count);
    }

    [Fact]
    public void A_filter_that_matches_nothing_gives_an_empty_plan()
    {
        Assert.True(RunPlan.From(Tree()).Filtered("nothing-is-called-this").IsEmpty);
    }
}
