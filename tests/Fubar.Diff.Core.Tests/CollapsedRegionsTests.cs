using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Where the folds go. The rules that matter are the boundary ones - a change must never end up inside
/// a fold, and a fold must never be so small that clicking it costs more than it saves.
/// </summary>
public class CollapsedRegionsTests
{
    /// <summary>Rows from a sketch: '.' is unchanged context, 'x' a change, 'i' an ignored row.</summary>
    private static IReadOnlyList<DiffLine> Rows(string sketch)
    {
        var rows = new List<DiffLine>(sketch.Length);

        for (var i = 0; i < sketch.Length; i++)
        {
            rows.Add(sketch[i] switch
            {
                'x' => new DiffLine(i + 1, "changed", i + 1, "CHANGED", ChangeKind.Modified),
                'i' => new DiffLine(i + 1, "ignored", i + 1, "IGNORED", ChangeKind.Unchanged) { IsIgnored = true },
                _ => new DiffLine(i + 1, "same", i + 1, "same", ChangeKind.Unchanged),
            });
        }

        return rows;
    }

    private static IReadOnlyList<FoldRange> Fold(string sketch, int context = 3) =>
        CollapsedRegions.Compute(Rows(sketch), context);

    [Fact]
    public void A_long_run_of_context_between_two_changes_is_folded_with_context_kept_either_side()
    {
        //         0123456789...
        var folds = Fold("x..........x", context: 3);

        var fold = Assert.Single(folds);
        Assert.Equal(4, fold.StartRow);   // 1 change + 3 context
        Assert.Equal(7, fold.EndRow);     // 3 context before the next change at 11
    }

    [Fact]
    public void No_change_is_ever_inside_a_fold()
    {
        var rows = Rows("x....x.........x...x");
        var folds = CollapsedRegions.Compute(rows, 2);

        foreach (var fold in folds)
        {
            for (var i = fold.StartRow; i <= fold.EndRow; i++)
            {
                Assert.False(rows[i].IsChange, $"row {i} is a change and must not be folded");
            }
        }
    }

    [Fact]
    public void A_run_at_the_start_of_the_file_keeps_no_leading_context()
    {
        // There is no change above it to give context to, and three arbitrary lines of file header
        // before the first fold is exactly the noise this feature exists to remove.
        var folds = Fold("..........x", context: 3);

        var fold = Assert.Single(folds);
        Assert.Equal(0, fold.StartRow);
    }

    [Fact]
    public void A_run_at_the_end_of_the_file_keeps_no_trailing_context()
    {
        var rows = Rows("x..........");
        var folds = CollapsedRegions.Compute(rows, 3);

        var fold = Assert.Single(folds);
        Assert.Equal(rows.Count - 1, fold.EndRow);
    }

    [Fact]
    public void A_gap_too_small_to_be_worth_hiding_is_left_alone()
    {
        // Two changes four rows apart with three lines of context either side: there is nothing left
        // in the middle, and a placeholder hiding one line saves nothing.
        Assert.Empty(Fold("x......x", context: 3));
    }

    [Fact]
    public void Identical_files_fold_to_a_single_placeholder()
    {
        var fold = Assert.Single(Fold(".........."));

        Assert.Equal(0, fold.StartRow);
        Assert.Equal(9, fold.EndRow);
    }

    [Fact]
    public void A_file_that_is_all_changes_folds_nothing()
    {
        Assert.Empty(Fold("xxxxxxxxxx"));
    }

    [Fact]
    public void An_ignored_row_is_not_folded_away()
    {
        // Its faint band exists so the reader can see the rule is doing something; hiding it removes
        // the only evidence of that.
        var rows = Rows(".....i.....");
        var folds = CollapsedRegions.Compute(rows, 1);

        foreach (var fold in folds)
        {
            for (var i = fold.StartRow; i <= fold.EndRow; i++)
            {
                Assert.False(rows[i].IsIgnored, $"row {i} is ignored and must stay visible");
            }
        }

        // ...and it splits the run in two rather than merely being skipped.
        Assert.Equal(2, folds.Count);
    }

    [Fact]
    public void Zero_context_hides_everything_between_changes()
    {
        var folds = Fold("x.....x", context: 0);

        var fold = Assert.Single(folds);
        Assert.Equal(1, fold.StartRow);
        Assert.Equal(5, fold.EndRow);
    }

    [Fact]
    public void A_negative_context_is_treated_as_none_rather_than_throwing()
    {
        Assert.Single(Fold("x.....x", context: -5));
    }

    [Fact]
    public void An_empty_document_folds_nothing()
    {
        // Typed explicitly: an empty collection expression cannot choose between the DiffLine and
        // AlignedLine overloads, which address different coordinate systems.
        Assert.Empty(CollapsedRegions.Compute(Array.Empty<DiffLine>(), 3));
    }

    [Fact]
    public void Folds_come_back_in_document_order_and_never_overlap()
    {
        var folds = Fold("x........x........x........x", context: 2);

        Assert.True(folds.Count > 1);

        for (var i = 1; i < folds.Count; i++)
        {
            Assert.True(folds[i].StartRow > folds[i - 1].EndRow, "folds must be ordered and disjoint");
        }
    }
}
