using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The range overload backs the detail pane, which shows one hunk close up. Its job is to be a
/// faithful excerpt: same rows, same alignment, same metadata as the full document.
/// </summary>
public class AlignedRangeTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    private static DiffLine Modified(int l, int r) => new(l, "before", r, "after", ChangeKind.Modified);

    private static DiffLine Inserted(int n) => new(null, null, n, "added", ChangeKind.Inserted);

    private static DiffResult Sample() =>
        DiffResult.Create([Unchanged(1), Modified(2, 2), Inserted(3), Unchanged(3)]);

    [Fact]
    public void Excerpt_holds_only_the_requested_rows()
    {
        var excerpt = AlignedText.Build(Sample(), DiffSide.Right, 1, 2);

        Assert.Equal(2, excerpt.Lines.Count);
        Assert.Equal("after\nadded", excerpt.Text);
    }

    /// <summary>Both sides must stay the same height, or the close-up stops lining up.</summary>
    [Fact]
    public void Both_sides_of_an_excerpt_have_the_same_line_count()
    {
        var result = Sample();

        var left = AlignedText.Build(result, DiffSide.Left, 1, 2);
        var right = AlignedText.Build(result, DiffSide.Right, 1, 2);

        Assert.Equal(left.Lines.Count, right.Lines.Count);
        Assert.Equal(left.Text.Split('\n').Length, right.Text.Split('\n').Length);
    }

    /// <summary>A filler inside the range is kept as an empty line, exactly as in the full document.</summary>
    [Fact]
    public void A_filler_inside_the_excerpt_is_kept()
    {
        var left = AlignedText.Build(Sample(), DiffSide.Left, 1, 2);

        Assert.Equal("before\n", left.Text);
        Assert.Equal(ChangeKind.Filler, left.Lines[1].Kind);
        Assert.Null(left.Lines[1].SourceNumber);
    }

    /// <summary>Metadata is re-indexed from zero, not left addressing the full document.</summary>
    [Fact]
    public void Excerpt_metadata_is_indexed_from_the_start_of_the_range()
    {
        var excerpt = AlignedText.Build(Sample(), DiffSide.Right, 1, 2);

        Assert.Equal(2, excerpt.Lines[0].SourceNumber);
        Assert.Equal(ChangeKind.Modified, excerpt.Lines[0].Kind);
    }

    /// <summary>
    /// A hunk can briefly outlive the result it came from while a new comparison is applied, so an
    /// out-of-bounds range must clamp rather than throw inside a render pass.
    /// </summary>
    [Theory]
    [InlineData(-5, 2)]
    [InlineData(3, 99)]
    [InlineData(99, 4)]
    [InlineData(1, -1)]
    public void An_out_of_range_request_clamps(int start, int count)
    {
        var excerpt = AlignedText.Build(Sample(), DiffSide.Left, start, count);

        Assert.True(excerpt.Lines.Count <= 4);
    }
}
