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
        if (_span is not { } span
            || SpanRange.Within(line, span) is not { } range
            || DiffLineColors.CurrentSpanBackground(_host) is not { } brush)
        {
            return;
        }

        ChangeLinePart(
            line.Offset + range.Start,
            line.Offset + range.End,
            element => element.TextRunProperties.SetBackgroundBrush(brush));
    }
}
