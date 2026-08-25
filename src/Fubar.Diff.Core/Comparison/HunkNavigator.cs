using System;
using System.Collections.Generic;

namespace Fubar.Diff.Core.Comparison;

using Fubar.Diff.Core.Models;

/// <summary>
/// Domain policy for "jump to the next/previous change". Pure and total: it never throws, and with no
/// hunks it reports no target. Lives in Core rather than a view model so the wrap-around and
/// boundary rules are testable without a UI - these are exactly the rules that get subtly wrong.
/// </summary>
public static class HunkNavigator
{
    /// <summary>
    /// The hunk index to move to from <paramref name="currentIndex"/>, wrapping past the end.
    /// Returns null when there is nothing to navigate to.
    /// </summary>
    /// <param name="hunks">The hunks in document order.</param>
    /// <param name="currentIndex">The current hunk index, or -1 when none is selected.</param>
    public static int? Next(IReadOnlyList<DiffHunk> hunks, int currentIndex) =>
        hunks.Count == 0 ? null : (currentIndex + 1) % hunks.Count;

    /// <summary>
    /// The previous hunk index, wrapping past the start. Returns null when there is nothing to
    /// navigate to. From "none selected" (-1) this lands on the LAST hunk, which is what a user
    /// pressing "previous" first expects.
    /// </summary>
    public static int? Previous(IReadOnlyList<DiffHunk> hunks, int currentIndex)
    {
        if (hunks.Count == 0)
        {
            return null;
        }

        return currentIndex <= 0 ? hunks.Count - 1 : currentIndex - 1;
    }

    /// <summary>
    /// Which lines of the two ORIGINAL files a hunk covers, for captioning it.
    ///
    /// Not simply the hunk's row indices: those address the aligned view, which contains filler rows
    /// that exist in neither file. A wholly inserted hunk covers no left-hand lines at all, and
    /// reporting the filler rows' positions as line numbers would name lines the user cannot find.
    /// Either side is therefore null when that side contributes nothing.
    /// </summary>
    public static HunkRange RangeOf(IReadOnlyList<DiffLine> lines, DiffHunk hunk)
    {
        int? leftStart = null, leftEnd = null, rightStart = null, rightEnd = null;

        var last = Math.Min(hunk.EndIndex, lines.Count - 1);
        for (var i = Math.Max(hunk.StartIndex, 0); i <= last; i++)
        {
            if (lines[i].LeftNumber is { } left)
            {
                leftStart ??= left;
                leftEnd = left;
            }

            if (lines[i].RightNumber is { } right)
            {
                rightStart ??= right;
                rightEnd = right;
            }
        }

        return new HunkRange(leftStart, leftEnd, rightStart, rightEnd);
    }

    /// <summary>
    /// The index of the hunk containing <paramref name="lineIndex"/>, or -1 if that row is unchanged
    /// context. Used to keep the navigator in step when the user scrolls or clicks instead.
    /// </summary>
    public static int IndexOfHunkContaining(IReadOnlyList<DiffHunk> hunks, int lineIndex)
    {
        for (var i = 0; i < hunks.Count; i++)
        {
            if (lineIndex >= hunks[i].StartIndex && lineIndex <= hunks[i].EndIndex)
            {
                return i;
            }
        }

        return -1;
    }
}
