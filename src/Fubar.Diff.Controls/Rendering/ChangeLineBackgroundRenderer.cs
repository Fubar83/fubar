using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Controls.Rendering;

/// <summary>
/// Paints the whole-line change tint behind the text - green for inserted, red for deleted, amber for
/// modified, and a dimmed band for filler rows.
///
/// Draws on <see cref="KnownLayer.Background"/> so the tint sits under both the text and the selection
/// highlight; painting above would wash out the selection and make selected text unreadable.
///
/// Only the visible lines are drawn (AvaloniaEdit hands us exactly those), so cost is proportional to
/// the viewport rather than the document - which is the whole reason for moving to a real editor.
/// </summary>
internal sealed class ChangeLineBackgroundRenderer : IBackgroundRenderer
{
    private readonly Avalonia.StyledElement _host;
    private IReadOnlyList<AlignedLine> _lines = [];

    public ChangeLineBackgroundRenderer(Avalonia.StyledElement host) => _host = host;

    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>Swaps in the metadata for a new comparison. The caller must redraw the text view.</summary>
    public void SetLines(IReadOnlyList<AlignedLine> lines) => _lines = lines;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_lines.Count == 0)
        {
            return;
        }

        textView.EnsureVisualLines();

        foreach (var visualLine in textView.VisualLines)
        {
            // AvaloniaEdit line numbers are 1-based; AlignedText is indexed from 0. A document can
            // briefly be longer than the metadata while a new comparison is being applied, so guard
            // rather than trusting them to be in step.
            var index = visualLine.FirstDocumentLine.LineNumber - 1;
            if (index < 0 || index >= _lines.Count)
            {
                continue;
            }

            if (DiffLineColors.LineBackground(_host, _lines[index].Kind) is not { } brush)
            {
                continue;
            }

            // Full viewport width, not just the text extent: a tint that stops at the end of the line
            // makes the changed block look ragged and is much harder to scan down.
            var top = visualLine.VisualTop - textView.VerticalOffset;
            drawingContext.FillRectangle(
                brush,
                new Rect(0, top, textView.Bounds.Width, visualLine.Height));
        }
    }
}
