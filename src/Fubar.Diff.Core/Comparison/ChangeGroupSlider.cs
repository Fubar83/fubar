using System.Collections.Generic;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Comparison;

/// <summary>
/// Slides one-sided change groups to the position that reads best, without changing what the diff
/// says.
///
/// The problem, and why no aligner can fix it on its own. When a run of added or removed lines is
/// bounded by lines identical to the ones just inside it, the diff is AMBIGUOUS: several positions for
/// that run produce exactly the same two documents, and every one of them is equally minimal, so the
/// algorithm has no basis to prefer one. Source code hits this constantly, because the lines at a
/// block's edges are the least distinctive ones in the file. Move a method, and the run of removed
/// lines is just as likely to come back as
///
/// <code>
/// -    }
/// -    void B() {
/// -        b();
/// </code>
///
/// as it is the version a human would write - the same three lines, slid down one, spelling a whole
/// method. Both are correct. Only one is readable.
///
/// So this is a presentation pass over a finished alignment, exactly like git's own "compaction"
/// heuristic, which exists for the same reason and is on by default there. Every shift it makes is
/// provably content-neutral: a group only moves across a line when that line is IDENTICAL to the one
/// leaving the group, so the merged document is unchanged, the counts are unchanged, and only the
/// pairing of equal lines moves.
/// </summary>
public static class ChangeGroupSlider
{
    /// <summary>
    /// Returns the rows with each one-sided group at its best-scoring legal position.
    ///
    /// Equality is judged on the comparison KEYS (two lines are interchangeable exactly when the diff
    /// considered them equal), while the score reads the DISPLAY lines, since indentation is the whole
    /// signal and trimming it away is precisely what a key may have done.
    /// </summary>
    public static IReadOnlyList<DiffLine> Compact(
        IReadOnlyList<DiffLine> rows,
        IReadOnlyList<string> leftKeys,
        IReadOnlyList<string> leftLines,
        IReadOnlyList<string> rightKeys,
        IReadOnlyList<string> rightLines)
    {
        List<DiffLine>? working = null;
        var index = 0;

        while (index < rows.Count)
        {
            var current = working ?? rows;

            if (!TryGroupAt(current, index, out var side, out var end))
            {
                index++;
                continue;
            }

            var keys = side == DiffSide.Left ? leftKeys : rightKeys;
            var lines = side == DiffSide.Left ? leftLines : rightLines;

            var shift = BestShift(current, index, end, side, keys, lines);

            if (shift == 0)
            {
                index = end + 1;
                continue;
            }

            // Nothing moves until something actually needs to: a document whose groups are all already
            // best-placed - the common case - comes back as the very list it went in as.
            working ??= [.. rows];

            for (var step = 0; step < shift; step++)
            {
                SlideDown(working, index + step, end + step, side);
            }

            for (var step = 0; step > shift; step--)
            {
                SlideUp(working, index + step, end + step, side);
            }

            index = end + shift + 1;
        }

        return working ?? rows;
    }

    /// <summary>
    /// Whether a maximal run of rows changed on ONE side only starts at <paramref name="start"/>, and
    /// where it ends.
    ///
    /// Modified rows are excluded because they have a line on both sides - there is no hole to slide.
    /// A deletion run butting straight up against an insertion run is left alone for the same reason
    /// the boundary test below is strict: sliding is only safe across CONTEXT.
    /// </summary>
    private static bool TryGroupAt(IReadOnlyList<DiffLine> rows, int start, out DiffSide side, out int end)
    {
        side = DiffSide.Left;
        end = start;

        var kind = rows[start].Kind;

        if (kind is not (ChangeKind.Deleted or ChangeKind.Inserted))
        {
            return false;
        }

        side = kind == ChangeKind.Deleted ? DiffSide.Left : DiffSide.Right;

        while (end + 1 < rows.Count && rows[end + 1].Kind == kind)
        {
            end++;
        }

        return true;
    }

    /// <summary>
    /// How far the group should move: the legal shift with the lowest score, ties going to the lowest
    /// position. Sliding down on a tie matches git's own default direction, and keeps a run of removed
    /// lines next to the block it belongs to rather than the one above it.
    /// </summary>
    private static int BestShift(
        IReadOnlyList<DiffLine> rows,
        int start,
        int end,
        DiffSide side,
        IReadOnlyList<string> keys,
        IReadOnlyList<string> lines)
    {
        var bestShift = 0;
        var bestScore = ScoreAt(rows, start, end, side, lines, offset: 0);

        for (var shift = 1; CanSlideDown(rows, start, end, side, keys, shift - 1); shift++)
        {
            var score = ScoreAt(rows, start, end, side, lines, shift);

            if (score < bestScore)
            {
                bestScore = score;
                bestShift = shift;
            }
        }

        for (var shift = -1; CanSlideUp(rows, start, end, side, keys, shift + 1); shift--)
        {
            var score = ScoreAt(rows, start, end, side, lines, shift);

            // Strictly better, so a tie keeps the lower position.
            if (score < bestScore)
            {
                bestScore = score;
                bestShift = shift;
            }
        }

        return bestShift;
    }

    private static bool CanSlideDown(
        IReadOnlyList<DiffLine> rows,
        int start,
        int end,
        DiffSide side,
        IReadOnlyList<string> keys,
        int offset)
    {
        var boundary = end + offset + 1;

        return boundary < rows.Count
               && IsContext(rows[boundary])
               && KeyOf(rows[start + offset], side, keys) == KeyOf(rows[boundary], side, keys);
    }

    private static bool CanSlideUp(
        IReadOnlyList<DiffLine> rows,
        int start,
        int end,
        DiffSide side,
        IReadOnlyList<string> keys,
        int offset)
    {
        var boundary = start + offset - 1;

        return boundary >= 0
               && IsContext(rows[boundary])
               && KeyOf(rows[end + offset], side, keys) == KeyOf(rows[boundary], side, keys);
    }

    /// <summary>
    /// A row a group may slide across: unchanged context with a real line on both sides.
    ///
    /// An IGNORED row is deliberately not context. It is drawn faintly precisely because the user
    /// wants to know it is there, and sliding a change group over one would move a difference past
    /// something they asked to see the position of.
    /// </summary>
    private static bool IsContext(DiffLine row) =>
        row.Kind == ChangeKind.Unchanged
        && !row.IsIgnored
        && row.LeftNumber is not null
        && row.RightNumber is not null;

    /// <summary>
    /// How bad the group's two boundaries read at a given shift. Lower is better, and the two
    /// boundaries are scored independently and summed - a placement is only as good as its worse edge.
    /// </summary>
    private static int ScoreAt(
        IReadOnlyList<DiffLine> rows,
        int start,
        int end,
        DiffSide side,
        IReadOnlyList<string> lines,
        int offset)
    {
        // The group's own lines on this side are consecutive, so its extent in the document follows
        // from the first row's number alone.
        var first = NumberOf(rows[start], side) + offset - 1;
        var last = NumberOf(rows[end], side) + offset - 1;

        return SplitScore(lines, first) + SplitScore(lines, last + 1);
    }

    /// <summary>
    /// How good a boundary immediately BEFORE <paramref name="index"/> is, lowest being best.
    ///
    /// Two rules, in order:
    /// <list type="number">
    /// <item>A boundary at the very start or end of the file, or against a blank line, is ideal. Blank
    /// lines are where an author already said one thing ends and the next begins, so a change that
    /// starts or stops there needs no explaining.</item>
    /// <item>Otherwise, prefer the least-indented line available. Indentation is the only structural
    /// signal a line-based differ has: an outdented line opens or closes something, and cutting there
    /// keeps a change inside one block instead of straddling two.</item>
    /// </list>
    /// </summary>
    private static int SplitScore(IReadOnlyList<string> lines, int index)
    {
        if (index <= 0 || index >= lines.Count)
        {
            return 0;
        }

        if (IsBlank(lines[index]) || IsBlank(lines[index - 1]))
        {
            return 0;
        }

        return 1 + Indent(lines[index]);
    }

    private static bool IsBlank(string line)
    {
        foreach (var c in line)
        {
            if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Leading whitespace width, counting a tab as one. Counting it as four would be a guess about a
    /// setting we cannot see, and only the ORDER of two indents matters here, which mixing tabs and
    /// spaces on adjacent lines would break either way.
    /// </summary>
    private static int Indent(string line)
    {
        var indent = 0;

        while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t'))
        {
            indent++;
        }

        return indent;
    }

    /// <summary>
    /// Moves the group one line later: the context row just past it takes the group's first line, and
    /// the line that context row occupied joins the group at the end.
    ///
    /// Sound because the caller has already checked the two lines are equal under the comparison keys -
    /// the row that becomes context is being paired with a line the diff considers identical to the one
    /// it was paired with before.
    /// </summary>
    private static void SlideDown(List<DiffLine> rows, int start, int end, DiffSide side)
    {
        var boundary = rows[end + 1];
        var leaving = rows[start];

        rows[start] = side == DiffSide.Left
            ? boundary with { LeftNumber = leaving.LeftNumber, LeftText = leaving.LeftText }
            : boundary with { RightNumber = leaving.RightNumber, RightText = leaving.RightText };

        rows[end + 1] = OneSided(boundary, side);
    }

    /// <summary>The mirror of <see cref="SlideDown"/>: the group moves one line earlier.</summary>
    private static void SlideUp(List<DiffLine> rows, int start, int end, DiffSide side)
    {
        var boundary = rows[start - 1];
        var leaving = rows[end];

        rows[end] = side == DiffSide.Left
            ? boundary with { LeftNumber = leaving.LeftNumber, LeftText = leaving.LeftText }
            : boundary with { RightNumber = leaving.RightNumber, RightText = leaving.RightText };

        rows[start - 1] = OneSided(boundary, side);
    }

    /// <summary>The context row reduced to just its line on <paramref name="side"/> - a filler opposite it.</summary>
    private static DiffLine OneSided(DiffLine row, DiffSide side) => side == DiffSide.Left
        ? new DiffLine(row.LeftNumber, row.LeftText, null, null, ChangeKind.Deleted)
        : new DiffLine(null, null, row.RightNumber, row.RightText, ChangeKind.Inserted);

    private static int NumberOf(DiffLine row, DiffSide side) =>
        (side == DiffSide.Left ? row.LeftNumber : row.RightNumber) ?? 0;

    private static string KeyOf(DiffLine row, DiffSide side, IReadOnlyList<string> keys)
    {
        var number = NumberOf(row, side);

        return number >= 1 && number <= keys.Count ? keys[number - 1] : string.Empty;
    }
}
