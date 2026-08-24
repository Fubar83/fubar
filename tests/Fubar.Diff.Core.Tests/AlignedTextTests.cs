using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The alignment invariant the whole two-editor view rests on: both sides flatten to the SAME number
/// of lines, and display line i corresponds to <c>DiffResult.Lines[i]</c>. If either ever stops
/// holding, the panes drift apart and every renderer paints the wrong row.
/// </summary>
public class AlignedTextTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    private static DiffLine Modified(int l, int r) => new(l, "before", r, "after", ChangeKind.Modified);

    private static DiffLine Inserted(int n) => new(null, null, n, "added", ChangeKind.Inserted);

    private static DiffLine Deleted(int n) => new(n, "gone", null, null, ChangeKind.Deleted);

    [Fact]
    public void Both_sides_flatten_to_the_same_line_count()
    {
        var result = DiffResult.Create([Unchanged(1), Deleted(2), Inserted(2), Modified(3, 3)]);

        var left = AlignedText.Build(result, DiffSide.Left);
        var right = AlignedText.Build(result, DiffSide.Right);

        Assert.Equal(result.Lines.Count, left.Lines.Count);
        Assert.Equal(left.Lines.Count, right.Lines.Count);

        // Same count in the TEXT too, not just the metadata - this is what scroll sync relies on.
        Assert.Equal(
            left.Text.Split('\n').Length,
            right.Text.Split('\n').Length);
    }

    [Fact]
    public void A_filler_becomes_an_empty_line_rather_than_being_skipped()
    {
        var result = DiffResult.Create([Unchanged(1), Inserted(2)]);

        var left = AlignedText.Build(result, DiffSide.Left);

        // Skipping it would shorten the left document and misalign everything below.
        Assert.Equal(2, left.Text.Split('\n').Length);
        Assert.Equal(string.Empty, left.Text.Split('\n')[1]);
    }

    [Fact]
    public void Filler_lines_carry_no_source_number()
    {
        var result = DiffResult.Create([Unchanged(1), Inserted(2)]);

        var left = AlignedText.Build(result, DiffSide.Left);

        Assert.Equal(1, left.Lines[0].SourceNumber);
        Assert.Null(left.Lines[1].SourceNumber);
    }

    [Fact]
    public void Source_numbers_track_the_file_not_the_display()
    {
        // Left has an insertion above it, so its display line 2 is source line 2 - NOT 3. Numbering
        // the displayed lines instead would put every number after an insertion out of step with the
        // file on disk.
        var result = DiffResult.Create([Inserted(1), Unchanged(2)]);

        var left = AlignedText.Build(result, DiffSide.Left);

        Assert.Null(left.Lines[0].SourceNumber);
        Assert.Equal(2, left.Lines[1].SourceNumber);
    }

    [Fact]
    public void A_deletion_tints_the_left_and_fills_the_right()
    {
        var result = DiffResult.Create([Deleted(1)]);

        Assert.Equal(ChangeKind.Deleted, AlignedText.Build(result, DiffSide.Left).Lines[0].Kind);
        Assert.Equal(ChangeKind.Filler, AlignedText.Build(result, DiffSide.Right).Lines[0].Kind);
    }

    [Fact]
    public void An_insertion_tints_the_right_and_fills_the_left()
    {
        var result = DiffResult.Create([Inserted(1)]);

        Assert.Equal(ChangeKind.Filler, AlignedText.Build(result, DiffSide.Left).Lines[0].Kind);
        Assert.Equal(ChangeKind.Inserted, AlignedText.Build(result, DiffSide.Right).Lines[0].Kind);
    }

    [Fact]
    public void A_modification_tints_both_sides()
    {
        var result = DiffResult.Create([Modified(1, 1)]);

        Assert.Equal(ChangeKind.Modified, AlignedText.Build(result, DiffSide.Left).Lines[0].Kind);
        Assert.Equal(ChangeKind.Modified, AlignedText.Build(result, DiffSide.Right).Lines[0].Kind);
    }

    [Fact]
    public void Each_side_carries_its_own_character_spans()
    {
        var row = Modified(1, 1) with
        {
            LeftSpans = [new CharSpan(0, 6, ChangeKind.Deleted)],
            RightSpans = [new CharSpan(0, 5, ChangeKind.Inserted)],
        };

        Assert.Equal(6, AlignedText.Build(DiffResult.Create([row]), DiffSide.Left).Lines[0].Spans[0].Length);
        Assert.Equal(5, AlignedText.Build(DiffResult.Create([row]), DiffSide.Right).Lines[0].Spans[0].Length);
    }

    [Fact]
    public void An_empty_result_flattens_to_empty_text()
    {
        var aligned = AlignedText.Build(DiffResult.Empty, DiffSide.Left);

        Assert.Equal(string.Empty, aligned.Text);
        Assert.Empty(aligned.Lines);
    }

    [Fact]
    public void Text_is_the_document_content_joined_in_row_order()
    {
        var result = DiffResult.Create([Unchanged(1), Modified(2, 2)]);

        Assert.Equal("same\nbefore", AlignedText.Build(result, DiffSide.Left).Text);
        Assert.Equal("same\nafter", AlignedText.Build(result, DiffSide.Right).Text);
    }
}
