using System.Collections.Generic;
using System.Linq;

namespace Fubar.Diff.Core.Merge;

/// <summary>
/// The user's decisions about each merge region, keyed by region index. Immutable, like its two-way
/// counterpart <see cref="MergeState"/>, and for the same reason: every resolution produces a new
/// state, so a view model can hold one field and re-render from it without worrying who else has a
/// reference.
///
/// Only CONFLICTS normally appear here. A region only one side touched is already decided by the merge
/// itself, and storing a decision for it would be recording an answer nobody was asked - though
/// overriding one is allowed, which is what makes "actually, keep the ancestor's version" possible.
/// </summary>
public sealed class ThreeWayMergeState
{
    private readonly IReadOnlyDictionary<int, MergeChoice> _choices;

    private ThreeWayMergeState(IReadOnlyDictionary<int, MergeChoice> choices) => _choices = choices;

    /// <summary>Nothing decided yet.</summary>
    public static ThreeWayMergeState Empty { get; } = new(new Dictionary<int, MergeChoice>());

    /// <summary>How many regions the user has decided explicitly.</summary>
    public int ResolvedCount => _choices.Count;

    /// <summary>True when the user has made at least one decision.</summary>
    public bool HasResolutions => _choices.Count > 0;

    /// <summary>The decision for a region, or <see cref="MergeChoice.Unresolved"/>.</summary>
    public MergeChoice For(int regionIndex) =>
        _choices.TryGetValue(regionIndex, out var choice) ? choice : MergeChoice.Unresolved;

    /// <summary>
    /// Returns a state with <paramref name="regionIndex"/> decided. Setting
    /// <see cref="MergeChoice.Unresolved"/> clears the decision rather than storing it, so the counts
    /// stay honest about what has actually been answered.
    /// </summary>
    public ThreeWayMergeState With(int regionIndex, MergeChoice choice)
    {
        var next = new Dictionary<int, MergeChoice>(_choices);

        if (choice == MergeChoice.Unresolved)
        {
            next.Remove(regionIndex);
        }
        else
        {
            next[regionIndex] = choice;
        }

        return new ThreeWayMergeState(next);
    }

    /// <summary>Drops every decision.</summary>
    public ThreeWayMergeState Clear() => Empty;

    /// <summary>
    /// Drops decisions for regions that no longer exist. Changing a comparison option re-runs the
    /// merge and can produce fewer regions; without this a stale index would silently resolve the
    /// wrong one.
    /// </summary>
    public ThreeWayMergeState RemapTo(int regionCount) =>
        _choices.Keys.All(i => i < regionCount)
            ? this
            : new ThreeWayMergeState(_choices
                .Where(pair => pair.Key < regionCount)
                .ToDictionary(pair => pair.Key, pair => pair.Value));

    /// <summary>
    /// How many of <paramref name="result"/>'s conflicts still have no decision - the number that has
    /// to reach zero before a merge is finished, and the one a save has to warn about.
    /// </summary>
    public int UnresolvedConflicts(ThreeWayResult result)
    {
        var count = 0;

        for (var i = 0; i < result.Regions.Count; i++)
        {
            if (result.Regions[i].IsConflict && For(i) == MergeChoice.Unresolved)
            {
                count++;
            }
        }

        return count;
    }
}
