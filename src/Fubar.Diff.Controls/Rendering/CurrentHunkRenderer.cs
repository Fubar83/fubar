using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace Fubar.Diff.Controls.Rendering;

/// <summary>
/// Marks the hunk the user is currently on: an accent bar down its left edge, a hairline boxing it
/// in, and a light wash over its rows.
///
/// Change tint alone cannot do this job. In a file where a hundred rows are already tinted, "which
/// one did F8 just take me to?" is unanswerable from colour density - so the current hunk gets shape
/// (a bar and a box) rather than merely more colour. That also keeps it legible for anyone who cannot
/// separate the amber wash from the change tints underneath.
///
/// Registered AFTER <see cref="ChangeLineBackgroundRenderer"/> on the same layer: background
/// renderers paint in registration order, so this lands on top of the change tint and under the text.
/// </summary>
internal sealed class CurrentHunkRenderer : IBackgroundRenderer
{
    private const double AccentBarWidth = 3.0;

    private readonly StyledElement _host;

    /// <summary>Inclusive row range of the current hunk, or a negative start when none is selected.</summary>
    private int _startIndex = -1;
    private int _endIndex = -1;

    public CurrentHunkRenderer(StyledElement host) => _host = host;

    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>Sets the highlighted range. Pass a negative start to clear it. Caller must redraw.</summary>
    public void SetRange(int startIndex, int endIndex)
    {
        _startIndex = startIndex;
        _endIndex = endIndex;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_startIndex < 0 || _endIndex < _startIndex)
        {
            return;
        }

        textView.EnsureVisualLines();

        var wash = DiffLineColors.CurrentHunkWash(_host);
        var accent = DiffLineColors.CurrentHunkAccent(_host);
        var outline = DiffLineColors.CurrentHunkOutline(_host);

        // The hunk is usually taller than one visual line, and only part of it may be on screen. Track
        // the drawn extent so the box is closed only on the edges that are genuinely the hunk's ends -
        // drawing a top border on a hunk that starts above the viewport would read as a boundary that
        // is not there.
        double? top = null;
        double? bottom = null;
        var sawFirstRow = false;
        var sawLastRow = false;

        foreach (var visualLine in textView.VisualLines)
        {
            var index = visualLine.FirstDocumentLine.LineNumber - 1;
            if (index < _startIndex || index > _endIndex)
            {
                continue;
            }

            var y = visualLine.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, y, textView.Bounds.Width, visualLine.Height);

            if (wash is not null)
            {
                drawingContext.FillRectangle(wash, rect);
            }

            if (accent is not null)
            {
                drawingContext.FillRectangle(accent, new Rect(0, y, AccentBarWidth, visualLine.Height));
            }

            top ??= y;
            bottom = y + visualLine.Height;
            sawFirstRow |= index == _startIndex;
            sawLastRow |= index == _endIndex;
        }

        if (outline is null || top is not { } t || bottom is not { } b)
        {
            return;
        }

        var pen = new Pen(outline, 1);
        var width = textView.Bounds.Width;

        // Snapped to the half-pixel so a 1px line renders crisp rather than as a 2px blur.
        if (sawFirstRow)
        {
            drawingContext.DrawLine(pen, new Point(0, Snap(t)), new Point(width, Snap(t)));
        }

        if (sawLastRow)
        {
            drawingContext.DrawLine(pen, new Point(0, Snap(b)), new Point(width, Snap(b)));
        }
    }

    private static double Snap(double value) => System.Math.Floor(value) + 0.5;
}
