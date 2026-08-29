using System;
using AvaloniaEdit;
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

        var target = ((lineNumber - 1) * lineHeight) - ((textView.Bounds.Height - lineHeight) / 2);
        editor.ScrollToVerticalOffset(Math.Max(0, target));
    }
}
