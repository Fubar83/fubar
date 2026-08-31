using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Controls.Views;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// Which view is on screen, and how the change tree behaves once it is.
///
/// The rule worth pinning is that there is no longer a CHOICE about the Json view: how the files are
/// being compared decides it. Two controls both answering "text or Json" meant picking Text in one and
/// Json in the other was a contradiction the app had to resolve behind the user's back.
/// </summary>
public class JsonViewSelectionTests
{
    private static DiffResult Rows() => DiffResult.Create(
    [
        new DiffLine(1, "{", 1, "{", ChangeKind.Unchanged),
        new DiffLine(2, "  \"a\": 1", 2, "  \"a\": 2", ChangeKind.Modified),
        new DiffLine(3, "}", 3, "}", ChangeKind.Unchanged),
    ]);

    private static JsonChange Change(string name) => new(
        JsonPath.Root.Property(name),
        ChangeKind.Modified,
        new JsonAstScalar(JsonAstKind.Number, "1", null, new SourceSpan(2, 10, 2, 11)),
        new JsonAstScalar(JsonAstKind.Number, "2", null, new SourceSpan(2, 10, 2, 11)));

    private static DiffPaneViewModel Semantic()
    {
        var pane = new DiffPaneViewModel();

        pane.Show(
            Rows(),
            isSemantic: true,
            semanticChanges: [Change("a")],
            leftRawText: "{\n  \"a\": 1\n}",
            rightRawText: "{\n  \"a\": 2\n}",
            originalSemanticChanges: [Change("a")]);

        return pane;
    }

    private static DiffPaneViewModel Text()
    {
        var pane = new DiffPaneViewModel();
        pane.Show(Rows());

        return pane;
    }

    [AvaloniaFact]
    public void A_semantic_comparison_shows_the_Json_view()
    {
        var pane = Semantic();

        Assert.True(pane.IsJsonViewVisible);
        Assert.False(pane.IsSideBySideViewVisible);
        Assert.False(pane.IsUnifiedViewVisible);
    }

    [AvaloniaFact]
    public void A_text_comparison_shows_text()
    {
        var pane = Text();

        Assert.False(pane.IsJsonViewVisible);
        Assert.True(pane.IsSideBySideViewVisible);
    }

    [AvaloniaFact]
    public void The_layout_selector_offers_only_the_two_text_layouts()
    {
        // Json is not a layout of a text comparison, and having it here made this selector and the
        // Compare selector contradict each other.
        Assert.Equal([DiffViewMode.SideBySide, DiffViewMode.Unified], Text().AvailableViewModes);
    }

    [AvaloniaFact]
    public void The_layout_choice_cannot_pull_a_semantic_comparison_out_of_the_Json_view()
    {
        var pane = Semantic();

        pane.ViewMode = DiffViewMode.Unified;

        Assert.True(pane.IsJsonViewVisible);
        Assert.False(pane.IsUnifiedViewVisible);
    }

    [AvaloniaFact]
    public void A_preferred_layout_survives_the_next_comparison()
    {
        // It used to be reset on every comparison, which was only ever needed to stop Json being
        // selected for content that is not JSON - no longer possible.
        var pane = new DiffPaneViewModel();
        pane.Show(Rows());

        pane.ViewMode = DiffViewMode.Unified;
        pane.Show(Rows());

        Assert.Equal(DiffViewMode.Unified, pane.ViewMode);
        Assert.True(pane.IsUnifiedViewVisible);
    }

    // ---- The tree ---------------------------------------------------------------------------------

    private static (Window Window, TreeView Tree) ShowTree(DiffPaneViewModel pane)
    {
        var view = new JsonTreeView { DataContext = pane };
        var window = new Window { Content = view, Width = 500, Height = 400 };

        window.Show();
        window.UpdateLayout();

        return (window, view.GetVisualDescendants().OfType<TreeView>().First());
    }

    [AvaloniaFact]
    public void The_tree_shows_the_changes()
    {
        var (_, tree) = ShowTree(Semantic());

        Assert.NotNull(tree.ItemsSource);
        Assert.NotEmpty(tree.ItemsSource!.Cast<object>());
    }

    // ---- Which control a click on a row belongs to ------------------------------------------------

    /// <summary>A row with a child, so it has something to expand.</summary>
    private static TreeViewItem Parent()
    {
        var item = new TreeViewItem();
        item.Items.Add(new TreeViewItem());

        return item;
    }

    [AvaloniaFact]
    public void A_tap_on_the_row_expands_it()
    {
        // The point of the whole change: a tree row reads as one control, so clicking the name - or
        // the space beside it - does what clicking the chevron does.
        var row = Parent();
        var label = new TextBlock();

        Assert.Same(row, JsonTreeView.RowToToggle([label, row]));
    }

    [AvaloniaFact]
    public void A_tap_on_the_chevron_is_left_to_the_chevron()
    {
        // The subtle one. The chevron already toggles by itself, so handling it here as well would
        // toggle twice and land back where it started - the click would look broken rather than doing
        // nothing visible.
        var row = Parent();

        Assert.Null(JsonTreeView.RowToToggle([new ToggleButton(), row]));
    }

    [AvaloniaFact]
    public void A_tap_on_a_button_in_the_row_means_the_button()
    {
        var row = Parent();

        Assert.Null(JsonTreeView.RowToToggle([new Button(), row]));
    }

    [AvaloniaFact]
    public void A_leaf_row_has_nothing_to_expand()
    {
        // Toggling a row with no children changes nothing visible, which reads as the click having
        // been swallowed.
        Assert.Null(JsonTreeView.RowToToggle([new TextBlock(), new TreeViewItem()]));
    }

    [AvaloniaFact]
    public void A_tap_on_empty_space_below_the_rows_toggles_nothing()
    {
        Assert.Null(JsonTreeView.RowToToggle([new TreeView()]));
        Assert.Null(JsonTreeView.RowToToggle([]));
    }
}
