using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Core.Tests.Running;

/// <summary>
/// What a batch step calls a node - the grammar <see cref="TreeLookup"/> reads the other way round.
///
/// <para>The pairing is the point: a selector this produces has to be one the planner can resolve, so
/// every case here is followed by the round trip through <c>TreeLookup.Find</c>.</para>
/// </summary>
public class BatchSelectorTests
{
    private static readonly string Collections = Path.Combine("w", "collections");

    private static string Path_(params string[] parts) => Path.Combine([Collections, .. parts]);

    private static string? For(string full, WorkspaceNodeKind kind) =>
        BatchSelector.For(full, kind, Collections);

    // ---- The ordinary shapes ---------------------------------------------------------------------

    [Fact]
    public void A_request_is_named_by_its_path_under_collections()
    {
        Assert.Equal("orders/_prices.json", For(Path_("orders", "_prices.json"), WorkspaceNodeKind.Request));
    }

    [Fact]
    public void An_endpoint_is_named_by_its_directory()
    {
        Assert.Equal("orders/get-order", For(Path_("orders", "get-order"), WorkspaceNodeKind.Endpoint));
    }

    [Fact]
    public void A_folder_is_named_too_and_expands_to_everything_under_it()
    {
        Assert.Equal("orders", For(Path_("orders"), WorkspaceNodeKind.Folder));
    }

    /// <summary>
    /// A step addresses a case as an endpoint plus a case NAME, so the selector is the endpoint -
    /// <c>cases/created.json</c> is not a path the planner can find.
    /// </summary>
    [Fact]
    public void A_case_resolves_to_its_endpoint()
    {
        Assert.Equal(
            "orders/get-order",
            For(Path_("orders", "get-order", "cases", "created.json"), WorkspaceNodeKind.Case));
    }

    // ---- What cannot be a step -------------------------------------------------------------------

    /// <summary>A batch listing a batch is a nesting neither the format nor the planner has.</summary>
    [Fact]
    public void A_batch_is_not_something_a_step_can_name()
    {
        Assert.Null(For(Path_("orders", "get-order", "batches", "happy.json"), WorkspaceNodeKind.Batch));
    }

    [Fact]
    public void The_collections_directory_itself_is_not_a_step()
    {
        Assert.Null(For(Collections, WorkspaceNodeKind.Folder));
    }

    [Fact]
    public void Something_outside_the_tree_is_not_a_step()
    {
        Assert.Null(For(Path.Combine("w", "environments", "staging.json"), WorkspaceNodeKind.Request));
        Assert.Null(For("", WorkspaceNodeKind.Request));
        Assert.Null(For(null!, WorkspaceNodeKind.Request));
    }

    // ---- The round trip ---------------------------------------------------------------------------

    /// <summary>
    /// The assertion that matters: what this writes into a batch is what <see cref="TreeLookup.Find"/>
    /// resolves. A selector that looks right and finds nothing would make a batch that errors on every
    /// step it was seeded with.
    /// </summary>
    [Theory]
    [InlineData("_prices.json", WorkspaceNodeKind.Request)]
    [InlineData("get-order", WorkspaceNodeKind.Endpoint)]
    public void What_it_writes_is_what_the_planner_finds(string leaf, WorkspaceNodeKind kind)
    {
        var node = new WorkspaceTreeNode(
            leaf, Path_("orders", leaf), kind == WorkspaceNodeKind.Endpoint, []) { Kind = kind };

        var orders = new WorkspaceTreeNode("orders", Path_("orders"), true, [node]);

        var selector = For(node.FullPath, kind);

        Assert.NotNull(selector);
        Assert.Same(node, TreeLookup.Find([orders], selector!));
    }
}
