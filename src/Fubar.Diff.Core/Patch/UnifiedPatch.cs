using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Patch;

/// <summary>
/// Writes a comparison out as a unified diff - the format <c>git apply</c>, <c>patch</c> and every code
/// review tool on earth already understands.
///
/// The point is that a diff stops being something only this app can read. A patch can be pasted into a
/// review, attached to an issue, mailed to someone without the tool, or applied on another machine -
/// and none of that is possible while the answer lives only in two panes.
///
/// It is NOT the unified VIEW. That one shows a whole document with folds and is optimised for reading
/// on screen; this one carries a few lines of context around each change and is optimised for being
/// applied by a machine, where an extra line of context is a hunk that fails to apply.
/// </summary>
public static class UnifiedPatch
{
    /// <summary>
    /// How many unchanged lines surround each change. Three is what <c>diff -u</c> and git default to,
    /// and patches are exchanged with tools that expect it.
    /// </summary>
    public const int DefaultContext = 3;

    /// <summary>
    /// Renders the patch, or an empty string when there is nothing to say.
    ///
    /// Empty rather than headers-with-no-hunks: a patch file that applies nothing is a thing someone
    /// will try to apply, and "there were no changes" is better said by the absence of a file.
    /// </summary>
    /// <param name="result">The comparison.</param>
    /// <param name="leftLabel">The name for the original, conventionally prefixed <c>a/</c> by git.</param>
    /// <param name="rightLabel">The name for the modified file.</param>
    /// <param name="contextLines">Unchanged lines to keep around each change.</param>
    public static string Create(
        DiffResult result,
        string leftLabel,
        string rightLabel,
        int contextLines = DefaultContext)
    {
        if (result.Hunks.Count == 0)
        {
            return string.Empty;
        }

        var context = contextLines < 0 ? 0 : contextLines;
        var groups = GroupHunks(result, context);

        var patch = new StringBuilder();

        patch.Append("--- ").Append(leftLabel).Append('\n');
        patch.Append("+++ ").Append(rightLabel).Append('\n');

        foreach (var (from, to) in groups)
        {
            AppendGroup(patch, result, from, to);
        }

        return patch.ToString();
    }

    /// <summary>
    /// Expands each hunk by the context lines and merges the ranges that then overlap or touch.
    ///
    /// Merging matters: two changes four lines apart with three lines of context each would otherwise
    /// produce two hunks whose context overlaps, which is a patch that describes the same lines twice
    /// and does not apply. One hunk covering both is the correct - and shorter - answer.
    /// </summary>
    private static List<(int From, int To)> GroupHunks(DiffResult result, int context)
    {
        var groups = new List<(int From, int To)>();

        foreach (var hunk in result.Hunks)
        {
            var from = Math.Max(hunk.StartIndex - context, 0);
            var to = Math.Min(hunk.EndIndex + context, result.Lines.Count - 1);

            // Touching counts as overlapping: adjacent ranges with no gap between them are one run of
            // lines, and splitting them would emit a hunk header for nothing.
            if (groups.Count > 0 && from <= groups[^1].To + 1)
            {
                groups[^1] = (groups[^1].From, Math.Max(groups[^1].To, to));
            }
            else
            {
                groups.Add((from, to));
            }
        }

        return groups;
    }

    private static void AppendGroup(StringBuilder patch, DiffResult result, int from, int to)
    {
        var (oldStart, oldCount, newStart, newCount) = Measure(result, from, to);

        patch.Append("@@ -")
            .Append(Range(oldStart, oldCount))
            .Append(" +")
            .Append(Range(newStart, newCount))
            .Append(" @@\n");

        var i = from;
        var cursor = 0;

        while (i <= to)
        {
            while (cursor < result.Hunks.Count && result.Hunks[cursor].EndIndex < i)
            {
                cursor++;
            }

            if (cursor < result.Hunks.Count && i >= result.Hunks[cursor].StartIndex && i <= result.Hunks[cursor].EndIndex)
            {
                var hunk = result.Hunks[cursor];

                // Removals then additions, which is what a patch looks like - and within a hunk there
                // is no context to separate them, so the grouping is exact rather than a rendering
                // choice.
                AppendSide(patch, result, hunk, DiffSide.Left, '-');
                AppendSide(patch, result, hunk, DiffSide.Right, '+');

                i = hunk.EndIndex + 1;
                continue;
            }

            // Context. A row an ignore rule downgraded may have text on one side only, so take
            // whichever side has it.
            var row = result.Lines[i];
            if ((row.LeftText ?? row.RightText) is { } text)
            {
                patch.Append(' ').Append(text).Append('\n');
            }

            i++;
        }
    }

    private static void AppendSide(StringBuilder patch, DiffResult result, DiffHunk hunk, DiffSide side, char marker)
    {
        var last = Math.Min(hunk.EndIndex, result.Lines.Count - 1);

        for (var i = Math.Max(hunk.StartIndex, 0); i <= last; i++)
        {
            var row = result.Lines[i];
            var text = side == DiffSide.Left ? row.LeftText : row.RightText;

            if (text is not null)
            {
                patch.Append(marker).Append(text).Append('\n');
            }
        }
    }

    /// <summary>
    /// The hunk header's line ranges, taken from the rows' own file line numbers rather than counted -
    /// a filler row has no line number, and counting rows would drift from the file by one per
    /// insertion.
    /// </summary>
    private static (int OldStart, int OldCount, int NewStart, int NewCount) Measure(
        DiffResult result,
        int from,
        int to)
    {
        int oldStart = 0, oldCount = 0, newStart = 0, newCount = 0;

        for (var i = from; i <= to; i++)
        {
            var row = result.Lines[i];

            if (row.LeftNumber is { } left)
            {
                if (oldCount == 0)
                {
                    oldStart = left;
                }

                oldCount++;
            }

            if (row.RightNumber is { } right)
            {
                if (newCount == 0)
                {
                    newStart = right;
                }

                newCount++;
            }
        }

        return (oldStart, oldCount, newStart, newCount);
    }

    /// <summary>
    /// A hunk header's range. An empty range is written as <c>0,0</c> - the convention for "this file
    /// has nothing here", which is how a patch expresses a file that was created or deleted whole. A
    /// single line drops the count, as every other tool writes it.
    /// </summary>
    private static string Range(int start, int count) => count switch
    {
        0 => "0,0",
        1 => start.ToString(CultureInfo.InvariantCulture),
        _ => string.Create(CultureInfo.InvariantCulture, $"{start},{count}"),
    };
}
