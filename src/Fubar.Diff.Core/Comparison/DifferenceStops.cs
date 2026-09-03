using System.Collections.Generic;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Comparison;

/// <summary>
/// One place navigation can stop: a hunk, or a run of ignored rows.
/// </summary>
/// <param name="StartRow">First row of the stop, in aligned-view indices.</param>
/// <param name="EndRow">Last row of the stop, inclusive.</param>
/// <param name="HunkIndex">Which hunk this is, or -1 for a run of ignored rows.</param>
public readonly record struct DifferenceStop(int StartRow, int EndRow, int HunkIndex)
{
    /// <summary>True for a run of ignored rows, which forms no hunk.</summary>
    public bool IsIgnored => HunkIndex < 0;
}

/// <summary>
/// Every place worth stopping in a comparison, INCLUDING the ignored rows that ordinary navigation
/// steps straight past.
///
/// <para>Ordinary Prev/Next walks hunks, and an ignored row forms no hunk by design - it is not
/// something the user asked to see, and stopping on one every time would make the ignore rules useless.
/// But "show me what I told you to ignore" is a real question, usually asked right after adding a rule
/// and once more before trusting the diff, and a faint band you have to find by scrolling is a poor
/// answer to it. This is the list that answers it, and Shift+Alt+Up/Down is what walks it.</para>
///
/// <para>Consecutive ignored rows are ONE stop, for the same reason the location map draws them as one
/// mark: a fifteen-line block whose indentation changed is one thing that happened, and stopping
/// fifteen times to say so is not navigation.</para>
///
/// <para>Position is taken as a ROW rather than as an index into this list, so nothing has to be kept
/// in step. The current row can be set by a click, the map, the tree or the toolbar, and every one of
/// those already updates what this reads.</para>
/// </summary>
public static class DifferenceStops
{
    /// <summary>Hunks and ignored runs together, in document order.</summary>
    public static IReadOnlyList<DifferenceStop> All(
        IReadOnlyList<DiffLine> lines, IReadOnlyList<DiffHunk> hunks)
    {
        var stops = new List<DifferenceStop>(hunks.Count);

        for (var i = 0; i < hunks.Count; i++)
        {
            stops.Add(new DifferenceStop(hunks[i].StartIndex, hunks[i].EndIndex, i));
        }

        for (var row = 0; row < lines.Count; row++)
        {
            if (!lines[row].IsIgnored)
            {
                continue;
            }

            var end = row;
            while (end + 1 < lines.Count && lines[end + 1].IsIgnored)
            {
                end++;
            }

            stops.Add(new DifferenceStop(row, end, -1));
            row = end;
        }

        // Hunks were added first and ignored runs second, so this cannot be left to insertion order.
        stops.Sort((a, b) => a.StartRow.CompareTo(b.StartRow));

        return stops;
    }

    /// <summary>
    /// The first stop below <paramref name="currentRow"/>, wrapping to the top. Null when there is
    /// nothing to walk. Pass -1 for "nowhere yet", which lands on the first stop.
    /// </summary>
    public static DifferenceStop? Next(IReadOnlyList<DifferenceStop> stops, int currentRow)
    {
        if (stops.Count == 0)
        {
            return null;
        }

        foreach (var stop in stops)
        {
            if (stop.StartRow > currentRow)
            {
                return stop;
            }
        }

        return stops[0];
    }

    /// <summary>
    /// The last stop above <paramref name="currentRow"/>, wrapping to the bottom. From "nowhere yet"
    /// (-1) this lands on the LAST stop, which is what pressing "previous" first is asking for and what
    /// <see cref="HunkNavigator.Previous"/> already does.
    /// </summary>
    public static DifferenceStop? Previous(IReadOnlyList<DifferenceStop> stops, int currentRow)
    {
        if (stops.Count == 0)
        {
            return null;
        }

        for (var i = stops.Count - 1; i >= 0; i--)
        {
            if (stops[i].StartRow < currentRow)
            {
                return stops[i];
            }
        }

        return stops[^1];
    }
}
