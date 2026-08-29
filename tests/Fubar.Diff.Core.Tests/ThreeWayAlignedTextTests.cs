using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Flattening a merge into the three documents the editors show. The point of these is that the
/// existing two-way renderers get an <see cref="AlignedDocument"/> they already understand, so what
/// needs checking is the TRANSLATION - which column is tinted how, and that fillers keep the three
/// panes in step.
/// </summary>
public class ThreeWayAlignedTextTests
{
    private static ThreeWayResult Result(params ThreeWayLine[] lines) => ThreeWayResult.Create(lines);

    /// <summary>base "b" against left "L" and right "b" - only the left side moved.</summary>
    private static ThreeWayResult LeftOnly() => Result(
        new ThreeWayLine(1, "a", 1, "a", 1, "a", MergeKind.Unchanged, -1),
        new ThreeWayLine(2, "b", 2, "L", 2, "b", MergeKind.LeftOnly, 0),
        new ThreeWayLine(3, "c", 3, "c", 3, "c", MergeKind.Unchanged, -1));

    private static ThreeWayResult Conflicting() => Result(
        new ThreeWayLine(1, "a", 1, "a", 1, "a", MergeKind.Unchanged, -1),
        new ThreeWayLine(2, "b", 2, "L", 2, "R", MergeKind.Conflict, 0));

    [Fact]
    public void Each_column_shows_its_own_document()
    {
        var result = LeftOnly();

        Assert.Equal("a\nb\nc", ThreeWayAlignedText.Build(result, MergeSide.Base).Text);
        Assert.Equal("a\nL\nc", ThreeWayAlignedText.Build(result, MergeSide.Left).Text);
        Assert.Equal("a\nb\nc", ThreeWayAlignedText.Build(result, MergeSide.Right).Text);
    }

    [Fact]
    public void Every_column_has_the_same_number_of_rows()
    {
        // What lets three panes scroll as one offset copy, and what makes a region read as a single
        // horizontal band across the window.
        var result = Result(
            new ThreeWayLine(1, "a", 1, "a", 1, "a", MergeKind.Unchanged, -1),
            new ThreeWayLine(null, null, 2, "extra", null, null, MergeKind.LeftOnly, 0));

        Assert.Equal(2, ThreeWayAlignedText.Build(result, MergeSide.Base).Lines.Count);
        Assert.Equal(2, ThreeWayAlignedText.Build(result, MergeSide.Left).Lines.Count);
        Assert.Equal(2, ThreeWayAlignedText.Build(result, MergeSide.Right).Lines.Count);
    }

    [Fact]
    public void A_side_with_no_line_here_is_a_filler()
    {
        var result = Result(new ThreeWayLine(null, null, 1, "extra", null, null, MergeKind.LeftOnly, 0));

        Assert.Equal(ChangeKind.Filler, ThreeWayAlignedText.Build(result, MergeSide.Base).Lines[0].Kind);
        Assert.Equal(ChangeKind.Filler, ThreeWayAlignedText.Build(result, MergeSide.Right).Lines[0].Kind);
        Assert.Equal(ChangeKind.Inserted, ThreeWayAlignedText.Build(result, MergeSide.Left).Lines[0].Kind);
    }

    [Fact]
    public void The_side_that_did_not_move_is_left_untinted()
    {
        // The single question a merge asks is "who moved?". Tinting all three columns of every region
        // hands that question straight back to the reader.
        var result = LeftOnly();

        Assert.Equal(ChangeKind.Inserted, ThreeWayAlignedText.Build(result, MergeSide.Left).Lines[1].Kind);
        Assert.Equal(ChangeKind.Unchanged, ThreeWayAlignedText.Build(result, MergeSide.Right).Lines[1].Kind);
        Assert.Equal(ChangeKind.Deleted, ThreeWayAlignedText.Build(result, MergeSide.Base).Lines[1].Kind);
    }

    [Fact]
    public void Both_edits_are_tinted_in_a_conflict()
    {
        var result = Conflicting();

        Assert.Equal(ChangeKind.Inserted, ThreeWayAlignedText.Build(result, MergeSide.Left).Lines[1].Kind);
        Assert.Equal(ChangeKind.Inserted, ThreeWayAlignedText.Build(result, MergeSide.Right).Lines[1].Kind);
    }

    [Fact]
    public void A_conflicting_row_is_marked_in_every_column()
    {
        // The flag rides alongside the kind rather than replacing it - see AlignedLine.IsConflict.
        var result = Conflicting();

        Assert.True(ThreeWayAlignedText.Build(result, MergeSide.Base).Lines[1].IsConflict);
        Assert.True(ThreeWayAlignedText.Build(result, MergeSide.Left).Lines[1].IsConflict);
        Assert.True(ThreeWayAlignedText.Build(result, MergeSide.Right).Lines[1].IsConflict);
    }

    [Fact]
    public void An_auto_merged_row_is_not_marked_as_a_conflict()
    {
        Assert.False(ThreeWayAlignedText.Build(LeftOnly(), MergeSide.Left).Lines[1].IsConflict);
    }

    [Fact]
    public void Line_numbers_come_from_the_original_file_not_the_view()
    {
        // Fillers would otherwise shift every number after the first insertion, and the gutter would
        // stop matching the file on disk.
        var result = Result(
            new ThreeWayLine(1, "a", 1, "a", 1, "a", MergeKind.Unchanged, -1),
            new ThreeWayLine(null, null, 2, "extra", null, null, MergeKind.LeftOnly, 0),
            new ThreeWayLine(2, "b", 3, "b", 2, "b", MergeKind.Unchanged, -1));

        var ancestor = ThreeWayAlignedText.Build(result, MergeSide.Base);

        Assert.Equal(1, ancestor.Lines[0].SourceNumber);
        Assert.Null(ancestor.Lines[1].SourceNumber);
        Assert.Equal(2, ancestor.Lines[2].SourceNumber);
    }

    [Fact]
    public void An_edit_with_character_spans_loses_its_full_line_tint()
    {
        // The same bargain the two-way view makes for a modified line: Modified draws no line
        // background (see DiffLineColors.LineBackground), leaving the spans as the whole signal, which
        // is more precise than washing the row they sit on.
        var result = Result(
            new ThreeWayLine(1, "value = 1", 1, "value = 2", 1, "value = 1", MergeKind.LeftOnly, 0)
            {
                LeftSpans = [new CharSpan(8, 1, ChangeKind.Inserted)],
            });

        var left = ThreeWayAlignedText.Build(result, MergeSide.Left);

        Assert.Equal(ChangeKind.Modified, left.Lines[0].Kind);
        Assert.Equal(8, Assert.Single(left.Lines[0].Spans).Start);
    }

    [Fact]
    public void An_edit_with_no_ancestor_line_keeps_its_full_line_tint()
    {
        // Nothing to compare against means no spans to defer to, so the whole row has to say it.
        var result = Result(
            new ThreeWayLine(null, null, 1, "brand new", null, null, MergeKind.LeftOnly, 0));

        var left = ThreeWayAlignedText.Build(result, MergeSide.Left);

        Assert.Equal(ChangeKind.Inserted, left.Lines[0].Kind);
        Assert.Empty(left.Lines[0].Spans);
    }

    [Fact]
    public void The_ancestor_column_carries_no_spans_of_its_own()
    {
        // It is already tinted whole as the text being replaced; spanning it too would ask the reader
        // to compare three sets of highlights to answer one question.
        var result = Result(
            new ThreeWayLine(1, "value = 1", 1, "value = 2", 1, "value = 3", MergeKind.Conflict, 0)
            {
                LeftSpans = [new CharSpan(8, 1, ChangeKind.Inserted)],
                RightSpans = [new CharSpan(8, 1, ChangeKind.Inserted)],
            });

        var ancestor = ThreeWayAlignedText.Build(result, MergeSide.Base);

        Assert.Empty(ancestor.Lines[0].Spans);
        Assert.Equal(ChangeKind.Deleted, ancestor.Lines[0].Kind);
    }

    [Fact]
    public void Both_edits_carry_their_own_spans_in_a_conflict()
    {
        var result = Result(
            new ThreeWayLine(1, "a", 1, "b", 1, "c", MergeKind.Conflict, 0)
            {
                LeftSpans = [new CharSpan(0, 1, ChangeKind.Inserted)],
                RightSpans = [new CharSpan(0, 1, ChangeKind.Inserted)],
            });

        Assert.Single(ThreeWayAlignedText.Build(result, MergeSide.Left).Lines[0].Spans);
        Assert.Single(ThreeWayAlignedText.Build(result, MergeSide.Right).Lines[0].Spans);
    }

    // ---- The close-up ---------------------------------------------------------------------------

    [Fact]
    public void The_close_up_drops_fillers_rather_than_padding_with_blanks()
    {
        // Stacking has no row-count parity to preserve, so a filler here would only insert a blank
        // line that exists in none of the three files.
        var result = Result(
            new ThreeWayLine(1, "base", 1, "one", null, null, MergeKind.Conflict, 0),
            new ThreeWayLine(null, null, 2, "two", null, null, MergeKind.Conflict, 0));

        var left = ThreeWayAlignedText.BuildCompact(result, MergeSide.Left, 0, 2);
        var right = ThreeWayAlignedText.BuildCompact(result, MergeSide.Right, 0, 2);

        Assert.Equal("one\ntwo", left.Text);
        Assert.Equal(2, left.Lines.Count);

        // The right side contributes nothing at all here, and says so by being empty rather than by
        // being two blank lines.
        Assert.Equal(string.Empty, right.Text);
        Assert.Empty(right.Lines);
    }

    [Fact]
    public void The_close_up_covers_only_the_requested_range()
    {
        var result = Result(
            new ThreeWayLine(1, "before", 1, "before", 1, "before", MergeKind.Unchanged, -1),
            new ThreeWayLine(2, "b", 2, "L", 2, "R", MergeKind.Conflict, 0),
            new ThreeWayLine(3, "after", 3, "after", 3, "after", MergeKind.Unchanged, -1));

        var excerpt = ThreeWayAlignedText.BuildCompact(result, MergeSide.Base, 1, 1);

        Assert.Equal("b", excerpt.Text);
    }

    [Fact]
    public void The_close_up_keeps_the_line_numbers_and_spans_of_the_full_view()
    {
        var result = Result(
            new ThreeWayLine(7, "value = 1", 9, "value = 2", 7, "value = 1", MergeKind.LeftOnly, 0)
            {
                LeftSpans = [new CharSpan(8, 1, ChangeKind.Inserted)],
            });

        var excerpt = ThreeWayAlignedText.BuildCompact(result, MergeSide.Left, 0, 1);

        Assert.Equal(9, excerpt.Lines[0].SourceNumber);
        Assert.Equal(8, Assert.Single(excerpt.Lines[0].Spans).Start);
    }

    [Fact]
    public void A_close_up_range_beyond_the_document_is_clamped_rather_than_throwing()
    {
        // A region can outlive the result it was computed from for a frame while a new merge is
        // applied, so this must degrade rather than take the window down inside a render pass.
        var result = Result(new ThreeWayLine(1, "a", 1, "a", 1, "a", MergeKind.Unchanged, -1));

        Assert.Equal(string.Empty, ThreeWayAlignedText.BuildCompact(result, MergeSide.Base, 5, 3).Text);
        Assert.Equal("a", ThreeWayAlignedText.BuildCompact(result, MergeSide.Base, 0, 99).Text);
        Assert.Equal(string.Empty, ThreeWayAlignedText.BuildCompact(result, MergeSide.Base, -4, 0).Text);
    }

    [Fact]
    public void An_empty_result_produces_an_empty_document()
    {
        var document = ThreeWayAlignedText.Build(ThreeWayResult.Empty, MergeSide.Base);

        Assert.Equal(string.Empty, document.Text);
        Assert.Empty(document.Lines);
    }
}
