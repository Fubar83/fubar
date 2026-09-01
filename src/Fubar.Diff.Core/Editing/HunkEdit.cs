using System.Collections.Generic;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Editing;

/// <summary>
/// Resolving one difference by REWRITING the file, rather than by recording a decision about it.
///
/// This is what "take left" means once the panes are editable, and it is a smaller idea than the
/// machinery it replaces. <c>MergeState</c> + <c>MergedDocument</c> keep a list of per-hunk choices and
/// build the merged text at save time, which means the choices are invisible until then, are keyed by
/// hunk INDEX (so a fresh comparison silently renumbers them), and cannot be undone with Ctrl+Z
/// because nothing in the document changed.
///
/// Applying the change immediately fixes all three at once. There is nothing pending to renumber, the
/// diff shrinks in front of the user as they resolve, and taking a side lands on the editor's own undo
/// stack beside everything they typed. The three-way merge still needs the other model - it resolves
/// REGIONS across three documents and has a defined answer for the ones nobody decided - so
/// <c>ThreeWayMergedDocument</c> stays exactly as it is.
/// </summary>
public static class HunkEdit
{
    /// <summary>
    /// The target file's lines after resolving one hunk in favour of one side.
    ///
    /// Returns the lines unchanged when there is nothing to do - taking the side you are already
    /// looking at.
    /// </summary>
    /// <param name="result">The comparison the hunk belongs to.</param>
    /// <param name="hunk">The difference being resolved.</param>
    /// <param name="take">The side whose version wins.</param>
    /// <param name="target">The side being written - the file that changes.</param>
    /// <param name="targetLines">The target file's current lines.</param>
    public static IReadOnlyList<string> Resolve(
        DiffResult result,
        DiffHunk hunk,
        DiffSide take,
        DiffSide target,
        IReadOnlyList<string> targetLines)
    {
        if (take == target)
        {
            return targetLines;
        }

        var rows = result.Lines;

        var first = int.MaxValue;
        var last = -1;
        var replacement = new List<string>();

        for (var i = hunk.StartIndex; i <= hunk.EndIndex && i < rows.Count; i++)
        {
            if (i < 0)
            {
                continue;
            }

            // Which of the target file's lines this hunk covers. A row that is filler on the target
            // side covers none - the file simply has nothing there - which is what makes taking the
            // other side of an insertion actually remove lines rather than blank them.
            if (NumberOn(rows[i], target) is { } number)
            {
                if (number < first)
                {
                    first = number;
                }

                if (number > last)
                {
                    last = number;
                }
            }

            if (TextOn(rows[i], take) is { } text)
            {
                replacement.Add(text);
            }
        }

        // The hunk is entirely filler on the target side: the other file has lines here and this one
        // has none, so there is nothing to replace and the text is INSERTED. Where matters - it goes
        // after the last real line above the hunk, which is the only position that keeps the rest of
        // the file in order.
        var start = last < 0 ? InsertionPoint(rows, hunk.StartIndex, target) : first - 1;
        var removed = last < 0 ? 0 : last - first + 1;

        var lines = new List<string>(targetLines.Count - removed + replacement.Count);

        for (var i = 0; i < start && i < targetLines.Count; i++)
        {
            lines.Add(targetLines[i]);
        }

        lines.AddRange(replacement);

        for (var i = start + removed; i < targetLines.Count; i++)
        {
            lines.Add(targetLines[i]);
        }

        return lines;
    }

    /// <summary>
    /// The 0-based index to insert at, for a hunk the target side has no lines in: just past the last
    /// line it does have above the hunk, or the very start of the file when there is none.
    /// </summary>
    private static int InsertionPoint(IReadOnlyList<DiffLine> rows, int hunkStart, DiffSide target)
    {
        for (var i = hunkStart - 1; i >= 0; i--)
        {
            if (NumberOn(rows[i], target) is { } number)
            {
                return number;
            }
        }

        return 0;
    }

    private static int? NumberOn(DiffLine row, DiffSide side) =>
        side == DiffSide.Left ? row.LeftNumber : row.RightNumber;

    private static string? TextOn(DiffLine row, DiffSide side) =>
        side == DiffSide.Left ? row.LeftText : row.RightText;
}
