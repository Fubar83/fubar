using System.Collections.Generic;
using Avalonia.Controls;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Controls.Rendering;

/// <summary>
/// Highlights the individual words that changed within a modified line, on top of the line tint.
///
/// This is what makes a one-character change readable: without it, a modified line is a uniform band
/// and the eye has to scan the whole row to find what moved.
/// </summary>
internal sealed class CharSpanColorizer : DocumentColorizingTransformer
{
    private readonly Avalonia.StyledElement _host;
    private IReadOnlyList<AlignedLine> _lines = [];

    public CharSpanColorizer(Avalonia.StyledElement host) => _host = host;

    /// <summary>Swaps in the metadata for a new comparison. The caller must redraw the text view.</summary>
    public void SetLines(IReadOnlyList<AlignedLine> lines) => _lines = lines;

    protected override void ColorizeLine(DocumentLine line)
    {
        var index = line.LineNumber - 1;
        if (index < 0 || index >= _lines.Count)
        {
            return;
        }

        var meta = _lines[index];
        if (meta.Spans.Count == 0)
        {
            return;
        }

        var lineLength = line.Length;

        foreach (var span in meta.Spans)
        {
            if (DiffLineColors.SpanBackground(_host, span.Kind) is not { } brush)
            {
                continue;
            }

            // Spans were computed against the document line's text, but clamp anyway: a stale
            // metadata list arriving a frame before its document would otherwise throw inside the
            // render pass, which takes the whole window down rather than just looking wrong.
            var start = span.Start;
            var end = span.End;
            if (start >= lineLength)
            {
                continue;
            }

            if (end > lineLength)
            {
                end = lineLength;
            }

            ChangeLinePart(
                line.Offset + start,
                line.Offset + end,
                element => element.TextRunProperties.SetBackgroundBrush(brush));
        }
    }
}
