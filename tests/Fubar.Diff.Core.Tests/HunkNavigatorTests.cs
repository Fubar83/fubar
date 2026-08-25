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
