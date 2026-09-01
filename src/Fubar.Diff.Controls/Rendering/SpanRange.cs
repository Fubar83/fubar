using System;
using AvaloniaEdit.Document;
using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Controls.Rendering;

/// <summary>
/// Turns a <see cref="SourceSpan"/> into the character range it covers on one document line.
///
/// Shared by the two colourizers that mark JSON spans (<see cref="SpanTextColorizer"/> for the one
/// change a close-up is about, <see cref="JsonChangeSpanColorizer"/> for every change in a document)
/// because getting it subtly wrong in one of them would show up as a highlight that is a character
/// out on multi-line objects, which is exactly the kind of thing nobody notices in a diff.
/// </summary>
internal static class SpanRange
{
    /// <summary>
    /// The half-open character range within <paramref name="line"/> that <paramref name="span"/>
    /// covers, or null when the line is outside the span or the range would be empty.
    ///
    /// Offsets are relative to the line, not the document. Columns are 1-based and only meaningful on
    /// the span's own first and last lines - a line the span merely passes through (the middle of a
    /// multi-line object or array) is covered in full. Everything is clamped rather than trusted: a
    /// metadata list can arrive a frame before the document it describes, and an out-of-range offset
    /// inside a render pass takes the window down rather than merely looking wrong.
    /// </summary>
    public static (int Start, int End)? Within(DocumentLine line, SourceSpan span)
    {
        if (!span.IsKnown)
        {
            return null;
        }

        var lineNumber = line.LineNumber;
        if (lineNumber < span.StartLine || lineNumber > span.EndLine)
        {
            return null;
        }

        var startColumn = lineNumber == span.StartLine ? span.StartColumn : 1;
        var endColumn = lineNumber == span.EndLine ? span.EndColumn : line.Length + 1;

        var start = Math.Clamp(startColumn - 1, 0, line.Length);
        var end = Math.Clamp(endColumn - 1, start, line.Length);

        return end > start ? (start, end) : null;
    }
}
