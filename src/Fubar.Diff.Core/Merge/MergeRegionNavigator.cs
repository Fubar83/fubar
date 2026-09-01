using System.Collections.Generic;

namespace Fubar.Diff.Core.Merge;

/// <summary>
/// Domain policy for stepping through a merge. The three-way counterpart of
/// <see cref="Comparison.HunkNavigator"/>, and pure for the same reason: wrap-around and boundary rules
/// are exactly what gets subtly wrong, and they should be testable without a window.
///
/// What it adds over the two-way navigator is that not every region is worth stopping on. A merge of
/// any size is mostly regions only one side touched, which are already decided; walking through all of
/// them to reach the four that actually conflict is how a three-way tool becomes slower than doing it
/// by hand.
/// </summary>
public static class MergeRegionNavigator
{
    /// <summary>
    /// The next region index, wrapping past the end, or null when there are none.
    /// </summary>
    /// <param name="regions">The regions in document order.</param>
    /// <param name="currentIndex">Where we are now, or -1 for nowhere.</param>
    /// <param name="conflictsOnly">
    /// Stop only on regions that need a person. With none left - every conflict resolved, or none to
    /// begin with - this returns null rather than falling back to stepping through auto-merged
    /// regions, so "next conflict" going nowhere is what tells the user they are finished.
    /// </param>
    public static int? Next(IReadOnlyList<MergeRegion> regions, int currentIndex, bool conflictsOnly = false)
    {
        if (regions.Count == 0)
        {
            return null;
        }

        for (var step = 1; step <= regions.Count; step++)
        {
            var index = ((currentIndex + step) % regions.Count + regions.Count) % regions.Count;

            if (!conflictsOnly || regions[index].IsConflict)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// The previous region index, wrapping past the start, or null when there are none. From "nowhere"
    /// this lands on the LAST match, which is what pressing "previous" first means.
    /// </summary>
    public static int? Previous(IReadOnlyList<MergeRegion> regions, int currentIndex, bool conflictsOnly = false)
    {
        if (regions.Count == 0)
        {
            return null;
        }

        var from = currentIndex < 0 ? regions.Count : currentIndex;

        for (var step = 1; step <= regions.Count; step++)
        {
            var index = ((from - step) % regions.Count + regions.Count) % regions.Count;

            if (!conflictsOnly || regions[index].IsConflict)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// The index of the region containing <paramref name="rowIndex"/>, or -1 for stable context. Keeps
    /// the selection in step when the user scrolls or clicks rather than navigating.
    /// </summary>
    public static int IndexOfRegionContaining(IReadOnlyList<MergeRegion> regions, int rowIndex)
    {
        for (var i = 0; i < regions.Count; i++)
        {
            if (rowIndex >= regions[i].StartIndex && rowIndex <= regions[i].EndIndex)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Which lines of each of the three ORIGINAL files a region covers, for captioning it.
    ///
    /// Null on a side that contributes nothing - the row indices address the aligned view, which
    /// contains fillers that exist in none of the three files, so reporting those as line numbers
    /// would name lines the user cannot go and look at.
    /// </summary>
    public static MergeRegionRange RangeOf(IReadOnlyList<ThreeWayLine> lines, MergeRegion region)
    {
        int? baseStart = null, baseEnd = null;
        int? leftStart = null, leftEnd = null;
        int? rightStart = null, rightEnd = null;

        var last = region.EndIndex < lines.Count - 1 ? region.EndIndex : lines.Count - 1;

        for (var i = region.StartIndex < 0 ? 0 : region.StartIndex; i <= last; i++)
        {
            var row = lines[i];

            if (row.BaseNumber is { } b)
            {
                baseStart ??= b;
                baseEnd = b;
            }

            if (row.LeftNumber is { } l)
            {
                leftStart ??= l;
                leftEnd = l;
            }

            if (row.RightNumber is { } r)
            {
                rightStart ??= r;
                rightEnd = r;
            }
        }

        return new MergeRegionRange(baseStart, baseEnd, leftStart, leftEnd, rightStart, rightEnd);
    }
}

/// <summary>The lines a region covers in each of the three files. Null where that file has none.</summary>
public sealed record MergeRegionRange(
    int? BaseStart,
    int? BaseEnd,
    int? LeftStart,
    int? LeftEnd,
    int? RightStart,
    int? RightEnd);
