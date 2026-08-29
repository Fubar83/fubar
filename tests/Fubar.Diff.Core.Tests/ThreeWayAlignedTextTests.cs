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
    public void An_empty_result_produces_an_empty_document()
    {
        var document = ThreeWayAlignedText.Build(ThreeWayResult.Empty, MergeSide.Base);

        Assert.Equal(string.Empty, document.Text);
        Assert.Empty(document.Lines);
    }
}
