using System;
using Avalonia;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Controls.Rendering;

/// <summary>
/// Highlights the EXACT characters a <see cref="SourceSpan"/> covers, rather than a full-width band
/// across every line it touches.
///
/// Used by <c>RawJsonPane</c> in the Json close-up (JsonDetailPane) instead of
/// <see cref="CurrentHunkRenderer"/>: a change there is usually a single value, and a full-width band
/// around it reads as "something on this line changed" where highlighting the value itself is both
/// more precise and, being a close-up whose only job is showing this one change, allowed to run at a
/// stronger opacity than a band that has to sit on top of the rest of the line's own colour.
/// </summary>
internal sealed class SpanTextColorizer : DocumentColorizingTransformer
{
    private readonly StyledElement _host;
    private SourceSpan? _span;

    public SpanTextColorizer(StyledElement host) => _host = host;

    /// <summary>Sets the span to highlight, or clears it with null. Caller must redraw.</summary>
    public void SetSpan(SourceSpan? span) => _span = span;

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_span is not { IsKnown: true } span)
        {
            return;
        }

        var lineNumber = line.LineNumber;
        if (lineNumber < span.StartLine || lineNumber > span.EndLine)
        {
            return;
        }

        // Columns are 1-based and only meaningful on the span's own first/last line - a line the span
        // merely passes through (a multi-line object or array) is covered in full.
        var startColumn = lineNumber == span.StartLine ? span.StartColumn : 1;
        var endColumn = lineNumber == span.EndLine ? span.EndColumn : line.Length + 1;

        var start = Math.Clamp(startColumn - 1, 0, line.Length);
        var end = Math.Clamp(endColumn - 1, start, line.Length);

        if (end <= start || DiffLineColors.CurrentSpanBackground(_host) is not { } brush)
        {
            return;
        }

        ChangeLinePart(
            line.Offset + start,
            line.Offset + end,
            element => element.TextRunProperties.SetBackgroundBrush(brush));
    }
}
