using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests.Comparison;

/// <summary>
/// Recognising a block that moved rather than changed.
///
/// The two things worth pinning are opposite failures. It has to FIND the move on a reordered file,
/// which is the case that makes a diff unreadable; and it has to REFUSE to guess when the same text
/// appears more than once, because a confident line drawn between two unrelated closing braces is
/// worse than no line at all.
/// </summary>
public class MoveDetectorTests
{
    /// <summary>
    /// Builds rows the way an aligner would, from a compact script: each entry is the kind and the
    /// text, and line numbers are handed out per side in order.
    ///
    /// A <see cref="ChangeKind.Modified"/> entry may spell its two sides as <c>"left => right"</c>,
    /// since that row is the interesting one here - the aligner paired two DIFFERENT lines, and this
    /// pass looks at each of them separately.
    /// </summary>
    private static (IReadOnlyList<DiffLine> Rows, string[] Left, string[] Right) Align(
        params (ChangeKind Kind, string Text)[] script)
    {
        var rows = new List<DiffLine>();
        var left = new List<string>();
        var right = new List<string>();

        foreach (var (kind, text) in script)
        {
            var parts = text.Split(" => ", 2, StringSplitOptions.None);
            var leftText = parts[0];
            var rightText = parts.Length == 2 ? parts[1] : parts[0];

            int? leftNumber = null;
            int? rightNumber = null;

            if (kind is ChangeKind.Unchanged or ChangeKind.Modified or ChangeKind.Deleted)
            {
                left.Add(leftText);
                leftNumber = left.Count;
            }

            if (kind is ChangeKind.Unchanged or ChangeKind.Modified or ChangeKind.Inserted)
            {
                right.Add(rightText);
                rightNumber = right.Count;
            }

            rows.Add(new DiffLine(
                leftNumber,
                leftNumber is null ? null : leftText,
                rightNumber,
                rightNumber is null ? null : rightText,
                kind));
        }

        return (rows, [.. left], [.. right]);
    }

    /// <summary>An array rather than the interface, purely so the assertions can use ranges.</summary>
    private static DiffLine[] Detect(params (ChangeKind Kind, string Text)[] script)
    {
        var (rows, left, right) = Align(script);

        return [.. MoveDetector.Detect(rows, left, right)];
    }

    [Fact]
    public void A_block_removed_here_and_added_there_is_one_move()
    {
        var rows = Detect(
            (ChangeKind.Deleted, "void Helper() {"),
            (ChangeKind.Deleted, "    help();"),
            (ChangeKind.Deleted, "}"),
            (ChangeKind.Unchanged, "void Main() {"),
            (ChangeKind.Unchanged, "    Helper();"),
            (ChangeKind.Unchanged, "}"),
            (ChangeKind.Inserted, "void Helper() {"),
            (ChangeKind.Inserted, "    help();"),
            (ChangeKind.Inserted, "}"));

        Assert.All(rows[..3], r => Assert.True(r.IsMoved));
        Assert.All(rows[6..], r => Assert.True(r.IsMoved));

        // Both halves of one move, so one id - which is what lets a reader be told the two places are
        // the same place.
        Assert.Equal(rows[0].LeftMoveId, rows[6].RightMoveId);
        Assert.All(rows[3..6], r => Assert.False(r.IsMoved));
    }

    [Fact]
    public void The_kinds_are_left_exactly_as_they_were()
    {
        // A move is extra information about a change, not a replacement for it. Rewriting the kinds
        // would take these rows out of the hunks, the counts, the navigation and the patch - all of
        // which have to keep describing what is genuinely on disk.
        var rows = Detect(
            (ChangeKind.Deleted, "a();"),
            (ChangeKind.Deleted, "b();"),
            (ChangeKind.Unchanged, "keep();"),
            (ChangeKind.Inserted, "a();"),
            (ChangeKind.Inserted, "b();"));

        Assert.Equal(ChangeKind.Deleted, rows[0].Kind);
        Assert.Equal(ChangeKind.Inserted, rows[4].Kind);
        Assert.True(rows[0].IsChange);
    }

    [Fact]
    public void A_single_distinctive_line_can_move()
    {
        // Moving one import or one field is common and worth seeing. The guard against noise is
        // uniqueness, not a minimum length - a length rule would miss this and still pair braces.
        var rows = Detect(
            (ChangeKind.Deleted, "using System.Text.Json;"),
            (ChangeKind.Unchanged, "using System;"),
            (ChangeKind.Inserted, "using System.Text.Json;"));

        Assert.True(rows[0].IsMoved);
        Assert.True(rows[2].IsMoved);
    }

    [Fact]
    public void Text_that_appears_twice_on_a_side_is_never_paired()
    {
        // The case the whole design turns on. Two identical closing braces removed and one added
        // could be either pairing, and the tool has no way to know - so it says nothing, rather than
        // drawing a line between two unrelated braces and inviting the reader to believe it.
        var rows = Detect(
            (ChangeKind.Deleted, "}"),
            (ChangeKind.Unchanged, "middle();"),
            (ChangeKind.Deleted, "}"),
            (ChangeKind.Unchanged, "end();"),
            (ChangeKind.Inserted, "}"));

        Assert.All(rows, r => Assert.False(r.IsMoved));
    }

    [Fact]
    public void Two_different_blocks_moving_get_different_ids()
    {
        var rows = Detect(
            (ChangeKind.Deleted, "first();"),
            (ChangeKind.Unchanged, "keep();"),
            (ChangeKind.Deleted, "second();"),
            (ChangeKind.Unchanged, "keep2();"),
            (ChangeKind.Inserted, "second();"),
            (ChangeKind.Unchanged, "keep3();"),
            (ChangeKind.Inserted, "first();"));

        Assert.True(rows[0].IsMoved);
        Assert.True(rows[2].IsMoved);
        Assert.Equal(rows[0].LeftMoveId, rows[6].RightMoveId);
        Assert.Equal(rows[2].LeftMoveId, rows[4].RightMoveId);
        Assert.NotEqual(rows[0].LeftMoveId, rows[2].LeftMoveId);
    }

    [Fact]
    public void A_block_that_only_partly_reappears_is_not_a_move()
    {
        // Runs are matched whole. Half a method turning up elsewhere is a rewrite that happens to
        // share some lines, and calling it a move would tell the reader to skip the part that changed.
        var rows = Detect(
            (ChangeKind.Deleted, "void M() {"),
            (ChangeKind.Deleted, "    a();"),
            (ChangeKind.Deleted, "    b();"),
            (ChangeKind.Unchanged, "keep();"),
            (ChangeKind.Inserted, "void M() {"),
            (ChangeKind.Inserted, "    a();"));

        Assert.All(rows, r => Assert.False(r.IsMoved));
    }

    [Fact]
    public void Blank_lines_alone_are_never_a_move()
    {
        // Blank runs are identical to each other everywhere by definition; pairing two of them is
        // technically true and says nothing.
        var rows = Detect(
            (ChangeKind.Deleted, "   "),
            (ChangeKind.Unchanged, "keep();"),
            (ChangeKind.Inserted, "   "));

        Assert.All(rows, r => Assert.False(r.IsMoved));
    }

    [Fact]
    public void A_blank_line_inside_a_moved_block_is_still_part_of_it()
    {
        var rows = Detect(
            (ChangeKind.Deleted, "void M() {"),
            (ChangeKind.Deleted, ""),
            (ChangeKind.Deleted, "}"),
            (ChangeKind.Unchanged, "keep();"),
            (ChangeKind.Inserted, "void M() {"),
            (ChangeKind.Inserted, ""),
            (ChangeKind.Inserted, "}"));

        Assert.All(rows[..3], r => Assert.True(r.IsMoved));
        Assert.All(rows[4..], r => Assert.True(r.IsMoved));
    }

    [Fact]
    public void Two_blocks_swapping_places_are_both_moves()
    {
        // The case per-side marks exist for, and the one a whole-row rule cannot see at all. Two
        // methods of the same shape trading places produce no one-sided rows: the aligner pairs the
        // first line of one against the first line of the other and calls every row modified.
        var rows = Detect(
            (ChangeKind.Modified, "void Helper() { => void Run() {"),
            (ChangeKind.Modified, "    help(); =>     run();"),
            (ChangeKind.Unchanged, "}"),
            (ChangeKind.Modified, "void Run() { => void Helper() {"),
            (ChangeKind.Modified, "    run(); =>     help();"));

        Assert.True(rows[0].IsMoved);
        Assert.True(rows[3].IsMoved);

        // Helper left the top and arrived at the bottom; Run did the opposite. Two blocks, two ids,
        // and each row carries one of each.
        Assert.Equal(rows[0].LeftMoveId, rows[3].RightMoveId);
        Assert.Equal(rows[3].LeftMoveId, rows[0].RightMoveId);
        Assert.NotEqual(rows[0].LeftMoveId, rows[0].RightMoveId);
    }

    [Fact]
    public void A_side_that_did_not_move_is_not_marked()
    {
        // One method moved down past a line that was genuinely rewritten. The rewritten row's own
        // text went nowhere, and saying it moved would tell the reader to skip a real edit.
        var rows = Detect(
            (ChangeKind.Deleted, "void Helper() {"),
            (ChangeKind.Deleted, "    help();"),
            (ChangeKind.Deleted, "}"),
            (ChangeKind.Modified, "var timeout = 30; => var timeout = 60;"),
            (ChangeKind.Inserted, "void Helper() {"),
            (ChangeKind.Inserted, "    help();"),
            (ChangeKind.Inserted, "}"));

        Assert.True(rows[0].IsMovedOn(DiffSide.Left));
        Assert.True(rows[4].IsMovedOn(DiffSide.Right));

        Assert.False(rows[3].IsMoved);
        Assert.False(rows[3].IsMovedOn(DiffSide.Left));
        Assert.False(rows[3].IsMovedOn(DiffSide.Right));
    }

    [Fact]
    public void The_filler_half_of_a_one_sided_move_is_not_marked()
    {
        // A deleted row has nothing on the right; claiming the block moved there too would draw it in
        // two places at once.
        var rows = Detect(
            (ChangeKind.Deleted, "moved();"),
            (ChangeKind.Unchanged, "keep();"),
            (ChangeKind.Inserted, "moved();"));

        Assert.True(rows[0].IsMovedOn(DiffSide.Left));
        Assert.False(rows[0].IsMovedOn(DiffSide.Right));

        Assert.True(rows[2].IsMovedOn(DiffSide.Right));
        Assert.False(rows[2].IsMovedOn(DiffSide.Left));
    }

    [Fact]
    public void An_ordinary_edit_is_left_completely_alone()
    {
        var (rows, left, right) = Align(
            (ChangeKind.Unchanged, "a();"),
            (ChangeKind.Inserted, "b();"),
            (ChangeKind.Unchanged, "c();"));

        // Same instance back, not a copy: nothing to mark means nothing to allocate, and the common
        // case is a diff with no moves in it at all.
        Assert.Same(rows, MoveDetector.Detect(rows, left, right));
    }

    [Fact]
    public void A_file_with_only_deletions_has_nothing_to_pair_with()
    {
        var (rows, left, right) = Align(
            (ChangeKind.Deleted, "gone();"),
            (ChangeKind.Unchanged, "kept();"));

        Assert.Same(rows, MoveDetector.Detect(rows, left, right));
    }

    [Fact]
    public void Moves_are_counted_once_each_not_once_per_row_or_per_side()
    {
        var rows = Detect(
            (ChangeKind.Deleted, "void M() {"),
            (ChangeKind.Deleted, "    m();"),
            (ChangeKind.Deleted, "}"),
            (ChangeKind.Unchanged, "keep();"),
            (ChangeKind.Inserted, "void M() {"),
            (ChangeKind.Inserted, "    m();"),
            (ChangeKind.Inserted, "}"));

        var result = DiffResult.Create(rows);

        Assert.Equal(1, result.Moved);

        // And the ordinary counts still describe the file as it is on disk.
        Assert.Equal(3, result.Deleted);
        Assert.Equal(3, result.Inserted);
    }

    [Fact]
    public void A_row_downgraded_to_unchanged_stops_reporting_as_moved()
    {
        // Later passes turn a comment-only or formatting-only change into an ignored, unchanged row.
        // Once it is no longer reported as a difference it must not be reported as a move either -
        // otherwise a rule the user added to quieten the diff would leave blue bands behind.
        var moved = new DiffLine(1, "// note", null, null, ChangeKind.Deleted) { LeftMoveId = 0 };
        var downgraded = moved with { Kind = ChangeKind.Unchanged, IsIgnored = true };

        Assert.True(moved.IsMoved);
        Assert.False(downgraded.IsMoved);
        Assert.Equal(0, DiffResult.Create([downgraded]).Moved);
    }

    [Fact]
    public void Keys_decide_the_pairing_not_the_displayed_text()
    {
        // The same rule as everywhere else in the pipeline: with "ignore case" on, two lines the user
        // can see differ were matched as equal, and this pass has to agree with the diff that already
        // made that call.
        var rows = new List<DiffLine>
        {
            new(1, "Helper();", null, null, ChangeKind.Deleted),
            new(2, "keep();", 1, "keep();", ChangeKind.Unchanged),
            new(null, null, 2, "HELPER();", ChangeKind.Inserted),
        };

        var detected = MoveDetector.Detect(rows, ["helper();", "keep();"], ["keep();", "helper();"]);

        Assert.True(detected[0].IsMoved);
        Assert.True(detected[2].IsMoved);
    }
}
