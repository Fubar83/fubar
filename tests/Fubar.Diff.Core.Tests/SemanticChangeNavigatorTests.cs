using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The Hybrid view's navigation: same wrap-around contract as <see cref="HunkNavigatorTests"/>, but
/// walking a flat change list instead of hunks, and skipping ignored entries - they are not something
/// the user asked to see, so Prev/Next must step past them exactly as hunk navigation does.
/// </summary>
public class SemanticChangeNavigatorTests
{
    private static JsonChange Change(string path, bool ignored = false) =>
        new(JsonPath.Root.Property(path), ChangeKind.Modified, null, null) { IsIgnored = ignored };

    private static readonly JsonChange[] ThreeChanges =
    [
        Change("a"),
        Change("b"),
        Change("c"),
    ];

    [Theory]
    [InlineData(-1, 0)] // nothing selected -> first
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]  // past the last -> wraps to first
    public void Next_advances_and_wraps(int current, int expected) =>
        Assert.Equal(expected, SemanticChangeNavigator.Next(ThreeChanges, current));

    [Theory]
    [InlineData(-1, 2)] // nothing selected -> LAST, matching HunkNavigator.Previous
    [InlineData(0, 2)]  // before the first -> wraps to last
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void Previous_retreats_and_wraps(int current, int expected) =>
        Assert.Equal(expected, SemanticChangeNavigator.Previous(ThreeChanges, current));

    [Fact]
    public void No_changes_means_nothing_to_navigate_to()
    {
        Assert.Null(SemanticChangeNavigator.Next([], -1));
        Assert.Null(SemanticChangeNavigator.Previous([], -1));
    }

    [Fact]
    public void Every_change_ignored_means_nothing_to_navigate_to()
    {
        JsonChange[] allIgnored = [Change("a", ignored: true), Change("b", ignored: true)];

        Assert.Null(SemanticChangeNavigator.Next(allIgnored, -1));
        Assert.Null(SemanticChangeNavigator.Previous(allIgnored, -1));
    }

    [Fact]
    public void Next_steps_straight_over_an_ignored_change()
    {
        JsonChange[] changes = [Change("a"), Change("b", ignored: true), Change("c")];

        Assert.Equal(2, SemanticChangeNavigator.Next(changes, 0));
    }

    [Fact]
    public void Previous_steps_straight_over_an_ignored_change()
    {
        JsonChange[] changes = [Change("a"), Change("b", ignored: true), Change("c")];

        Assert.Equal(0, SemanticChangeNavigator.Previous(changes, 2));
    }

    /// <summary>
    /// From "nothing selected", Next must land on the first NAVIGABLE change, not index 0 regardless -
    /// if index 0 happens to be ignored, jumping there would silently show a difference the user
    /// asked not to see.
    /// </summary>
    [Fact]
    public void Next_from_nothing_selected_skips_a_leading_ignored_change()
    {
        JsonChange[] changes = [Change("a", ignored: true), Change("b")];

        Assert.Equal(1, SemanticChangeNavigator.Next(changes, -1));
    }

    [Fact]
    public void Previous_from_nothing_selected_skips_a_trailing_ignored_change()
    {
        JsonChange[] changes = [Change("a"), Change("b", ignored: true)];

        Assert.Equal(0, SemanticChangeNavigator.Previous(changes, -1));
    }
}
