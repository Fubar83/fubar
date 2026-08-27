using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The compact excerpt backs the STACKED close-up (old block, then new block) rather than the
/// side-by-side one. Its job is the opposite of <see cref="AlignedText.Build(DiffResult,DiffSide,int,int)"/>:
/// drop filler entirely, since a stacked layout has no row-count-parity requirement to preserve.
/// </summary>
public class AlignedTextCompactTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    private static DiffLine Modified(int l, int r) => new(l, "before", r, "after", ChangeKind.Modified);

    private static DiffLine Inserted(int n) => new(null, null, n, "added", ChangeKind.Inserted);

    private static DiffLine Deleted(int n) => new(n, "gone", null, null, ChangeKind.Deleted);

    private static DiffResult Sample() =>
        DiffResult.Create([Unchanged(1), Deleted(2), Inserted(2), Modified(3, 3)]);

    [Fact]
    public void A_modified_row_appears_on_both_sides()
    {
        var result = DiffResult.Create([Modified(1, 1)]);

        Assert.Equal("before", AlignedText.BuildCompact(result, DiffSide.Left, 0, 1).Text);
        Assert.Equal("after", AlignedText.BuildCompact(result, DiffSide.Right, 0, 1).Text);
    }

    /// <summary>The whole point: no blank line stands in for the side that has nothing here.</summary>
    [Fact]
    public void A_deleted_row_does_not_appear_on_the_right_at_all()
    {
        var result = DiffResult.Create([Deleted(1)]);

        var right = AlignedText.BuildCompact(result, DiffSide.Right, 0, 1);

        Assert.Equal(string.Empty, right.Text);
        Assert.Empty(right.Lines);
    }

    [Fact]
    public void An_inserted_row_does_not_appear_on_the_left_at_all()
    {
        var result = DiffResult.Create([Inserted(1)]);

        var left = AlignedText.BuildCompact(result, DiffSide.Left, 0, 1);

        Assert.Equal(string.Empty, left.Text);
        Assert.Empty(left.Lines);
    }

    /// <summary>
    /// A mixed range (delete + insert + modify) compacts each side to ONLY its real lines, with no
    /// blank gap where the other side's row would have been - two lines on the left, two on the right,
    /// not four-with-holes on either side.
    /// </summary>
    [Fact]
    public void A_mixed_range_compacts_each_side_independently()
    {
        var result = Sample(); // Unchanged, Deleted, Inserted, Modified

        var left = AlignedText.BuildCompact(result, DiffSide.Left, 0, 4);
        var right = AlignedText.BuildCompact(result, DiffSide.Right, 0, 4);

        Assert.Equal("same\ngone\nbefore", left.Text);
        Assert.Equal("same\nadded\nafter", right.Text);
        Assert.Equal(3, left.Lines.Count);
        Assert.Equal(3, right.Lines.Count);
    }

    /// <summary>Kept rows carry their OWN kind directly - no Filler remapping, since there is nothing left to remap.</summary>
    [Fact]
    public void Kept_rows_use_their_own_kind_unchanged()
    {
        var left = AlignedText.BuildCompact(Sample(), DiffSide.Left, 0, 4);

        Assert.Equal(ChangeKind.Unchanged, left.Lines[0].Kind);
        Assert.Equal(ChangeKind.Deleted, left.Lines[1].Kind);
        Assert.Equal(ChangeKind.Modified, left.Lines[2].Kind);
    }

    /// <summary>Source line numbers are preserved for the gutter, exactly as the side-by-side excerpt does.</summary>
    [Fact]
    public void Source_numbers_are_preserved()
    {
        var left = AlignedText.BuildCompact(Sample(), DiffSide.Left, 0, 4);

        Assert.Equal(1, left.Lines[0].SourceNumber);
        Assert.Equal(2, left.Lines[1].SourceNumber);
        Assert.Equal(3, left.Lines[2].SourceNumber);
    }

    [Theory]
    [InlineData(-5, 2)]
    [InlineData(3, 99)]
    [InlineData(99, 4)]
    [InlineData(1, -1)]
    public void An_out_of_range_request_clamps(int start, int count)
    {
        var excerpt = AlignedText.BuildCompact(Sample(), DiffSide.Left, start, count);

        Assert.True(excerpt.Lines.Count <= 4);
    }
}
