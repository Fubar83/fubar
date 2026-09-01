using System;
using System.Collections.Generic;
using System.Text;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Comparison;

/// <summary>
/// Recognises a block that was MOVED rather than rewritten.
///
/// A line-based diff has no way to say "this went somewhere else": a method that moved down the file
/// is a deletion here and an insertion there, and the reader has to notice that the two blocks are the
/// same text before they can dismiss them. On a reordered file that is most of the review.
///
/// This does not change what the diff SAYS - the rows keep their kinds, because that is what is
/// genuinely on disk, and the counts, the hunks, navigation, the merge and the patch all stay exactly
/// as they were. It adds a mark, so the two halves can be drawn as one thing and counted separately:
/// "4 changes and 2 blocks moved" is a far more useful sentence than "6 changes".
///
/// Marks are PER SIDE, and that is not a detail. The obvious case - a whole block deleted here and
/// inserted there - is only half of what people actually do. Swapping two methods of similar shape
/// produces no one-sided rows at all: the aligner pairs `void Helper()` against `void Run()` and calls
/// the row modified, because from a line differ's point of view that is what it is. Looking at each
/// side's own text independently finds both cases with one rule; looking only at whole rows finds the
/// first and reports the second as an unrelated pile of rewrites.
/// </summary>
public static class MoveDetector
{
    /// <summary>
    /// Marks matching runs of changed text as moves, or returns the rows untouched when there are
    /// none.
    /// </summary>
    /// <param name="rows">The aligned rows.</param>
    /// <param name="leftKeys">Comparison keys for the left document.</param>
    /// <param name="rightKeys">Comparison keys for the right document.</param>
    public static IReadOnlyList<DiffLine> Detect(
        IReadOnlyList<DiffLine> rows,
        IReadOnlyList<string> leftKeys,
        IReadOnlyList<string> rightKeys)
    {
        var fromLeft = ByUniqueContent(RunsOf(rows, leftKeys, DiffSide.Left));
        if (fromLeft.Count == 0)
        {
            return rows;
        }

        var fromRight = ByUniqueContent(RunsOf(rows, rightKeys, DiffSide.Right));
        if (fromRight.Count == 0)
        {
            return rows;
        }

        List<DiffLine>? moved = null;
        var nextId = 0;

        foreach (var (content, gone) in fromLeft)
        {
            if (!fromRight.TryGetValue(content, out var arrived))
            {
                continue;
            }

            moved ??= [.. rows];

            var id = nextId++;

            for (var i = gone.From; i <= gone.To; i++)
            {
                moved[i] = moved[i] with { LeftMoveId = id };
            }

            for (var i = arrived.From; i <= arrived.To; i++)
            {
                moved[i] = moved[i] with { RightMoveId = id };
            }
        }

        return moved ?? rows;
    }

    /// <summary>
    /// Maximal runs of changed text on one side, with the keys they cover.
    ///
    /// One side at a time, and by that side's own content: a deleted row contributes to the left runs
    /// only, an inserted row to the right runs only, and a modified row to BOTH - its left text left
    /// the file and its right text arrived in it, and either half can be the one that turns up
    /// somewhere else.
    ///
    /// Runs rather than single rows because a move is a BLOCK: half a method appearing elsewhere is
    /// not a move, and matching row by row would report exactly that.
    ///
    /// A run is broken by unchanged context AND by a change of kind. The second boundary matters more
    /// than it looks: a deleted row and a modified row are different KINDS of evidence - one line has
    /// no counterpart, the other was paired with something - and letting them share a run means an
    /// ordinary edit that happens to sit directly against a moved block glues the two together into
    /// one run that matches nothing. That is a common shape (move a method, tweak the line below it)
    /// and it would silently cost the move.
    /// </summary>
    private static List<(int From, int To, string Content)> RunsOf(
        IReadOnlyList<DiffLine> rows,
        IReadOnlyList<string> keys,
        DiffSide side)
    {
        var runs = new List<(int, int, string)>();
        var start = -1;
        var kind = ChangeKind.Unchanged;

        for (var i = 0; i <= rows.Count; i++)
        {
            var open = i < rows.Count && LineNumber(rows[i], side) is not null && rows[i].IsChange;

            if (open && (start < 0 || rows[i].Kind == kind))
            {
                if (start < 0)
                {
                    start = i;
                    kind = rows[i].Kind;
                }

                continue;
            }

            if (start >= 0)
            {
                if (Content(rows, start, i - 1, keys, side) is { } run)
                {
                    runs.Add(run);
                }

                start = -1;
            }

            // A row that ended the previous run because its KIND differs still starts one of its own.
            if (open)
            {
                start = i;
                kind = rows[i].Kind;
            }
        }

        return runs;
    }

    /// <summary>
    /// The run trimmed of blank lines at either end, with its keys joined - or null when there is
    /// nothing left worth considering.
    ///
    /// The trimming is what makes this work on real code rather than only on fixtures. Methods are
    /// separated by blank lines, so a block that moves takes one with it - and it takes the one that
    /// happened to be adjacent, which is BELOW it in the file it left and ABOVE it in the file it
    /// arrived in. The two runs then differ by where their blank line sits and match nothing, over a
    /// line that carries no identity in the first place. A blank INSIDE the block is kept: it is part
    /// of the block's shape, and two blocks that differ there are not the same block.
    ///
    /// A run of nothing but blank lines trims away to nothing, which is the right answer for it too:
    /// blank runs are everywhere, identical to each other by definition, and calling one a "move"
    /// says nothing.
    /// </summary>
    private static (int From, int To, string Content)? Content(
        IReadOnlyList<DiffLine> rows,
        int from,
        int to,
        IReadOnlyList<string> keys,
        DiffSide side)
    {
        var lineKeys = new string[to - from + 1];

        for (var i = from; i <= to; i++)
        {
            // Guarded rather than trusted: a row's number indexes a document this pass was handed
            // separately, and being wrong here would mean marking the wrong lines rather than throwing.
            if (LineNumber(rows[i], side) is not { } line || line < 1 || line > keys.Count)
            {
                return null;
            }

            lineKeys[i - from] = keys[line - 1];
        }

        var first = 0;
        var last = lineKeys.Length - 1;

        while (first <= last && string.IsNullOrWhiteSpace(lineKeys[first]))
        {
            first++;
        }

        while (last >= first && string.IsNullOrWhiteSpace(lineKeys[last]))
        {
            last--;
        }

        if (first > last)
        {
            return null;
        }

        var builder = new StringBuilder();
        for (var i = first; i <= last; i++)
        {
            builder.Append(lineKeys[i]).Append('\n');
        }

        return (from + first, from + last, builder.ToString());
    }

    /// <summary>
    /// Runs keyed by their content, keeping only content that appears EXACTLY ONCE.
    ///
    /// This is what makes the feature usable rather than noisy, and it is the same idea patience
    /// diffing rests on. A block distinctive enough to appear once on each side is almost certainly
    /// the same block, moved; a run of <c>}</c> appears everywhere, and pairing one arbitrarily with
    /// another would draw a confident line between two unrelated braces. Ambiguity is reported as "not
    /// a move" rather than guessed at - a diff that quietly tells the reader to skip the wrong block
    /// is worse than one that tells them nothing.
    /// </summary>
    private static Dictionary<string, (int From, int To)> ByUniqueContent(
        List<(int From, int To, string Content)> runs)
    {
        var byContent = new Dictionary<string, (int, int)>(runs.Count, StringComparer.Ordinal);
        var repeated = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (from, to, content) in runs)
        {
            if (repeated.Contains(content))
            {
                continue;
            }

            if (!byContent.TryAdd(content, (from, to)))
            {
                byContent.Remove(content);
                repeated.Add(content);
            }
        }

        return byContent;
    }

    private static int? LineNumber(DiffLine row, DiffSide side) =>
        side == DiffSide.Left ? row.LeftNumber : row.RightNumber;
}
