using System.Collections.Generic;

namespace Fubar.Diff.Core.Merge;

/// <summary>
/// Builds the merged file from a three-way result plus the user's decisions.
///
/// Pure, and the ONLY thing that decides what a three-way merge writes to disk - the same rule its
/// two-way counterpart <see cref="MergedDocument"/> follows, and for the same reason: the editors'
/// text contains filler rows that exist in none of the three files, so reading a pane back would write
/// blank lines into the result.
/// </summary>
public static class ThreeWayMergedDocument
{
    /// <summary>
    /// Produces the merged lines.
    ///
    /// Stable rows contribute the ancestor's text - all three agree, so it does not matter which is
    /// read. Within a region, an explicit decision wins; without one, the region's own kind decides,
    /// which is what "auto-merge" means here and is why a merge with no decisions at all is already
    /// correct everywhere the two edits did not collide.
    /// </summary>
    public static IReadOnlyList<string> Build(ThreeWayResult result, ThreeWayMergeState state)
    {
        var merged = new List<string>(result.Lines.Count);

        // Regions are ordered and non-overlapping, so one advancing cursor is enough - no search per
        // row. The same walk MergedDocument does.
        var cursor = 0;

        for (var i = 0; i < result.Lines.Count; i++)
        {
            var row = result.Lines[i];

            while (cursor < result.Regions.Count && result.Regions[cursor].EndIndex < i)
            {
                cursor++;
            }

            var inRegion = cursor < result.Regions.Count
                           && i >= result.Regions[cursor].StartIndex
                           && i <= result.Regions[cursor].EndIndex;

            var side = inRegion
                ? SideFor(result.Regions[cursor].Kind, state.For(cursor))
                : MergeSide.Base;

            // A row with no line on the chosen side means that side genuinely has nothing here, so the
            // merged file gets nothing - NOT a blank line. This is what makes taking the side that
            // deleted something actually delete it.
            if (row.TextOn(side) is { } text)
            {
                merged.Add(text);
            }
        }

        return merged;
    }

    /// <summary>
    /// Which document a region's content comes from.
    ///
    /// An unresolved CONFLICT falls back to the ancestor, which is the conservative answer rather than
    /// the useful one, and deliberately so: the alternatives are to invent a merge nobody approved, or
    /// to write conflict markers into a file the user asked to save. Callers are expected to check
    /// <see cref="ThreeWayMergeState.UnresolvedConflicts"/> and say something before saving - the
    /// fallback exists so that a preview always renders, not so that saving past a conflict is fine.
    /// </summary>
    private static MergeSide SideFor(MergeKind kind, MergeChoice choice) => choice switch
    {
        MergeChoice.TakeBase => MergeSide.Base,
        MergeChoice.TakeLeft => MergeSide.Left,
        MergeChoice.TakeRight => MergeSide.Right,
        _ => kind switch
        {
            // Exactly one side moved: that side is the merge, with nothing to ask.
            MergeKind.LeftOnly => MergeSide.Left,
            MergeKind.RightOnly => MergeSide.Right,

            // Both moved, identically - either is right, and left is picked only to be deterministic.
            MergeKind.BothSame => MergeSide.Left,

            _ => MergeSide.Base,
        },
    };
}
