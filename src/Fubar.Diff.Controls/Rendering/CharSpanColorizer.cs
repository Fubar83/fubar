using System.Collections.Generic;
using Avalonia.Controls;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Fubar.Diff.Core.Models;
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
    private bool _emphasized;
    private int _currentStart = -1;
    private int _currentEnd = -1;

    public CharSpanColorizer(Avalonia.StyledElement host) => _host = host;

    /// <summary>Swaps in the metadata for a new comparison. The caller must redraw the text view.</summary>
    public void SetLines(IReadOnlyList<AlignedLine> lines) => _lines = lines;

    /// <summary>Whether this pane is a close-up (DiffDetailPane), where the highlight should carry more weight.</summary>
    public void SetEmphasized(bool value) => _emphasized = value;

    /// <summary>The current hunk's row range - see <see cref="ChangeLineBackgroundRenderer.SetCurrentRange"/>.</summary>
    public void SetCurrentRange(int startIndex, int endIndex)
    {
        _currentStart = startIndex;
        _currentEnd = endIndex;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var index = line.LineNumber - 1;
        if (index < 0 || index >= _lines.Count)
        {
            return;
        }

        var meta = _lines[index];
        var lineLength = line.Length;
        var emphasis = Emphasis(index);
        var spans = meta.Spans;

        // A whole inserted/deleted row carries no per-character spans in the main view - the full-line
        // tint already says "this entire row is the difference", so picking out characters within it
        // would be noise (see FileComparisonServiceTests.Only_modified_rows_get_inline_spans). The
        // close-up drops that full-line tint, though (see ChangeLineBackgroundRenderer), so it needs
        // ITS OWN way to say "this text is the difference" - highlighting the row's own text here,
        // rather than reaching back into the row-tinting renderer this pane just turned off.
        if (spans.Count == 0)
        {
            if (!_emphasized || lineLength == 0 || meta.Kind is not (ChangeKind.Inserted or ChangeKind.Deleted))
            {
                return;
            }

            spans = [new CharSpan(0, lineLength, meta.Kind)];
        }

        foreach (var span in spans)
        {
            if (DiffLineColors.SpanBackground(_host, span.Kind, emphasis) is not { } brush)
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

    private DiffEmphasis Emphasis(int index)
    {
        if (_emphasized)
        {
            return DiffEmphasis.Emphasized;
        }

        return _currentStart >= 0 && (index < _currentStart || index > _currentEnd)
            ? DiffEmphasis.Faded
            : DiffEmphasis.Normal;
    }
}
