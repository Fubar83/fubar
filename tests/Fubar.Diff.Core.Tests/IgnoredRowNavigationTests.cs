using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// An ignored row is drawn, but is not a change: it forms no hunk, so next/previous steps straight
/// over it, the diff map has no tick for it, and it is not counted.
///
/// This falls out of the row being downgraded to <see cref="ChangeKind.Unchanged"/> with a flag
/// beside it rather than keeping its kind. That is easy to "tidy up" into an
/// <c>ChangeKind.Ignored</c> later, which would silently put every ignored row back into the hunk
/// list and make F8 stop on the fields the user explicitly asked not to be shown - so it is pinned
/// here.
/// </summary>
public class IgnoredRowNavigationTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    private static DiffLine Modified(int l, int r) => new(l, "before", r, "after", ChangeKind.Modified);

    /// <summary>Rows 1 and 3 differ; a rule covers row 1, so only row 3 is navigable.</summary>
    private static DiffResult Filtered()
    {
        var text = DiffResult.Create([Unchanged(1), Modified(2, 2), Unchanged(3), Modified(4, 4)]);

        return SemanticLineFilter.Apply(
            text,
            significantLeftLines: new HashSet<int> { 4 },
            significantRightLines: new HashSet<int> { 4 },
            ignoredLeftLines: new HashSet<int> { 2 },
            ignoredRightLines: new HashSet<int> { 2 });
    }

    [Fact]
    public void An_ignored_row_is_marked()
    {
        Assert.True(Filtered().Lines[1].IsIgnored);
    }

    [Fact]
    public void An_ignored_row_is_not_a_change()
    {
        var row = Filtered().Lines[1];

        Assert.False(row.IsChange);
        Assert.Equal(ChangeKind.Unchanged, row.Kind);
    }

    /// <summary>The one that matters: no hunk means nothing to navigate to.</summary>
    [Fact]
    public void An_ignored_row_forms_no_hunk()
    {
        var result = Filtered();

        var hunk = Assert.Single(result.Hunks);
        Assert.Equal(3, hunk.StartIndex);
    }

    [Fact]
    public void Next_steps_straight_over_an_ignored_row()
    {
        var result = Filtered();

        var first = HunkNavigator.Next(result.Hunks, -1);

        Assert.Equal(0, first);
        Assert.Equal(3, result.Hunks[first!.Value].StartIndex);
    }

    [Fact]
    public void Previous_steps_straight_over_an_ignored_row()
    {
        var result = Filtered();

        var last = HunkNavigator.Previous(result.Hunks, -1);

        Assert.Equal(3, result.Hunks[last!.Value].StartIndex);
    }

    /// <summary>A row with real changes on it stays navigable even if something ignored shares it.</summary>
    [Fact]
    public void A_row_that_is_both_significant_and_ignored_stays_a_change()
    {
        var text = DiffResult.Create([Unchanged(1), Modified(2, 2)]);

        var result = SemanticLineFilter.Apply(
            text,
            significantLeftLines: new HashSet<int> { 2 },
            significantRightLines: new HashSet<int> { 2 },
            ignoredLeftLines: new HashSet<int> { 2 },
            ignoredRightLines: new HashSet<int> { 2 });

        Assert.True(result.Lines[1].IsChange);
        Assert.False(result.Lines[1].IsIgnored);
        Assert.Single(result.Hunks);
    }

    /// <summary>Ignored rows must not count towards the change totals either.</summary>
    [Fact]
    public void An_ignored_row_is_not_counted()
    {
        var result = Filtered();

        Assert.Equal(1, result.Modified);
        Assert.False(result.AreIdentical);
    }

    /// <summary>The flag has to reach the renderers, which read AlignedLine rather than DiffLine.</summary>
    [Fact]
    public void The_flag_reaches_the_flattened_document_on_both_sides()
    {
        var result = Filtered();

        Assert.True(AlignedText.Build(result, DiffSide.Left).Lines[1].IsIgnored);
        Assert.True(AlignedText.Build(result, DiffSide.Right).Lines[1].IsIgnored);
    }
}
