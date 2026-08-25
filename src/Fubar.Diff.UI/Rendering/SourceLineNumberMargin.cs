using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.UI.Rendering;

/// <summary>
/// A line-number gutter that shows each line's number in the ORIGINAL file.
///
/// AvaloniaEdit's built-in <c>ShowLineNumbers</c> numbers the lines it is displaying, which is wrong
/// here: the aligned document contains filler lines, so every number after the first insertion would
/// be off by the number of fillers above it, and none of them would match the file on disk. Filler
/// rows get no number at all, which is also what tells the reader that side has nothing there.
/// </summary>
internal sealed class SourceLineNumberMargin : AbstractMargin
{
    private IReadOnlyList<AlignedLine> _lines = [];
    private Typeface _typeface = new(FontFamily.Default);
    private double _fontSize = 12;
    private IBrush _foreground = Brushes.Gray;

    /// <summary>Swaps in the metadata for a new comparison and re-measures the gutter.</summary>
    public void SetLines(IReadOnlyList<AlignedLine> lines)
    {
        _lines = lines;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Matches the gutter's text to the editor's, so the numbers align with the code rows.</summary>
    public void SetTextStyle(Typeface typeface, double fontSize, IBrush foreground)
    {
        _typeface = typeface;
        _fontSize = fontSize;
        _foreground = foreground;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Size to the widest number the document can produce, so the gutter does not resize (and shift
        // the text) as the user scrolls from line 9 to line 10.
        var widest = FormatText(MaxSourceNumber().ToString(CultureInfo.InvariantCulture));

        return new Size(widest.Width + Padding * 2, 0);
    }

    public override void Render(DrawingContext context)
    {
        if (TextView is not { VisualLinesValid: true } textView || _lines.Count == 0)
        {
            return;
        }

        foreach (var visualLine in textView.VisualLines)
        {
            var index = visualLine.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= _lines.Count)
            {
                continue;
            }

            // No number for a filler: the point is that this side has no such line.
            if (_lines[index].SourceNumber is not { } number)
            {
                continue;
            }

            var text = FormatText(number.ToString(CultureInfo.InvariantCulture));
            var top = visualLine.VisualTop - textView.VerticalOffset;

            // Right-aligned, the way every editor numbers lines - ragged left edges are much harder
            // to scan than ragged right ones.
            context.DrawText(text, new Point(Bounds.Width - Padding - text.Width, top));
        }
    }

    private const double Padding = 6;

    private int MaxSourceNumber()
    {
        var max = 0;
        foreach (var line in _lines)
        {
            if (line.SourceNumber is { } number && number > max)
            {
                max = number;
            }
        }

        // Never zero: an empty or all-filler side still needs a sensible gutter width.
        return Math.Max(max, 99);
    }

    private FormattedText FormatText(string value) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, _foreground);
}
