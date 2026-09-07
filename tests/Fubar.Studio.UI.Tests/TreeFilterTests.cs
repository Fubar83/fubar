using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// There was no way to find anything in the left pane, and an OpenAPI import routinely produces a
/// hundred requests in nested folders - so the app's flagship import created the one tree it could
/// not navigate.
/// </summary>
public class TreeFilterTests
{
    /// <summary>collections/Orders/{create,list}, collections/Users/get.</summary>
    private static WorkspaceNodeViewModel Tree()
    {
        var root = new WorkspaceNodeViewModel("root", "/w", isDirectory: true);

        var orders = new WorkspaceNodeViewModel("Orders", "/w/Orders", isDirectory: true);
        orders.Children.Add(new WorkspaceNodeViewModel("create.json", "/w/Orders/create.json", false)
        {
            Method = "POST",
            Url = "https://api.example.com/v1/orders",
        });
        orders.Children.Add(new WorkspaceNodeViewModel("list.json", "/w/Orders/list.json", false)
        {
            Method = "GET",
            Url = "https://api.example.com/v1/orders",
        });

        var users = new WorkspaceNodeViewModel("Users", "/w/Users", isDirectory: true);
        users.Children.Add(new WorkspaceNodeViewModel("get.json", "/w/Users/get.json", false)
        {
            Method = "GET",
            Url = "https://api.example.com/v1/users/{id}",
        });

        root.Children.Add(orders);
        root.Children.Add(users);
        return root;
    }

    private static WorkspaceNodeViewModel Folder(WorkspaceNodeViewModel root, string name) =>
        root.Children.Single(c => c.Name == name);

    [Fact]
    public void An_empty_filter_shows_everything()
    {
        var root = Tree();

        root.ApplyFilter("");

        Assert.All(root.Children, c => Assert.True(c.IsVisible));
    }

    [Fact]
    public void A_matching_file_keeps_its_folder_visible()
    {
        var root = Tree();

        root.ApplyFilter("create");

        Assert.True(Folder(root, "Orders").IsVisible);
        Assert.False(Folder(root, "Users").IsVisible);
        Assert.True(Folder(root, "Orders").Children.Single(c => c.Name == "create.json").IsVisible);
        Assert.False(Folder(root, "Orders").Children.Single(c => c.Name == "list.json").IsVisible);
    }

    // Three tests about folders expanding while filtering used to sit here. They were removed with
    // the behaviour: the request tree is always expanded now, and the IsExpanded they asserted on was
    // bound to no TreeViewItem, so the force-expand they guarded had never reached the screen.

    /// <summary>"Show me the Orders folder" means the folder, not an empty one.</summary>
    [Fact]
    public void A_folder_matching_by_name_keeps_all_its_children()
    {
        var root = Tree();

        root.ApplyFilter("Orders");

        Assert.True(Folder(root, "Orders").IsVisible);
        Assert.All(Folder(root, "Orders").Children, c => Assert.True(c.IsVisible));
    }

    /// <summary>The reason the URL is carried on the node at all: an OpenAPI import names files after
    /// operation ids, which are often the least memorable part of an endpoint.</summary>
    [Fact]
    public void A_url_fragment_matches()
    {
        var root = Tree();

        root.ApplyFilter("users/{id}");

        Assert.True(Folder(root, "Users").IsVisible);
        Assert.False(Folder(root, "Orders").IsVisible);
    }

    [Fact]
    public void A_method_matches()
    {
        var root = Tree();

        root.ApplyFilter("POST");

        Assert.True(Folder(root, "Orders").Children.Single(c => c.Name == "create.json").IsVisible);
        Assert.False(Folder(root, "Users").IsVisible);
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var root = Tree();

        root.ApplyFilter("ORDERS");

        Assert.True(Folder(root, "Orders").IsVisible);
    }

    [Fact]
    public void A_filter_matching_nothing_hides_everything()
    {
        var root = Tree();

        root.ApplyFilter("nothing-matches-this");

        Assert.All(root.Children, c => Assert.False(c.IsVisible));
    }

    /// <summary>
    /// A run walks the view-model tree (ToTreeNode), and the filter is a way to FIND things, never a
    /// way to select them. If filtering changed what a run sends, typing in a search box would
    /// silently change what gets executed against a real API.
    /// </summary>
    [Fact]
    public void Filtering_does_not_change_what_a_run_would_send()
    {
        var root = Tree();
        var before = root.ToTreeNode();

        root.ApplyFilter("create");
        var after = root.ToTreeNode();

        Assert.Equal(Count(before), Count(after));

        static int Count(Fubar.Studio.Core.Models.WorkspaceTreeNode node) =>
            (node.IsDirectory ? 0 : 1) + node.Children.Sum(Count);
    }

    /// <summary>
    /// A filter that finds a request inside a folded folder and leaves it folded has shown the user
    /// nothing. This rule existed once before and did nothing at all, because the IsExpanded it wrote to
    /// was bound to no container - so it is asserted on the way the row actually reads it.
    /// </summary>
    [Fact]
    public void A_folded_folder_opens_when_the_filter_finds_something_inside_it()
    {
        var root = Tree();
        var orders = Folder(root, "Orders");
        orders.IsExpanded = false;

        root.ApplyFilter("create");

        Assert.True(orders.IsExpanded);
        Assert.True(Folder(orders, "create.json").IsVisible);
    }

    [Fact]
    public void A_folder_with_no_match_inside_is_left_folded()
    {
        var root = Tree();
        var users = Folder(root, "Users");
        users.IsExpanded = false;

        root.ApplyFilter("create");

        Assert.False(users.IsExpanded);
        Assert.False(users.IsVisible);
    }

    /// <summary>Clearing the filter leaves open what it opened: re-folding would undo the folding a
    /// person did by hand, and nothing afterwards can tell the two apart.</summary>
    [Fact]
    public void Clearing_the_filter_leaves_what_it_opened_open()
    {
        var root = Tree();
        var orders = Folder(root, "Orders");
        orders.IsExpanded = false;
        root.ApplyFilter("create");

        root.ApplyFilter(null);

        Assert.True(orders.IsExpanded);
    }
}
