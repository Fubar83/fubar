using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Fubar.Diff.Controls.Rendering;

/// <summary>
/// Centres a line in the viewport rather than merely scrolling it into view. Navigating to a
/// difference should put it where the eye already rests - the middle of the pane - not wherever
/// AvaloniaEdit's own <c>ScrollToLine</c> happens to stop, which is only guaranteed to be on-screen,
/// often hugging whichever edge it approached from.
/// </summary>
internal static class EditorScroll
{
    /// <summary>
    /// Scrolls a pane sideways to an absolute offset.
    ///
    /// Goes through <see cref="IScrollable"/> on the TEXT VIEW, and that detail cost a diagnostic
    /// session to find. <c>TextEditor.ScrollToHorizontalOffset</c> looks like the obvious counterpart
    /// to <c>ScrollToVerticalOffset</c> and is silently useless here: AvaloniaEdit's TextView is an
    /// <c>ILogicalScrollable</c> that scrolls ITSELF, so the ScrollViewer in the editor's template
    /// never moves - its <c>Offset.X</c> reads 0.0 on a pane visibly scrolled to 809.8 - and writing
    /// to it changes nothing anyone can see. The vertical twin works only because AvaloniaEdit routes
    /// it to the text view internally.
    ///
    /// Measured before being believed: the target's extent was never the problem (1270 wide against a
    /// 450 viewport, so the offset asked for was always reachable). The write was going somewhere
    /// that does not scroll.
    /// </summary>
    public static void ScrollHorizontallyTo(TextView textView, double offset)
    {
        if (textView is not IScrollable scrollable)
        {
            return;
        }

        // Clamped by the view itself: a side whose longest line is shorter simply stops at its own
        // end rather than the pair jamming or throwing.
        scrollable.Offset = new Vector(Math.Max(0, offset), scrollable.Offset.Y);
    }

    /// <summary>
    /// Brings the changed CHARACTERS of a line into view sideways, if they are not already.
    ///
    /// <para>Centring vertically is right - the eye rests in the middle of the pane - and centring
    /// horizontally is not. Horizontal position carries meaning: indentation is how code shows its
    /// structure, and a pane yanked sideways on every navigation loses that for every difference that
    /// did not need it. So this scrolls the MINIMUM required, and only when the span is actually out of
    /// view.</para>
    ///
    /// <para>A change with no columns to point at - a wholly inserted or deleted line, which carries no
    /// character spans because the entire row is the difference - scrolls back to the left margin
    /// instead. That IS where such a change starts, and leaving the pane parked to the right after
    /// following a long line would hide the next difference behind the gutter.</para>
    /// </summary>
    /// <param name="startColumn">1-based column of the first changed character, or 0 for none.</param>
    /// <param name="endColumn">1-based column just past the last changed character.</param>
    public static void RevealColumns(
        TextEditor editor, TextView textView, int lineNumber, int startColumn, int endColumn)
    {
        if (textView is not IScrollable scrollable || textView.Bounds.Width <= 0)
        {
            return;
        }

        var viewportWidth = textView.Bounds.Width;
        var offset = scrollable.Offset.X;

        // No columns: the whole line is the change, so home.
        if (startColumn <= 0)
        {
            if (offset > 0)
            {
                ScrollHorizontallyTo(textView, 0);
            }

            return;
        }

        var line = editor.Document?.GetLineByNumber(lineNumber);
        if (line is null)
        {
            return;
        }

        // Clamped to the line: a span is computed against the display text, and a stale or
        // out-of-range column would otherwise ask AvaloniaEdit for a position that does not exist.
        var length = line.Length;
        var from = Math.Clamp(startColumn, 1, length + 1);
        var to = Math.Clamp(endColumn, from, length + 1);

        var left = XOf(textView, lineNumber, from);
        var right = XOf(textView, lineNumber, to);

        if (double.IsNaN(left) || double.IsNaN(right))
        {
            return;
        }

        // A margin so the change does not sit flush against an edge, where it reads as cut off.
        const double Margin = 48;

        if (left < offset + Margin)
        {
            ScrollHorizontallyTo(textView, Math.Max(0, left - Margin));
            return;
        }

        if (right > offset + viewportWidth - Margin)
        {
            // Prefer showing the START of a span too wide to fit: reading begins there.
            var wanted = right - viewportWidth + Margin;
            ScrollHorizontallyTo(textView, Math.Min(wanted, Math.Max(0, left - Margin)));
        }
    }

    private static double XOf(TextView textView, int lineNumber, int column)
    {
        try
        {
            return textView
                .GetVisualPosition(new TextViewPosition(lineNumber, column), VisualYPosition.LineTop)
                .X;
        }
        catch (ArgumentOutOfRangeException)
        {
            // The view has not built that visual line yet. Not knowing where a column is is a reason to
            // leave the pane alone, never to throw out of a navigation.
            return double.NaN;
        }
    }

    public static void CenterOnLine(TextEditor editor, TextView textView, int lineNumber)
    {
        var lineHeight = textView.DefaultLineHeight;

        if (lineHeight <= 0 || textView.Bounds.Height <= 0)
        {
            // Not laid out yet - fall back to AvaloniaEdit's own "just make it visible" rather than
            // computing an offset against a viewport that has no size.
            editor.ScrollToLine(lineNumber);
            return;
        }

        // ScrollToLine FIRST: jumping straight to ScrollToVerticalOffset for a line the ScrollViewer
        // has not measured towards yet gets silently clamped back to the current extent, which only
        // covers whatever has actually been rendered so far. Asking AvaloniaEdit to make the line
        // visible first forces it to acknowledge the document really is that tall, so the follow-up
        // offset lands where asked instead of getting clamped to (near) zero.
        editor.ScrollToLine(lineNumber);

        // The line's real position, asked of the editor rather than computed as line x height. Lines
        // are only uniformly tall when nothing is folded and nothing wraps, and both of those are on
        // in the views that use this - a collapsed region above the target subtracts its rows, and a
        // wrapped line adds visual rows the arithmetic knows nothing about. Getting it wrong is silent:
        // the pane scrolls somewhere plausible and simply does not centre the difference.
        var visualTop = textView.GetVisualTopByDocumentLine(lineNumber);

        editor.ScrollToVerticalOffset(Math.Max(0, visualTop - ((textView.Bounds.Height - lineHeight) / 2)));
    }
}
