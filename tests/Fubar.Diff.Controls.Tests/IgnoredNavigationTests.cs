using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// Shift+Alt+Up/Down: the same walk as Prev/Next, but stopping at the differences a rule is hiding.
///
/// Ordinary navigation steps past those, which is the point of having rules. This answers the other
/// question - "what exactly am I not being told?" - which gets asked right after adding a rule and once
/// more before trusting the diff. Before it, an ignored difference was a faint mark you had to find by
/// scrolling, which on a long file means not finding it.
/// </summary>
public class IgnoredNavigationTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    private static DiffLine Ignored(int n) => new(n, "x ", n, "x", ChangeKind.Unchanged) { IsIgnored = true };

    private static DiffLine Modified(int n) => new(n, "a", n, "b", ChangeKind.Modified);

    /// <summary>Rows 2-3 an ignored run, row 6 a real change.</summary>
    private static DiffPaneViewModel Pane()
    {
        var rows = Enumerable.Range(1, 9).Select(Unchanged).ToList();

        rows[2] = Ignored(3);
        rows[3] = Ignored(4);
        rows[6] = Modified(7);

        var pane = new DiffPaneViewModel();
        pane.Show(DiffResult.Create(rows));

        return pane;
    }

    [Fact]
    public void Ordinary_next_still_steps_straight_past_an_ignored_run()
    {
        // Unchanged behaviour, and the reason the new gesture had to be a separate one: stopping on
        // every ignored row by default would make the ignore rules pointless.
        var pane = Pane();

        pane.NextDifference();

        Assert.Equal(6, pane.ScrollToRow);
        Assert.True(pane.HasCurrentHunk);
    }

    [Fact]
    public void Stepping_with_ignored_included_stops_at_the_ignored_run_first()
    {
        var pane = Pane();

        pane.NextDifferenceIncludingIgnored();

        Assert.Equal(2, pane.ScrollToRow);
        Assert.Equal(2, pane.CurrentIgnoredRow);
        Assert.Equal(3, pane.CurrentIgnoredRowEnd);

        // And it is NOT a hunk, so nothing may claim one is selected - the merge commands act on the
        // current hunk and would otherwise act on whichever one was selected before.
        Assert.False(pane.HasCurrentHunk);
    }

    [Fact]
    public void It_then_reaches_the_real_change_and_clears_the_ignored_selection()
    {
        var pane = Pane();

        pane.NextDifferenceIncludingIgnored();
        pane.NextDifferenceIncludingIgnored();

        Assert.Equal(6, pane.ScrollToRow);
        Assert.True(pane.HasCurrentHunk);
        Assert.Equal(-1, pane.CurrentIgnoredRow);
    }

    [Fact]
    public void The_whole_run_is_one_stop()
    {
        // Two adjacent ignored rows are one thing that happened. Stopping once per row is not
        // navigation - the same rule the location map groups its marks by.
        var pane = Pane();

        pane.NextDifferenceIncludingIgnored();
        pane.NextDifferenceIncludingIgnored();
        pane.NextDifferenceIncludingIgnored();

        // Back to the ignored run, having visited it and the change exactly once each.
        Assert.Equal(2, pane.ScrollToRow);
    }

    [Fact]
    public void Previous_from_nowhere_lands_on_the_last_stop()
    {
        var pane = Pane();

        pane.PreviousDifferenceIncludingIgnored();

        Assert.Equal(6, pane.ScrollToRow);
    }

    [Fact]
    public void The_close_up_shows_the_ignored_run_it_stopped_at()
    {
        // It used to answer "No difference selected" about something the reader had just deliberately
        // navigated to, because the close-up was driven by the current HUNK and an ignored run is not
        // one. Being told to ignore a difference is not a reason to refuse to show it when asked.
        var pane = Pane();

        pane.NextDifferenceIncludingIgnored();

        Assert.NotNull(pane.DetailLeft);
        Assert.NotNull(pane.DetailRight);

        // Both sides of the run, and only the run.
        Assert.Equal("x \nx ", pane.DetailLeft!.Text);
        Assert.Equal("x\nx", pane.DetailRight!.Text);

        Assert.StartsWith("Ignored difference", pane.DetailCaption, StringComparison.Ordinal);
        Assert.Contains("lines 3–4", pane.DetailCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void Stepping_on_to_a_real_change_puts_that_in_the_close_up_instead()
    {
        var pane = Pane();

        pane.NextDifferenceIncludingIgnored();
        pane.NextDifferenceIncludingIgnored();

        Assert.Equal("a", pane.DetailLeft!.Text);
        Assert.Equal("b", pane.DetailRight!.Text);
        Assert.StartsWith("Difference", pane.DetailCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void Clicking_an_ignored_row_selects_its_whole_run()
    {
        // Pointing at a difference and saying "this one" has to work for the ones a rule is hiding too -
        // they are the ones a close-up is most needed for, since it is the only place that says what is
        // actually different about them. The whole run, not the clicked line: the run is the difference.
        var pane = Pane();

        pane.SelectDifferenceAtRow(3);

        Assert.Equal(2, pane.CurrentIgnoredRow);
        Assert.Equal(3, pane.CurrentIgnoredRowEnd);
        Assert.False(pane.HasCurrentHunk);
        Assert.NotNull(pane.DetailLeft);
    }

    [Fact]
    public void Clicking_a_real_change_clears_any_ignored_selection()
    {
        var pane = Pane();

        pane.SelectDifferenceAtRow(3);
        pane.SelectDifferenceAtRow(6);

        Assert.True(pane.HasCurrentHunk);
        Assert.Equal(-1, pane.CurrentIgnoredRow);
        Assert.StartsWith("Difference", pane.DetailCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void Stepping_follows_a_position_set_by_something_else()
    {
        // Position is read from the current row, not from an index this kept for itself, so a click, the
        // map or the tree moving the selection moves stepping with it. Keeping a private cursor in step
        // with four other things that can set the position is how they drift apart.
        var pane = Pane();

        pane.JumpToRow(6);
        pane.PreviousDifferenceIncludingIgnored();

        Assert.Equal(2, pane.ScrollToRow);
    }
}
