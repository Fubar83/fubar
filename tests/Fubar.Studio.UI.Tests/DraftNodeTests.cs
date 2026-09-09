using Fubar.Studio.Core.Models;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// A node with no file behind it yet.
///
/// <para>The tree is otherwise a reflection of the file system, and it is reconciled against a fresh
/// scan on every file-system event - a <c>git pull</c>, a save two folders away, the debounced watcher
/// firing. A draft is the one thing in it that disk does not account for, so it has to be exempted
/// from that reconciliation twice: never removed for being absent, and no longer a draft the moment
/// the scan does report it.</para>
/// </summary>
public class DraftNodeTests
{
    private const string Endpoint = "/w/collections/orders/get-order";

    private sealed class Root : WorkspaceNodeViewModel
    {
        public Root() : base("collections", "/w/collections", true) { }

        public void Load(params WorkspaceTreeNode[] children) => SyncChildren(children);
    }

    private static WorkspaceTreeNode Case(string name) =>
        new(name, $"{Endpoint}/cases/{name}.json", false, []) { Kind = WorkspaceNodeKind.Case };

    private static WorkspaceTreeNode Batch(string name) =>
        new(name, $"{Endpoint}/batches/{name}.json", false, []) { Kind = WorkspaceNodeKind.Batch };

    private static WorkspaceTreeNode EndpointNode(
        WorkspaceTreeNode[] cases, params WorkspaceTreeNode[] batches) =>
        new("get-order", Endpoint, true, cases)
        {
            Kind = WorkspaceNodeKind.Endpoint,
            Batches = batches,
        };

    private static WorkspaceNodeViewModel Draft(string name, WorkspaceNodeKind kind) =>
        new(name + ".json",
            kind == WorkspaceNodeKind.Batch ? $"{Endpoint}/batches/{name}.json" : $"{Endpoint}/cases/{name}.json",
            isDirectory: false,
            kind)
        {
            IsDraft = true,
        };

    private static (Root Root, WorkspaceNodeViewModel Endpoint) Loaded(params WorkspaceTreeNode[] cases)
    {
        var root = new Root();
        root.Load(EndpointNode(cases));
        return (root, root.Children.Single());
    }

    // ---- Surviving the scan ----------------------------------------------------------------------

    /// <summary>The one that matters: a rescan must not delete what someone is in the middle of
    /// writing.</summary>
    [Fact]
    public void A_drafted_case_survives_a_rescan()
    {
        var (root, endpoint) = Loaded(Case("default"));
        endpoint.Children.Add(Draft("new-case", WorkspaceNodeKind.Case));

        root.Load(EndpointNode([Case("default")]));

        Assert.Equal(["default", "new-case"], root.Children.Single().Children.Select(c => c.DisplayName));
        Assert.True(root.Children.Single().Children[1].IsDraft);
    }

    [Fact]
    public void A_drafted_batch_survives_a_rescan()
    {
        var (root, endpoint) = Loaded(Case("default"));
        endpoint.Batches.Add(Draft("new-batch", WorkspaceNodeKind.Batch));

        root.Load(EndpointNode([Case("default")], Batch("happy")));

        Assert.Equal(["happy", "new-batch"], root.Children.Single().Batches.Select(b => b.DisplayName));
    }

    /// <summary>Saving is what makes one real, and the scan reporting it is the only way to know.</summary>
    [Fact]
    public void A_draft_the_scan_reports_stops_being_one()
    {
        var (root, endpoint) = Loaded(Case("default"));
        endpoint.Children.Add(Draft("new-case", WorkspaceNodeKind.Case));

        root.Load(EndpointNode([Case("default"), Case("new-case")]));

        var saved = root.Children.Single().Children.Single(c => c.DisplayName == "new-case");

        Assert.False(saved.IsDraft);
        Assert.False(saved.IsUnsaved);
    }

    /// <summary>The scan does not mention drafts, so reconciliation moves the real nodes around
    /// whatever position one happens to hold. Last is the position nothing else competes for.</summary>
    [Fact]
    public void Drafts_sit_last()
    {
        var (root, endpoint) = Loaded(Case("b"));
        endpoint.Children.Insert(0, Draft("new-case", WorkspaceNodeKind.Case));

        root.Load(EndpointNode([Case("a"), Case("b")]));

        Assert.Equal(["a", "b", "new-case"], root.Children.Single().Children.Select(c => c.DisplayName));
    }

    // ---- What a draft is not ---------------------------------------------------------------------

    /// <summary>
    /// A draft is never sent.
    /// </summary>
    /// <remarks>
    /// <c>ToTreeNode</c> feeds <c>RunPlan</c> and the runner reads from disk, so a case that has not
    /// been saved has no file to send - including it would turn "run this endpoint" into a run with a
    /// step that cannot possibly work.
    /// </remarks>
    [Fact]
    public void A_draft_is_not_part_of_a_run()
    {
        var (_, endpoint) = Loaded(Case("default"));
        endpoint.Children.Add(Draft("new-case", WorkspaceNodeKind.Case));

        Assert.Equal(["default"], endpoint.ToTreeNode().Children.Select(c => c.Name));
    }

    [Fact]
    public void A_draft_reads_as_unsaved()
    {
        var draft = Draft("new-case", WorkspaceNodeKind.Case);

        Assert.True(draft.IsUnsaved);
        Assert.False(draft.IsDirty);
    }

    /// <summary>An edited-but-saved node and a never-saved one both owe a Save, and the tree's dot
    /// means exactly that.</summary>
    [Fact]
    public void An_edited_node_also_reads_as_unsaved()
    {
        var (_, endpoint) = Loaded(Case("default"));
        var node = endpoint.Children.Single();

        Assert.False(node.IsUnsaved);

        node.IsDirty = true;

        Assert.True(node.IsUnsaved);
    }

    /// <summary>A draft still counts towards what the endpoint holds - it is there, and the row says
    /// so - which is what makes it findable while it is unsaved.</summary>
    [Fact]
    public void A_draft_is_shown_beneath_its_endpoint()
    {
        var (_, endpoint) = Loaded(Case("default"));
        endpoint.Children.Add(Draft("new-case", WorkspaceNodeKind.Case));

        Assert.Equal(2, endpoint.DisplayChildren.Count());
        Assert.Equal("2 cases", endpoint.ContentsText);
    }

    // ---- Endpoints and requests ------------------------------------------------------------------

    /// <summary>An endpoint is a DIRECTORY holding endpoint.json, so a drafted one is a directory
    /// that does not exist yet - and it is still never part of a run.</summary>
    [Fact]
    public void A_drafted_endpoint_is_a_directory_and_is_not_run()
    {
        var root = new Root();
        root.Load(EndpointNode([Case("default")]));

        root.Children.Add(new WorkspaceNodeViewModel(
            "New Endpoint", "/w/collections/orders/New Endpoint", isDirectory: true, WorkspaceNodeKind.Endpoint)
        {
            IsDraft = true,
        });

        Assert.Equal(2, root.Children.Count);
        Assert.Single(root.ToTreeNode().Children);
    }

    /// <summary>A drafted request survives a rescan the same way, because nothing about the rule is
    /// specific to cases.</summary>
    [Fact]
    public void A_drafted_request_survives_a_rescan()
    {
        var root = new Root();
        root.Load(EndpointNode([Case("default")]));

        root.Children.Add(new WorkspaceNodeViewModel(
            "New Request.json", "/w/collections/New Request.json", isDirectory: false, WorkspaceNodeKind.Request)
        {
            IsDraft = true,
        });

        root.Load(EndpointNode([Case("default")]));

        Assert.Equal(["get-order", "New Request"], root.Children.Select(c => c.DisplayName));
    }
}
