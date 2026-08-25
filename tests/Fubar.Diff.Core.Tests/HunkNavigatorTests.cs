using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Wrap-around and boundary behaviour for change navigation. These are the cases that are wrong in
/// most diff viewers, and they are pure functions, so they are cheap to pin down exactly.
/// </summary>
public class HunkNavigatorTests
{
    private static readonly DiffHunk[] ThreeHunks =
    [
        new(2, 4),
        new(10, 10),
        new(20, 25),
    ];

    [Theory]
    [InlineData(-1, 0)]  // nothing selected -> first
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]   // past the last -> wraps to first
    public void Next_advances_and_wraps(int current, int expected) =>
        Assert.Equal(expected, HunkNavigator.Next(ThreeHunks, current));

    [Theory]
    [InlineData(-1, 2)]  // nothing selected -> LAST, which is what "previous" should mean first
    [InlineData(0, 2)]   // before the first -> wraps to last
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void Previous_retreats_and_wraps(int current, int expected) =>
        Assert.Equal(expected, HunkNavigator.Previous(ThreeHunks, current));

    [Fact]
    public void Navigation_reports_no_target_when_there_are_no_hunks()
    {
        Assert.Null(HunkNavigator.Next([], -1));
        Assert.Null(HunkNavigator.Previous([], -1));
    }

    [Fact]
    public void Single_hunk_stays_put_in_both_directions()
    {
        DiffHunk[] one = [new(3, 3)];

        Assert.Equal(0, HunkNavigator.Next(one, 0));
        Assert.Equal(0, HunkNavigator.Previous(one, 0));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(4, 0)]
    [InlineData(10, 1)]
    [InlineData(25, 2)]
    public void IndexOfHunkContaining_finds_the_enclosing_hunk(int line, int expected) =>
        Assert.Equal(expected, HunkNavigator.IndexOfHunkContaining(ThreeHunks, line));

    [Theory]
    [InlineData(0)]   // before the first hunk
    [InlineData(5)]   // between hunks
    [InlineData(99)]  // past the last
    public void IndexOfHunkContaining_returns_minus_one_for_context_lines(int line) =>
        Assert.Equal(-1, HunkNavigator.IndexOfHunkContaining(ThreeHunks, line));
}

/// <summary>
/// <see cref="HunkNavigator.RangeOf"/> captions a hunk with the lines it covers in the two ORIGINAL
/// files. The trap it guards is reporting aligned-view row indices as line numbers: those count
/// filler rows, which exist in neither file.
/// </summary>
public class HunkRangeTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    private static DiffLine Modified(int l, int r) => new(l, "before", r, "after", ChangeKind.Modified);

    private static DiffLine Inserted(int n) => new(null, null, n, "added", ChangeKind.Inserted);

    private static DiffLine Deleted(int n) => new(n, "gone", null, null, ChangeKind.Deleted);

    [Fact]
    public void Reports_the_source_lines_a_modified_hunk_spans()
    {
        var lines = new List<DiffLine> { Unchanged(1), Modified(2, 2), Modified(3, 3), Unchanged(4) };

        var range = HunkNavigator.RangeOf(lines, new DiffHunk(1, 2));

        Assert.Equal(2, range.LeftStart);
        Assert.Equal(3, range.LeftEnd);
        Assert.Equal(2, range.RightStart);
        Assert.Equal(3, range.RightEnd);
    }

    /// <summary>An insertion covers no left-hand lines - naming any would point at nothing.</summary>
    [Fact]
    public void An_inserted_hunk_has_no_left_range()
    {
        var lines = new List<DiffLine> { Unchanged(1), Inserted(2), Inserted(3) };

        var range = HunkNavigator.RangeOf(lines, new DiffHunk(1, 2));

        Assert.Null(range.LeftStart);
        Assert.Null(range.LeftEnd);
        Assert.Equal(2, range.RightStart);
        Assert.Equal(3, range.RightEnd);
    }

    [Fact]
    public void A_deleted_hunk_has_no_right_range()
    {
        var lines = new List<DiffLine> { Unchanged(1), Deleted(2) };

        var range = HunkNavigator.RangeOf(lines, new DiffHunk(1, 1));

        Assert.Equal(2, range.LeftStart);
        Assert.Null(range.RightStart);
    }

    /// <summary>
    /// A replaced block is deletions followed by insertions, so each side's range must come from the
    /// rows that actually carry a number - not from the hunk's extent.
    /// </summary>
    [Fact]
    public void A_replaced_block_reports_each_side_independently()
    {
        var lines = new List<DiffLine> { Deleted(1), Deleted(2), Inserted(1), Inserted(2), Inserted(3) };

        var range = HunkNavigator.RangeOf(lines, new DiffHunk(0, 4));

        Assert.Equal(1, range.LeftStart);
        Assert.Equal(2, range.LeftEnd);
        Assert.Equal(1, range.RightStart);
        Assert.Equal(3, range.RightEnd);
    }

    /// <summary>Must not throw inside a render pass when the hunk outlives its result.</summary>
    [Fact]
    public void An_out_of_range_hunk_clamps()
    {
        var lines = new List<DiffLine> { Unchanged(1) };

        var range = HunkNavigator.RangeOf(lines, new DiffHunk(-3, 99));

        Assert.Equal(1, range.LeftStart);
    }
}
