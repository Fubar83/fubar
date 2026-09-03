using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// Pointing at a difference in a pane and making it the current one.
///
/// The panes were read-only as a navigation surface: every difference was visible and the only way to
/// step to one was the toolbar, so saying "this one" about the difference already under the cursor was
/// impossible. It was the missing half of the map, the tree and Prev/Next all agreeing about a current
/// difference that nothing could SET by hand.
/// </summary>
public class ClickToSelectTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    private static DiffLine Modified(int n) => new(n, "a", n, "b", ChangeKind.Modified);

    /// <summary>Rows 2-3 and 7 differ, so there are two hunks with unchanged text around them.</summary>
    private static DiffPaneViewModel Pane()
    {
        var lines = new List<DiffLine>();
        for (var i = 1; i <= 10; i++)
        {
            lines.Add(i is 3 or 4 or 8 ? Modified(i) : Unchanged(i));
        }

        var pane = new DiffPaneViewModel();
        pane.Show(DiffResult.Create(lines));

        return pane;
    }

    [Fact]
    public void Clicking_inside_a_difference_selects_it()
    {
        var pane = Pane();

        pane.SelectDifferenceAtRow(3);   // second row of the first hunk

        Assert.Equal(0, pane.CurrentHunk);
    }

    [Fact]
    public void Clicking_inside_the_second_difference_selects_that_one()
    {
        var pane = Pane();

        pane.SelectDifferenceAtRow(7);

        Assert.Equal(1, pane.CurrentHunk);
    }

    [Fact]
    public void Clicking_unchanged_text_selects_nothing()
    {
        // The caret moves for all sorts of reasons - selecting text to copy, clicking to read - and
        // scrolling the window somewhere else because of it would make the panes unusable for the job
        // they exist for.
        var pane = Pane();
        pane.SelectDifferenceAtRow(3);

        pane.SelectDifferenceAtRow(0);

        Assert.Equal(0, pane.CurrentHunk);
    }

    [Fact]
    public void A_row_outside_the_document_is_ignored()
    {
        var pane = Pane();

        pane.SelectDifferenceAtRow(-1);
        pane.SelectDifferenceAtRow(999);

        Assert.Equal(-1, pane.CurrentHunk);
    }

    [Fact]
    public void Clicking_the_difference_already_current_leaves_it_alone()
    {
        // No churn: re-selecting would raise the scroll request again and yank a pane the reader had
        // just scrolled by hand.
        var pane = Pane();
        pane.SelectDifferenceAtRow(2);

        var scrolled = pane.ScrollToRow;
        pane.SelectDifferenceAtRow(3);   // same hunk

        Assert.Equal(0, pane.CurrentHunk);
        Assert.Equal(scrolled, pane.ScrollToRow);
    }

    [Fact]
    public void In_the_json_view_it_selects_the_semantic_change_too()
    {
        // Both halves are set where both apply, so the Json view's change and the text view's hunk
        // cannot disagree about which difference is current.
        var change = new JsonChange(
            JsonPath.Root.Property("total"),
            ChangeKind.Modified,
            new JsonAstScalar(JsonAstKind.Number, "1", null, new SourceSpan(3, 1, 3, 2)),
            new JsonAstScalar(JsonAstKind.Number, "2", null, new SourceSpan(3, 1, 3, 2)));

        var lines = new List<DiffLine>();
        for (var i = 1; i <= 5; i++)
        {
            lines.Add(i == 3 ? Modified(i) : Unchanged(i));
        }

        var pane = new DiffPaneViewModel();
        pane.Show(DiffResult.Create(lines), semanticChanges: [change], originalSemanticChanges: [change]);

        pane.SelectDifferenceAtRow(2);

        Assert.Equal(0, pane.CurrentSemanticChangeIndex);
    }
}
