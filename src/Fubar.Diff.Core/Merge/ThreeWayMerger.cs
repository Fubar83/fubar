using System;
using System.Collections.Generic;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Merge;

/// <summary>
/// One of the three documents going into a merge: what its lines are MATCHED on, and what they SAY.
///
/// Two lists for the same reason every other comparison here keeps two: the keys carry whatever
/// normalisation is in force (case folding, comment stripping) and are never shown, while the lines are
/// what the user reads. Merging on keys and emitting lines is what lets "ignore case" affect whether
/// something is a conflict without ever writing a case-folded copy of anyone's file to disk.
/// </summary>
/// <param name="Keys">The comparison key per line.</param>
/// <param name="Lines">The display text per line. Same length as <paramref name="Keys"/>.</param>
public sealed record MergeDocument(IReadOnlyList<string> Keys, IReadOnlyList<string> Lines)
{
    /// <summary>A document whose keys are its lines - the plain case, and what tests want.</summary>
    public static MergeDocument Of(IReadOnlyList<string> lines) => new(lines, lines);

    /// <summary>An absent document.</summary>
    public static MergeDocument Empty { get; } = new([], []);

    /// <summary>How many lines it has.</summary>
    public int Count => Keys.Count;
}

/// <summary>
/// Three-way merge: works out, region by region, which of two edits to a common ancestor can be taken
/// automatically and which genuinely disagree.
///
/// This is the classic diff3 algorithm, and its whole idea is that the ancestor turns an unanswerable
/// question into an answerable one. Two documents can only tell you THAT a region differs; someone has
/// to decide every single one. Add what both started from and most differences answer themselves -
/// only one side moved, so that side wins - leaving just the regions both sides touched, which is the
/// set worth a person's attention.
///
/// The alignment itself is not redone here. Two ordinary two-way diffs - ancestor against each edit -
/// already say which lines survived unchanged on each side, and this reads those two answers together:
/// wherever both agree that a line is untouched, all three documents are synchronised, and everything
/// between two such points is one region to classify. Reusing the two-way aligner is not just less
/// code, it is what keeps a three-way merge consistent with the two-way diff of the same files -
/// including every comparison option, since those have already been applied to the keys.
/// </summary>
public static class ThreeWayMerger
{
    /// <summary>
    /// Merges two edits of a common ancestor.
    /// </summary>
    /// <param name="ancestor">The common ancestor, the base of the merge.</param>
    /// <param name="left">One edit.</param>
    /// <param name="right">The other edit.</param>
    /// <param name="ancestorToLeft">
    /// A two-way alignment of the ancestor against <paramref name="left"/>, with the ancestor as the
    /// LEFT-hand side of that alignment. Only its unchanged rows are read.
    /// </param>
    /// <param name="ancestorToRight">The same, for <paramref name="right"/>.</param>
    public static ThreeWayResult Merge(
        MergeDocument ancestor,
        MergeDocument left,
        MergeDocument right,
        IReadOnlyList<DiffLine> ancestorToLeft,
        IReadOnlyList<DiffLine> ancestorToRight)
    {
        var leftMatch = MatchesFrom(ancestorToLeft, ancestor.Count);
        var rightMatch = MatchesFrom(ancestorToRight, ancestor.Count);

        var rows = new List<ThreeWayLine>(Math.Max(ancestor.Count, Math.Max(left.Count, right.Count)));

        var b = 0;
        var l = 0;
        var r = 0;
        var regionIndex = 0;

        while (b < ancestor.Count || l < left.Count || r < right.Count)
        {
            // All three synchronised on this line: it survived both edits untouched.
            if (b < ancestor.Count && leftMatch[b] == l && rightMatch[b] == r)
            {
                rows.Add(new ThreeWayLine(
                    b + 1, ancestor.Lines[b],
                    l + 1, left.Lines[l],
                    r + 1, right.Lines[r],
                    MergeKind.Unchanged,
                    RegionIndex: -1));

                b++;
                l++;
                r++;
                continue;
            }

            var (nextB, nextL, nextR) = NextSync(leftMatch, rightMatch, b, l, r, ancestor.Count, left.Count, right.Count);

            var kind = Classify(ancestor, left, right, b, nextB, l, nextL, r, nextR);

            // Defensive: a run all three agree on that nonetheless failed to synchronise is not a
            // decision anyone should be asked to make, so it is emitted as context rather than as a
            // region nobody can tell apart from the lines around it.
            var index = kind == MergeKind.Unchanged ? -1 : regionIndex++;

            AppendRegion(rows, ancestor, left, right, b, nextB, l, nextL, r, nextR, kind, index);

            b = nextB;
            l = nextL;
            r = nextR;
        }

        return ThreeWayResult.Create(rows);
    }

    /// <summary>
    /// Which line of the other document each ancestor line survived as, or -1 where it did not survive.
    ///
    /// Only <see cref="ChangeKind.Unchanged"/> rows count. A modified row means the aligner paired two
    /// lines that are NOT equal, which for merging purposes is a change like any other - taking it as a
    /// match would quietly merge one side's edit away.
    /// </summary>
    private static int[] MatchesFrom(IReadOnlyList<DiffLine> rows, int ancestorCount)
    {
        var matches = new int[ancestorCount];
        Array.Fill(matches, -1);

        foreach (var row in rows)
        {
            if (row.Kind != ChangeKind.Unchanged
                || row.LeftNumber is not { } ancestorLine
                || row.RightNumber is not { } otherLine)
            {
                continue;
            }

            if (ancestorLine >= 1 && ancestorLine <= ancestorCount)
            {
                matches[ancestorLine - 1] = otherLine - 1;
            }
        }

        return matches;
    }

    /// <summary>
    /// The next point at which all three documents line up again, or the end of all three when they
    /// never do.
    ///
    /// A candidate has to be matched on BOTH sides and at or past where each cursor already is -
    /// a match that points backwards would produce a region running the wrong way.
    /// </summary>
    private static (int Base, int Left, int Right) NextSync(
        int[] leftMatch,
        int[] rightMatch,
        int b,
        int l,
        int r,
        int ancestorCount,
        int leftCount,
        int rightCount)
    {
        for (var i = b; i < ancestorCount; i++)
        {
            if (leftMatch[i] >= l && rightMatch[i] >= r)
            {
                return (i, leftMatch[i], rightMatch[i]);
            }
        }

        return (ancestorCount, leftCount, rightCount);
    }

    /// <summary>
    /// What happened to one region: who moved, and whether the two who moved agree.
    ///
    /// "Both sides made the same edit" is a distinct answer from "they conflict", and worth the extra
    /// comparison - it is what happens on every cherry-pick, every shared reformatting, and every
    /// rebase of a change someone else already landed. Reporting those as conflicts would bury the
    /// handful that are real.
    /// </summary>
    private static MergeKind Classify(
        MergeDocument ancestor,
        MergeDocument left,
        MergeDocument right,
        int b,
        int nextB,
        int l,
        int nextL,
        int r,
        int nextR)
    {
        var leftChanged = !SlicesEqual(ancestor.Keys, b, nextB, left.Keys, l, nextL);
        var rightChanged = !SlicesEqual(ancestor.Keys, b, nextB, right.Keys, r, nextR);

        if (!leftChanged)
        {
            return rightChanged ? MergeKind.RightOnly : MergeKind.Unchanged;
        }

        if (!rightChanged)
        {
            return MergeKind.LeftOnly;
        }

        return SlicesEqual(left.Keys, l, nextL, right.Keys, r, nextR)
            ? MergeKind.BothSame
            : MergeKind.Conflict;
    }

    private static bool SlicesEqual(
        IReadOnlyList<string> first,
        int firstFrom,
        int firstTo,
        IReadOnlyList<string> second,
        int secondFrom,
        int secondTo)
    {
        if (firstTo - firstFrom != secondTo - secondFrom)
        {
            return false;
        }

        for (var i = 0; i < firstTo - firstFrom; i++)
        {
            if (!string.Equals(first[firstFrom + i], second[secondFrom + i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Emits one region's rows, padded so all three columns advance together.
    ///
    /// The three slices are usually different lengths, and the padding is what preserves the invariant
    /// the panes depend on: row <c>i</c> is the same row in all three editors, so scroll sync stays a
    /// plain offset copy and a region is one horizontal band across the window. The sides are aligned
    /// at the TOP of the region rather than matched up within it - anything cleverer would be a second
    /// alignment pass whose answer could disagree with the one that produced the region.
    /// </summary>
    private static void AppendRegion(
        List<ThreeWayLine> rows,
        MergeDocument ancestor,
        MergeDocument left,
        MergeDocument right,
        int b,
        int nextB,
        int l,
        int nextL,
        int r,
        int nextR,
        MergeKind kind,
        int regionIndex)
    {
        var length = Math.Max(nextB - b, Math.Max(nextL - l, nextR - r));

        for (var i = 0; i < length; i++)
        {
            rows.Add(new ThreeWayLine(
                b + i < nextB ? b + i + 1 : null,
                b + i < nextB ? ancestor.Lines[b + i] : null,
                l + i < nextL ? l + i + 1 : null,
                l + i < nextL ? left.Lines[l + i] : null,
                r + i < nextR ? r + i + 1 : null,
                r + i < nextR ? right.Lines[r + i] : null,
                kind,
                regionIndex));
        }
    }
}
