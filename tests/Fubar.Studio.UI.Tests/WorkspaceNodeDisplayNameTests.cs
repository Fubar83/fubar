using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// The tree shows a request's name, not the file it is stored in. Every request in a workspace is a
/// .json, so the extension distinguishes nothing while taking width from the names that do.
/// </summary>
public class WorkspaceNodeDisplayNameTests
{
    [Fact]
    public void A_request_file_is_shown_without_its_json_extension()
    {
        var node = new WorkspaceNodeViewModel("Create order.json", "/w/Create order.json", isDirectory: false);

        Assert.Equal("Create order", node.DisplayName);
    }

    [Fact]
    public void Only_the_extension_goes_not_dots_inside_the_name()
    {
        var node = new WorkspaceNodeViewModel("v2.create.json", "/w/v2.create.json", isDirectory: false);

        Assert.Equal("v2.create", node.DisplayName);
    }

    [Fact]
    public void A_folder_keeps_its_name_even_when_it_ends_in_json()
    {
        var node = new WorkspaceNodeViewModel("json", "/w/json", isDirectory: true);

        Assert.Equal("json", node.DisplayName);
    }

    /// <summary>Anything that is not a request keeps its extension, because there it says something.</summary>
    [Fact]
    public void Another_extension_is_left_alone()
    {
        var node = new WorkspaceNodeViewModel("notes.md", "/w/notes.md", isDirectory: false);

        Assert.Equal("notes.md", node.DisplayName);
    }

    [Fact]
    public void Renaming_the_node_restates_the_display_name()
    {
        var node = new WorkspaceNodeViewModel("old.json", "/w/old.json", isDirectory: false);
        var seen = new List<string>();
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceNodeViewModel.DisplayName))
            {
                seen.Add(node.DisplayName);
            }
        };

        node.Name = "new.json";

        Assert.Equal(["new"], seen);
    }
}
