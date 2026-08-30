using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fubar.Diff.Core.Rendering;

/// <summary>
/// Formats a stretch of bytes the way every hex editor does: offset, sixteen bytes, then those bytes
/// as characters.
///
/// The character column is not decoration - it is what makes a binary file readable at all. Version
/// strings, embedded paths, magic numbers and format markers are text sitting inside the file, and
/// they are usually the thing that explains the difference.
/// </summary>
public static class HexDump
{
    /// <summary>Bytes per line. Sixteen, as every hex editor since the 1970s.</summary>
    public const int BytesPerLine = 16;

    /// <summary>
    /// Formats up to <paramref name="lineCount"/> lines, starting at the line CONTAINING
    /// <paramref name="offset"/>.
    ///
    /// Snapped down to a line boundary rather than started at the offset itself, so the two sides' dumps
    /// line up column for column: a difference at byte 0x1007 has to appear under the same column in
    /// both panes, which it only does when both start at 0x1000.
    /// </summary>
    public static IReadOnlyList<string> Build(ReadOnlySpan<byte> bytes, int offset, int lineCount)
    {
        if (bytes.Length == 0 || lineCount <= 0)
        {
            return [];
        }

        var start = Math.Max(0, offset - (offset % BytesPerLine));
        var lines = new List<string>(lineCount);

        for (var line = 0; line < lineCount; line++)
        {
            var from = start + (line * BytesPerLine);
            if (from >= bytes.Length)
            {
                break;
            }

            lines.Add(FormatLine(bytes[from..Math.Min(from + BytesPerLine, bytes.Length)], from));
        }

        return lines;
    }

    /// <summary>
    /// The single line covering <paramref name="offset"/>, snapped down to a line boundary exactly as
    /// <see cref="Build"/> snaps its first. Null when the offset is past the end of the content.
    /// </summary>
    public static string? Line(ReadOnlySpan<byte> bytes, int offset)
    {
        var from = Math.Max(0, offset - (offset % BytesPerLine));

        return from >= bytes.Length
            ? null
            : FormatLine(bytes[from..Math.Min(from + BytesPerLine, bytes.Length)], from);
    }

    /// <summary>The 0-based hex-dump line an offset falls on.</summary>
    public static int LineOf(int offset) => Math.Max(0, offset) / BytesPerLine;

    private static string FormatLine(ReadOnlySpan<byte> row, int offset)
    {
        var builder = new StringBuilder(8 + 2 + (BytesPerLine * 3) + 1 + BytesPerLine);

        builder.Append(offset.ToString("x8", CultureInfo.InvariantCulture)).Append("  ");

        for (var i = 0; i < BytesPerLine; i++)
        {
            if (i < row.Length)
            {
                builder.Append(row[i].ToString("x2", CultureInfo.InvariantCulture)).Append(' ');
            }
            else
            {
                // Padded rather than truncated, so a short final row keeps its character column exactly
                // where every row above put theirs.
                builder.Append("   ");
            }
        }

        builder.Append(' ');

        foreach (var b in row)
        {
            // Printable ASCII only. Anything else becomes a dot - the alternative is control characters
            // reaching a text renderer, where a lone CR or a bidi override rearranges the line and the
            // columns stop meaning anything.
            builder.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }

        return builder.ToString();
    }
}
