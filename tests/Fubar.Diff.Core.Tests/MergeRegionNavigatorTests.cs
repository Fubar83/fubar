using Fubar.Diff.Core.Merge;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Stepping through a merge. The rule that matters: "next conflict" must not quietly become "next
/// anything" when the conflicts run out, because going nowhere is how the user learns they are done.
/// </summary>
public class MergeRegionNavigatorTests
{
    /// <summary>Regions at arbitrary but ordered positions - only their kinds matter here.</summary>
    private static readonly IReadOnlyList<MergeRegion> Mixed =
    [
        new(0, 1, MergeKind.LeftOnly),
        new(4, 4, MergeKind.Conflict),
        new(7, 9, MergeKind.RightOnly),
        new(12, 12, MergeKind.Conflict),
    ];

    private static readonly IReadOnlyList<MergeRegion> NoConflicts =
    [
        new(0, 1, MergeKind.LeftOnly),
        new(4, 4, MergeKind.RightOnly),
    ];

    [Fact]
    public void Next_steps_through_every_region_by_default()
    {
        Assert.Equal(0, MergeRegionNavigator.Next(Mixed, -1));
        Assert.Equal(1, MergeRegionNavigator.Next(Mixed, 0));
        Assert.Equal(3, MergeRegionNavigator.Next(Mixed, 2));
    }

    [Fact]
    public void Next_wraps_past_the_end()
    {
        Assert.Equal(0, MergeRegionNavigator.Next(Mixed, 3));
    }

    [Fact]
    public void Previous_from_nowhere_lands_on_the_last()
    {
        Assert.Equal(3, MergeRegionNavigator.Previous(Mixed, -1));
    }

    [Fact]
    public void Previous_wraps_past_the_start()
    {
        Assert.Equal(3, MergeRegionNavigator.Previous(Mixed, 0));
    }

    [Fact]
    public void Conflicts_only_skips_the_regions_that_answered_themselves()
    {
        // A real merge is mostly one-sided regions. Walking all of them to reach the few that are
        // contested is how a three-way tool ends up slower than doing it by hand.
        Assert.Equal(1, MergeRegionNavigator.Next(Mixed, -1, conflictsOnly: true));
        Assert.Equal(3, MergeRegionNavigator.Next(Mixed, 1, conflictsOnly: true));
        Assert.Equal(1, MergeRegionNavigator.Next(Mixed, 3, conflictsOnly: true));
    }

    [Fact]
    public void Conflicts_only_goes_backwards_too()
    {
        Assert.Equal(3, MergeRegionNavigator.Previous(Mixed, -1, conflictsOnly: true));
        Assert.Equal(1, MergeRegionNavigator.Previous(Mixed, 3, conflictsOnly: true));
    }

    [Fact]
    public void With_no_conflicts_there_is_nowhere_to_go()
    {
        Assert.Null(MergeRegionNavigator.Next(NoConflicts, -1, conflictsOnly: true));
        Assert.Null(MergeRegionNavigator.Previous(NoConflicts, -1, conflictsOnly: true));
    }

    [Fact]
    public void With_no_regions_at_all_there_is_nowhere_to_go()
    {
        Assert.Null(MergeRegionNavigator.Next([], -1));
        Assert.Null(MergeRegionNavigator.Previous([], -1));
    }

    [Fact]
    public void A_row_maps_back_to_the_region_containing_it()
    {
        Assert.Equal(0, MergeRegionNavigator.IndexOfRegionContaining(Mixed, 1));
        Assert.Equal(2, MergeRegionNavigator.IndexOfRegionContaining(Mixed, 8));
        Assert.Equal(-1, MergeRegionNavigator.IndexOfRegionContaining(Mixed, 3));
    }

    [Fact]
    public void A_regions_range_names_the_lines_of_each_real_file()
    {
        // Row indices address the aligned view, which contains fillers that exist in none of the three
        // files - reporting those as line numbers would name lines the user cannot go and look at.
        IReadOnlyList<ThreeWayLine> lines =
        [
            new(1, "a", 1, "a", 1, "a", MergeKind.Unchanged, -1),
            new(2, "b", 2, "B", null, null, MergeKind.Conflict, 0),
            new(null, null, 3, "extra", null, null, MergeKind.Conflict, 0),
        ];

        var range = MergeRegionNavigator.RangeOf(lines, new MergeRegion(1, 2, MergeKind.Conflict));

        Assert.Equal(2, range.BaseStart);
        Assert.Equal(2, range.BaseEnd);
        Assert.Equal(2, range.LeftStart);
        Assert.Equal(3, range.LeftEnd);
        Assert.Null(range.RightStart);
    }
}

/// <summary>The decision store. Mirrors <c>MergeStateTests</c>, one side wider.</summary>
public class ThreeWayMergeStateTests
{
    [Fact]
    public void An_undecided_region_reports_unresolved() =>
        Assert.Equal(MergeChoice.Unresolved, ThreeWayMergeState.Empty.For(0));

    [Fact]
    public void A_decision_is_remembered()
    {
        var state = ThreeWayMergeState.Empty.With(2, MergeChoice.TakeLeft);

        Assert.Equal(MergeChoice.TakeLeft, state.For(2));
        Assert.Equal(MergeChoice.Unresolved, state.For(1));
        Assert.True(state.HasResolutions);
    }

    [Fact]
    public void The_original_state_is_untouched()
    {
        var original = ThreeWayMergeState.Empty;
        original.With(0, MergeChoice.TakeRight);

        Assert.False(original.HasResolutions);
    }

    [Fact]
    public void Setting_unresolved_clears_the_decision_rather_than_storing_it()
    {
        // Otherwise the counts would claim something had been answered when it had been un-answered.
        var state = ThreeWayMergeState.Empty
            .With(0, MergeChoice.TakeLeft)
            .With(0, MergeChoice.Unresolved);

        Assert.False(state.HasResolutions);
        Assert.Equal(0, state.ResolvedCount);
    }

    [Fact]
    public void Decisions_for_regions_that_no_longer_exist_are_dropped()
    {
        // Changing an option re-runs the merge and can produce fewer regions; a stale index would
        // otherwise silently resolve the wrong one.
        var state = ThreeWayMergeState.Empty
            .With(0, MergeChoice.TakeLeft)
            .With(5, MergeChoice.TakeRight)
            .RemapTo(3);

        Assert.Equal(MergeChoice.TakeLeft, state.For(0));
        Assert.Equal(MergeChoice.Unresolved, state.For(5));
        Assert.Equal(1, state.ResolvedCount);
    }

    [Fact]
    public void Remapping_a_state_that_is_still_valid_returns_the_same_instance()
    {
        var state = ThreeWayMergeState.Empty.With(0, MergeChoice.TakeLeft);

        Assert.Same(state, state.RemapTo(3));
    }
}
