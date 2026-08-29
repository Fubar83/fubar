using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Fubar.Diff.Core.Merge;

/// <summary>
/// One row of a three-way merge: the text on each of the three documents, plus which region it belongs
/// to. Any side may be absent, which is what keeps the three panes row-aligned - the same filler
/// discipline the two-way view depends on, extended by one column.
/// </summary>
/// <param name="BaseNumber">1-based line number in the ancestor, or null when it has no line here.</param>
/// <param name="BaseText">The ancestor's text, or null.</param>
/// <param name="LeftNumber">1-based line number in the left document, or null.</param>
/// <param name="LeftText">The left document's text, or null.</param>
/// <param name="RightNumber">1-based line number in the right document, or null.</param>
/// <param name="RightText">The right document's text, or null.</param>
/// <param name="Kind">What happened to the region this row belongs to.</param>
/// <param name="RegionIndex">
/// Index into <see cref="ThreeWayResult.Regions"/>, or -1 for a stable row. Carried on the row so a
/// renderer or a click can get from a position back to the decision it belongs to without searching.
/// </param>
public sealed record ThreeWayLine(
    int? BaseNumber,
    string? BaseText,
    int? LeftNumber,
    string? LeftText,
    int? RightNumber,
    string? RightText,
    MergeKind Kind,
    int RegionIndex)
{
    /// <summary>True when this row is part of a region rather than stable context.</summary>
    public bool IsChange => Kind != MergeKind.Unchanged;

    /// <summary>The text on one side, or null when that side has no line here.</summary>
    public string? TextOn(MergeSide side) => side switch
    {
        MergeSide.Left => LeftText,
        MergeSide.Right => RightText,
        _ => BaseText,
    };

    /// <summary>The 1-based line number on one side, or null when that side has no line here.</summary>
    public int? NumberOn(MergeSide side) => side switch
    {
        MergeSide.Left => LeftNumber,
        MergeSide.Right => RightNumber,
        _ => BaseNumber,
    };
}

/// <summary>
/// A contiguous run of rows the merge treats as one decision.
///
/// Regions are the unit of everything above this: navigation stops on them, a resolution is stored per
/// region, and the merged document is assembled region by region. Only UNSTABLE runs are regions -
/// context between them is not something anyone decides about.
/// </summary>
/// <param name="StartIndex">First row index, inclusive.</param>
/// <param name="EndIndex">Last row index, inclusive.</param>
/// <param name="Kind">What happened here.</param>
public sealed record MergeRegion(int StartIndex, int EndIndex, MergeKind Kind)
{
    /// <summary>How many rows the region covers.</summary>
    public int Length => EndIndex - StartIndex + 1;

    /// <summary>Whether this region needs a person.</summary>
    public bool IsConflict => Kind == MergeKind.Conflict;
}

/// <summary>
/// A completed three-way merge: the aligned rows, the regions derived from them, and the counts a
/// status line needs. Built via <see cref="Create"/> so the regions can never disagree with the rows
/// they describe.
/// </summary>
public sealed class ThreeWayResult
{
    private ThreeWayResult(IReadOnlyList<ThreeWayLine> lines, IReadOnlyList<MergeRegion> regions)
    {
        Lines = lines;
        Regions = regions;

        foreach (var region in regions)
        {
            if (region.IsConflict)
            {
                ConflictCount++;
            }
            else
            {
                AutoMergedCount++;
            }
        }
    }

    /// <summary>Every row, in document order, stable context and fillers included.</summary>
    public IReadOnlyList<ThreeWayLine> Lines { get; }

    /// <summary>The regions, in document order.</summary>
    public IReadOnlyList<MergeRegion> Regions { get; }

    /// <summary>How many regions need a person.</summary>
    public int ConflictCount { get; }

    /// <summary>How many regions the merge settled on its own.</summary>
    public int AutoMergedCount { get; }

    /// <summary>True when the three documents agree everywhere - nothing to merge.</summary>
    public bool AreIdentical => Regions.Count == 0;

    /// <summary>Nothing loaded yet.</summary>
    public static ThreeWayResult Empty { get; } = new([], []);

    /// <summary>
    /// Builds a result from aligned rows, deriving the regions from the <see cref="ThreeWayLine.RegionIndex"/>
    /// each row already carries. Rows sharing an index must be contiguous - the merger emits them that
    /// way, and a region that was not contiguous could not be resolved as one decision.
    /// </summary>
    public static ThreeWayResult Create(IReadOnlyList<ThreeWayLine> lines)
    {
        var regions = new List<MergeRegion>();
        var start = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var index = lines[i].RegionIndex;

            if (index >= 0)
            {
                if (start < 0)
                {
                    start = i;
                }

                // Still inside the same region unless the NEXT row says otherwise.
                if (i + 1 < lines.Count && lines[i + 1].RegionIndex == index)
                {
                    continue;
                }

                regions.Add(new MergeRegion(start, i, lines[i].Kind));
                start = -1;
            }
            else
            {
                start = -1;
            }
        }

        return new ThreeWayResult(lines, new ReadOnlyCollection<MergeRegion>(regions));
    }
}
