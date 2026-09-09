using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Core.Tests;

/// <summary>
/// The selector grammar, and what it resolves to in a tree.
///
/// <para>The rule behind most of these: a selector that matched nothing must never quietly become a
/// selector that matched everything, and a selector nobody can parse must never quietly become the
/// whole workspace. Both are how a typo in a CI script comes to pass.</para>
/// </summary>
public class RunSelectorTests
{
    // ---- Parsing ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    public void Nothing_means_the_whole_workspace(string? text) =>
        Assert.Equal(RunSelectorKind.Everything, RunSelector.Parse(text).Kind);

    [Fact]
    public void A_path_is_a_path()
    {
        var selector = RunSelector.Parse("orders/get-order");

        Assert.Equal(RunSelectorKind.Path, selector.Kind);
        Assert.Equal("orders/get-order", selector.Path);
        Assert.Null(selector.Case);
    }

    [Fact]
    public void A_hash_names_one_case()
    {
        var selector = RunSelector.Parse("orders/get-order#not-found");

        Assert.Equal("orders/get-order", selector.Path);
        Assert.Equal("not-found", selector.Case);
    }

    [Fact]
    public void An_at_sign_names_a_batch()
    {
        var selector = RunSelector.Parse("@smoke");

        Assert.Equal(RunSelectorKind.Batch, selector.Kind);
        Assert.Equal("smoke", selector.BatchName);
    }

    /// <summary>This is Windows and half the paths pasted in come from Explorer.</summary>
    [Fact]
    public void Backslashes_are_accepted_and_normalised()
    {
        Assert.Equal("orders/get-order", RunSelector.Parse(@"orders\get-order").Path);
    }

    [Theory]
    [InlineData("@")]
    [InlineData("orders/get-order#")]
    [InlineData("#default")]
    public void A_malformed_selector_is_refused_rather_than_widened(string text) =>
        Assert.Throws<FormatException>(() => RunSelector.Parse(text));

    // ---- Expansion -------------------------------------------------------------------------------

    private static WorkspaceTreeNode Case(string name, string endpoint) =>
        new(name, $"{endpoint}/cases/{name}.json", false, []) { Kind = WorkspaceNodeKind.Case };

    private static WorkspaceTreeNode Endpoint(string name, string path, params WorkspaceTreeNode[] cases) =>
        new(name, path, true, cases) { Kind = WorkspaceNodeKind.Endpoint };

    /// <summary>
    ///   collections/
    ///     orders/
    ///       get-order/   (default, not-found)
    ///       list-orders/ (first-page)
    /// </summary>
    private static IReadOnlyList<WorkspaceTreeNode> Tree() =>
    [
        new WorkspaceTreeNode("orders", "/w/collections/orders", true,
        [
            Endpoint("get-order", "/w/collections/orders/get-order",
                Case("default", "/w/collections/orders/get-order"),
                Case("not-found", "/w/collections/orders/get-order")),
            Endpoint("list-orders", "/w/collections/orders/list-orders",
                Case("first-page", "/w/collections/orders/list-orders")),
        ]),
    ];

    [Fact]
    public void Everything_expands_to_every_case_in_the_tree()
    {
        var plan = TreeLookup.Expand(Tree(), RunSelector.Everything);

        Assert.Equal(["default", "not-found", "first-page"], plan.Steps.Select(s => s.CaseName));
    }

    [Fact]
    public void A_folder_expands_depth_first()
    {
        var plan = TreeLookup.Expand(Tree(), RunSelector.Parse("orders"));

        Assert.Equal(3, plan.Count);
    }

    [Fact]
    public void An_endpoint_expands_to_all_its_cases()
    {
        var plan = TreeLookup.Expand(Tree(), RunSelector.Parse("orders/get-order"));

        Assert.Equal(["default", "not-found"], plan.Steps.Select(s => s.CaseName));
    }

    [Fact]
    public void A_case_selector_expands_to_that_case_alone()
    {
        var plan = TreeLookup.Expand(Tree(), RunSelector.Parse("orders/get-order#not-found"));

        Assert.Equal("not-found", Assert.Single(plan.Steps).CaseName);
    }

    [Fact]
    public void A_path_that_names_nothing_is_refused_rather_than_run_as_an_empty_plan()
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => TreeLookup.Expand(Tree(), RunSelector.Parse("orders/nope")));

        Assert.Contains("orders/nope", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>Named and not found says what IS there, because the commonest reason to be here is a
    /// name half-remembered rather than a case that never existed.</summary>
    [Fact]
    public void A_case_that_does_not_exist_lists_the_ones_that_do()
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => TreeLookup.Expand(Tree(), RunSelector.Parse("orders/get-order#nope")));

        Assert.Contains("not-found", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_case_named_on_something_that_is_not_an_endpoint_is_refused()
    {
        Assert.Throws<InvalidOperationException>(
            () => TreeLookup.Expand(Tree(), RunSelector.Parse("orders#default")));
    }

    /// <summary>A selector resolves against the TREE, so it can only ever name something the tree
    /// shows - which is also what stops it leaving the workspace.</summary>
    [Fact]
    public void A_selector_cannot_climb_out_of_the_workspace()
    {
        Assert.Throws<InvalidOperationException>(
            () => TreeLookup.Expand(Tree(), RunSelector.Parse("../../etc/passwd")));
    }
}
