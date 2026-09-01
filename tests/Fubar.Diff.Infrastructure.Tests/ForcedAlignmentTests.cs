using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// Pairings the user made by hand, which the aligner must honour.
///
/// The promise is absolute, and that is the point: an anchor is not a hint the engine weighs against
/// its own opinion, it is the answer. Everything else in the app - ignore whitespace, ignore case,
/// ignore comments - changes what COUNTS as a difference, and none of them can change which lines
/// correspond, which is the one thing an aligner sometimes gets wrong beyond rescue.
/// </summary>
public class ForcedAlignmentTests
{
    private static IReadOnlyList<DiffLine> Align(string[] left, string[] right, params AlignmentAnchor[] anchors) =>
        new DiffPlexDiffEngine().Align(left, right, new ComparisonOptions { Alignments = anchors });

    private static (int? Left, int? Right) PairAt(IReadOnlyList<DiffLine> rows, int index) =>
        (rows[index].LeftNumber, rows[index].RightNumber);

    [Fact]
    public void The_anchored_lines_are_paired_with_each_other()
    {
        // Nothing in this content suggests pairing line 2 with line 4; the user said so.
        var rows = Align(
            ["a", "target", "c"],
            ["x", "y", "z", "target", "w"],
            new AlignmentAnchor(2, 4));

        var anchored = rows.Single(r => r.LeftNumber == 2);
        Assert.Equal(4, anchored.RightNumber);
        Assert.Equal(ChangeKind.Unchanged, anchored.Kind);
    }

    [Fact]
    public void Two_lines_that_differ_are_still_paired_and_still_reported_as_different()
    {
        // "These correspond" is not "these match". Calling a rewritten line unchanged because someone
        // pointed at it would hide the very difference they were lining up to read.
        var rows = Align(["alpha", "was rewritten"], ["alpha", "completely different"],
            new AlignmentAnchor(2, 2));

        var anchored = rows.Single(r => r.LeftNumber == 2);
        Assert.Equal(2, anchored.RightNumber);
        Assert.Equal(ChangeKind.Modified, anchored.Kind);
    }

    [Fact]
    public void The_region_before_an_anchor_is_aligned_on_its_own()
    {
        // Two lines on the left, one on the right, before the anchor: the region has to resolve
        // itself, and cannot borrow lines from beyond the anchor to do it.
        var rows = Align(
            ["one", "two", "anchor"],
            ["one", "anchor"],
            new AlignmentAnchor(3, 2));

        Assert.Equal((1, 1), PairAt(rows, 0));
        Assert.Equal((2, null), PairAt(rows, 1));
        Assert.Equal((3, 2), PairAt(rows, 2));
    }

    [Fact]
    public void The_region_after_an_anchor_is_aligned_on_its_own()
    {
        var rows = Align(
            ["anchor", "tail"],
            ["anchor", "different tail", "extra"],
            new AlignmentAnchor(1, 1));

        Assert.Equal((1, 1), PairAt(rows, 0));
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r is { RightNumber: 3, LeftNumber: null });
    }

    [Fact]
    public void Every_line_still_appears_exactly_once_and_in_order()
    {
        // The invariant that catches a stitching mistake generically - an anchor splits the problem
        // into three pieces that have to be renumbered back into one document.
        var left = Enumerable.Range(1, 40).Select(i => $"L{i}").ToArray();
        var right = Enumerable.Range(1, 30).Select(i => $"R{i}").ToArray();

        var rows = Align(left, right, new AlignmentAnchor(20, 10));

        Assert.Equal(
            Enumerable.Range(1, 40),
            rows.Select(r => r.LeftNumber).Where(n => n is not null).Select(n => n!.Value));

        Assert.Equal(
            Enumerable.Range(1, 30),
            rows.Select(r => r.RightNumber).Where(n => n is not null).Select(n => n!.Value));
    }

    [Fact]
    public void Several_anchors_are_all_honoured()
    {
        var rows = Align(
            ["a", "b", "c", "d", "e"],
            ["v", "w", "x", "y", "z"],
            new AlignmentAnchor(2, 4),
            new AlignmentAnchor(4, 5));

        Assert.Equal(4, rows.Single(r => r.LeftNumber == 2).RightNumber);
        Assert.Equal(5, rows.Single(r => r.LeftNumber == 4).RightNumber);
    }

    [Fact]
    public void An_anchor_past_the_end_of_a_file_is_ignored_rather_than_moved()
    {
        // Stale: the file was edited or replaced under it. Clamping it to the nearest line that does
        // exist would be inventing an instruction the user never gave.
        var rows = Align(["a", "b"], ["a", "b"], new AlignmentAnchor(2, 900));

        Assert.All(rows, row => Assert.Equal(ChangeKind.Unchanged, row.Kind));
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void An_anchor_needs_a_line_on_both_sides()
    {
        // Zero is what "the caret is on a filler" would produce if it ever reached this far.
        Assert.False(new AlignmentAnchor(0, 3).IsValid);
        Assert.False(new AlignmentAnchor(3, 0).IsValid);
        Assert.True(new AlignmentAnchor(3, 1).IsValid);
    }

    // ---- Keeping the set usable ------------------------------------------------------------------

    [Fact]
    public void A_second_anchor_that_crosses_the_first_replaces_it()
    {
        // Honouring both would need the lines between them to run backwards on one side. Rather than
        // refuse - leaving the user to work out which forgotten decision is in the way - the newest
        // instruction wins.
        var anchors = AlignmentAnchors.Add([], new AlignmentAnchor(10, 20));
        anchors = AlignmentAnchors.Add(anchors, new AlignmentAnchor(12, 15));

        Assert.Equal(new AlignmentAnchor(12, 15), Assert.Single(anchors));
    }

    [Fact]
    public void Re_anchoring_a_line_replaces_what_it_was_paired_with()
    {
        var anchors = AlignmentAnchors.Add([], new AlignmentAnchor(10, 20));
        anchors = AlignmentAnchors.Add(anchors, new AlignmentAnchor(10, 25));

        Assert.Equal(new AlignmentAnchor(10, 25), Assert.Single(anchors));
    }

    [Fact]
    public void Anchors_that_do_not_conflict_are_all_kept_in_order()
    {
        var anchors = AlignmentAnchors.Add([], new AlignmentAnchor(30, 40));
        anchors = AlignmentAnchors.Add(anchors, new AlignmentAnchor(10, 20));

        Assert.Equal([new AlignmentAnchor(10, 20), new AlignmentAnchor(30, 40)], anchors);
    }

    [Fact]
    public void A_set_that_arrives_out_of_order_cannot_make_the_aligner_emit_impossible_rows()
    {
        // Usable is the last line of defence: whatever a caller hands over, what comes back is sorted
        // on the left and increasing on the right.
        var usable = AlignmentAnchors.Usable(
            [new AlignmentAnchor(10, 50), new AlignmentAnchor(20, 30)],
            leftLines: 100,
            rightLines: 100);

        Assert.Equal(new AlignmentAnchor(10, 50), Assert.Single(usable));
    }
}
