using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Fubar.Diff.Core.Models;

/// <summary>
/// The comparison of two documents as an aligned list of rows, plus the derived hunks and counts a
/// viewer needs. Construct via <see cref="Create"/> so hunks and statistics stay consistent with the
/// rows they describe.
/// </summary>
public sealed class DiffResult
{
    private DiffResult(IReadOnlyList<DiffLine> lines, IReadOnlyList<DiffHunk> hunks)
    {
        Lines = lines;
        Hunks = hunks;
        Inserted = lines.Count(l => l.Kind == ChangeKind.Inserted);
        Deleted = lines.Count(l => l.Kind == ChangeKind.Deleted);
        Modified = lines.Count(l => l.Kind == ChangeKind.Modified);
        Moved = lines
            .Where(l => l.IsMoved)
            .SelectMany(l => new[] { l.LeftMoveId, l.RightMoveId })
            .Where(id => id is not null)
            .Distinct()
            .Count();
    }

    /// <summary>Every row, in document order, including unchanged context and fillers.</summary>
    public IReadOnlyList<DiffLine> Lines { get; }

    /// <summary>Contiguous runs of changed rows, in document order.</summary>
    public IReadOnlyList<DiffHunk> Hunks { get; }

    public int Inserted { get; }
    public int Deleted { get; }
    public int Modified { get; }

    /// <summary>
    /// How many BLOCKS moved, counted once each rather than once per side or once per line.
    ///
    /// Reported alongside the other counts and not subtracted from them: a moved block genuinely is
    /// a deletion here and an insertion there, and the counts have to keep agreeing with the rows,
    /// the hunks and the patch. What this adds is the sentence the reader wants - "6 changes, 2 of
    /// them blocks that only moved".
    /// </summary>
    public int Moved { get; }

    /// <summary>True when the two documents compared equal under the options used.</summary>
    public bool AreIdentical => Hunks.Count == 0;

    /// <summary>An empty result - two documents that are identical, or nothing loaded yet.</summary>
    public static DiffResult Empty { get; } = new([], []);

    /// <summary>Builds a result from aligned rows, deriving the hunks from them.</summary>
    public static DiffResult Create(IReadOnlyList<DiffLine> lines) =>
        new(lines, GroupIntoHunks(lines));

    /// <summary>
    /// Collapses runs of adjacent changed rows into hunks. Unchanged and filler-only context breaks a
    /// run; a filler is always paired with a change on the other side, so it never appears alone.
    /// </summary>
    private static ReadOnlyCollection<DiffHunk> GroupIntoHunks(IReadOnlyList<DiffLine> lines)
    {
        var hunks = new List<DiffHunk>();
        var start = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].IsChange)
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                hunks.Add(new DiffHunk(start, i - 1));
                start = -1;
            }
        }

        if (start >= 0)
        {
            hunks.Add(new DiffHunk(start, lines.Count - 1));
        }

        return hunks.AsReadOnly();
    }
}
