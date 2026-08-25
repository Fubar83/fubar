using Fubar.Diff.Core.Merge;

namespace Fubar.Diff.Core.Tests;

public class MergeStateTests
{
    [Fact]
    public void Nothing_is_resolved_to_begin_with()
    {
        Assert.False(MergeState.Empty.HasResolutions);
        Assert.Equal(HunkResolution.Unresolved, MergeState.Empty.For(0));
    }

    [Fact]
    public void With_records_a_decision_without_mutating_the_original()
    {
        var original = MergeState.Empty;
        var updated = original.With(1, HunkResolution.TakeLeft);

        Assert.Equal(HunkResolution.TakeLeft, updated.For(1));
        Assert.Equal(HunkResolution.Unresolved, original.For(1));
    }

    [Fact]
    public void A_later_decision_replaces_an_earlier_one()
    {
        var state = MergeState.Empty
            .With(0, HunkResolution.TakeLeft)
            .With(0, HunkResolution.TakeRight);

        Assert.Equal(HunkResolution.TakeRight, state.For(0));
        Assert.Equal(1, state.ResolvedCount);
    }

    [Fact]
    public void Resolving_back_to_unresolved_clears_the_decision()
    {
        // Storing Unresolved instead of removing it would leave HasResolutions true, so the Save
        // button would stay enabled with nothing actually to save.
        var state = MergeState.Empty
            .With(0, HunkResolution.TakeLeft)
            .With(0, HunkResolution.Unresolved);

        Assert.False(state.HasResolutions);
        Assert.Equal(0, state.ResolvedCount);
    }

    [Fact]
    public void Clear_drops_everything()
    {
        var state = MergeState.Empty
            .With(0, HunkResolution.TakeLeft)
            .With(3, HunkResolution.TakeRight);

        Assert.False(state.Clear().HasResolutions);
    }

    [Fact]
    public void RemapTo_drops_decisions_for_hunks_that_no_longer_exist()
    {
        // Toggling a comparison option re-runs the diff and can produce fewer hunks. A stale index
        // would otherwise resolve whichever hunk now sits at that position - silently the wrong one.
        var state = MergeState.Empty
            .With(0, HunkResolution.TakeLeft)
            .With(5, HunkResolution.TakeRight);

        var remapped = state.RemapTo(hunkCount: 2);

        Assert.Equal(HunkResolution.TakeLeft, remapped.For(0));
        Assert.Equal(HunkResolution.Unresolved, remapped.For(5));
        Assert.Equal(1, remapped.ResolvedCount);
    }

    [Fact]
    public void RemapTo_keeps_everything_when_all_indices_still_fit()
    {
        var state = MergeState.Empty.With(0, HunkResolution.TakeLeft).With(1, HunkResolution.TakeRight);

        Assert.Equal(2, state.RemapTo(hunkCount: 5).ResolvedCount);
    }

    [Fact]
    public void RemapTo_zero_clears_everything()
    {
        var state = MergeState.Empty.With(0, HunkResolution.TakeLeft);

        Assert.False(state.RemapTo(hunkCount: 0).HasResolutions);
    }
}
