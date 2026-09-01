using System;
using System.Collections.Generic;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Rendering;

/// <summary>
/// Presents a byte comparison as an ordinary <see cref="DiffResult"/> of hex-dump rows.
///
/// This is the whole reason binary comparison cost so little to add. A hex dump is already a list of
/// lines, and two dumps of the same offsets already line up row for row - so expressing one as the
/// same shape everything else consumes means the side-by-side editors, scroll sync, the change tints,
/// the diff map, F7/F8 navigation and the collapse-unchanged folds all work on it without knowing that
/// anything unusual happened. A bespoke hex control would have had to earn every one of those back.
///
/// Alignment here is POSITIONAL, and that is not a shortcut - it is the only honest answer. Byte
/// offsets are the coordinate a binary file has; matching a row of bytes against a similar-looking row
/// somewhere else would be inventing a correspondence the format does not have.
/// </summary>
public static class HexDiff
{
    /// <summary>
    /// How much of the two files to lay out.
    ///
    /// Capped, unlike a text comparison, because the row count here is a property of the FILE SIZE
    /// rather than of its content: a 64 MB file is four million hex rows, every one of them
    /// materialised as a string on both sides. Two megabytes is far more than anyone reads by hand and
    /// still bounds the work at a fixed cost. What is beyond the cap is described in the summary rather
    /// than drawn - the comparison itself always covers the whole file, only the view is trimmed.
    /// </summary>
    public const int MaxBytesShown = 2 * 1024 * 1024;

    /// <summary>Builds the aligned hex rows for a byte comparison.</summary>
    public static DiffResult Build(BinaryComparison comparison)
    {
        var left = comparison.Left.Bytes.Span;
        var right = comparison.Right.Bytes.Span;

        var shown = Math.Min(Math.Max(left.Length, right.Length), MaxBytesShown);
        var rows = new List<DiffLine>((shown / HexDump.BytesPerLine) + 1);

        for (var offset = 0; offset < shown; offset += HexDump.BytesPerLine)
        {
            var leftRow = RowAt(left, offset);
            var rightRow = RowAt(right, offset);

            if (leftRow is null && rightRow is null)
            {
                continue;
            }

            var number = (offset / HexDump.BytesPerLine) + 1;

            rows.Add(new DiffLine(
                leftRow is null ? null : number,
                leftRow,
                rightRow is null ? null : number,
                rightRow,
                KindOf(leftRow, rightRow)));
        }

        return DiffResult.Create(rows);
    }

    /// <summary>The hex line at an offset, or null when this file has already ended.</summary>
    private static string? RowAt(ReadOnlySpan<byte> bytes, int offset) => HexDump.Line(bytes, offset);

    /// <summary>
    /// One side ending before the other is a deletion or an insertion; two rows that both exist are
    /// unchanged or modified. There is no fourth case: the offsets are the same on both sides by
    /// construction.
    /// </summary>
    private static ChangeKind KindOf(string? left, string? right)
    {
        if (left is null)
        {
            return ChangeKind.Inserted;
        }

        if (right is null)
        {
            return ChangeKind.Deleted;
        }

        return string.Equals(left, right, StringComparison.Ordinal)
            ? ChangeKind.Unchanged
            : ChangeKind.Modified;
    }
}
