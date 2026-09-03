using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// The close-up on a MOVED block: both ends at once, from whichever end you selected.
///
/// A move is the one difference whose halves are not on the same rows - the block left the file at one
/// place and turned up at another - so it is two hunks, and the close-up used to build both of its
/// sides from ONE of them. That showed the block on one side and an empty box on the other, which is
/// the single comparison a move actually needs and the only one it could not make.
///
/// The two remain two DIFFERENCES: navigation stops at each end in turn and the counts are unchanged.
/// This is only about what the close-up is looking at.
/// </summary>
public class MovedBlockDetailTests
{
    /// <summary>
    /// A block of two lines that moved down: deleted from rows 1-2 on the left, inserted at rows 5-6 on
    /// the right, both ends tagged with the same move id.
    /// </summary>
    private static DiffPaneViewModel Pane()
    {
        var rows = new List<DiffLine>
        {
            new(1, "header", 1, "header", ChangeKind.Unchanged),
            new(2, "moved one", null, null, ChangeKind.Deleted) { LeftMoveId = 7 },
            new(3, "moved two", null, null, ChangeKind.Deleted) { LeftMoveId = 7 },
            new(4, "middle", 2, "middle", ChangeKind.Unchanged),
            new(5, "tail", 3, "tail", ChangeKind.Unchanged),
            new(null, null, 4, "moved one", ChangeKind.Inserted) { RightMoveId = 7 },
            new(null, null, 5, "moved two", ChangeKind.Inserted) { RightMoveId = 7 },
        };

        var pane = new DiffPaneViewModel();
        pane.Show(DiffResult.Create(rows));

        return pane;
    }

    [Fact]
    public void Selecting_the_end_it_left_shows_both_ends()
    {
        var pane = Pane();

        pane.SelectDifferenceAtRow(1);

        Assert.Equal("moved one\nmoved two", pane.DetailLeft!.Text);
        Assert.Equal("moved one\nmoved two", pane.DetailRight!.Text);
    }

    [Fact]
    public void Selecting_the_end_it_arrived_at_shows_both_ends_too()
    {
        // Whichever end you clicked - the close-up is about the move, not about which half of it you
        // happened to point at.
        var pane = Pane();

        pane.SelectDifferenceAtRow(5);

        Assert.Equal("moved one\nmoved two", pane.DetailLeft!.Text);
        Assert.Equal("moved one\nmoved two", pane.DetailRight!.Text);
    }

    [Fact]
    public void The_caption_says_it_moved_and_names_both_line_ranges()
    {
        var pane = Pane();

        pane.SelectDifferenceAtRow(1);

        Assert.Contains("moved", pane.DetailCaption, StringComparison.Ordinal);
        Assert.Contains("left lines 2–3", pane.DetailCaption, StringComparison.Ordinal);
        Assert.Contains("right lines 4–5", pane.DetailCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_ends_are_still_two_differences_to_navigation()
    {
        // The point of the whole change is what the close-up LOOKS at. Merging the ends into one
        // difference would change the counts, the map and next/previous, none of which was asked for -
        // and a block that moved really did leave one place and arrive at another.
        var pane = Pane();

        Assert.Equal(2, pane.Result.Hunks.Count);

        pane.NextDifference();
        var first = pane.CurrentHunk;

        pane.NextDifference();

        Assert.NotEqual(first, pane.CurrentHunk);
    }

    [Fact]
    public void An_ordinary_deletion_still_shows_an_empty_other_side()
    {
        // The counterpart lookup must not fire for a change that simply has nothing on one side. A
        // deletion that is only a deletion has no counterpart, and inventing one would put unrelated
        // text opposite it.
        var rows = new List<DiffLine>
        {
            new(1, "header", 1, "header", ChangeKind.Unchanged),
            new(2, "gone", null, null, ChangeKind.Deleted),
            new(3, "tail", 2, "tail", ChangeKind.Unchanged),
        };

        var pane = new DiffPaneViewModel();
        pane.Show(DiffResult.Create(rows));

        pane.SelectDifferenceAtRow(1);

        Assert.Equal("gone", pane.DetailLeft!.Text);
        Assert.Equal(string.Empty, pane.DetailRight!.Text);
        Assert.DoesNotContain("moved", pane.DetailCaption, StringComparison.Ordinal);
    }
}
