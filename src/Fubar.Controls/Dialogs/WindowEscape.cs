using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;

namespace Fubar.Controls;

/// <summary>
/// What Escape does in a window that is not the main one.
/// </summary>
/// <remarks>
/// <para>Two stages, because Escape means two things and the inner one comes first. In a text field it
/// means "I have finished with this field" - so it moves focus out and stops there. Anywhere else it
/// means "I have finished with this window", and closes it. A single-stage version closes the window
/// out from under someone who was only trying to leave a filter box, losing whatever else they had set
/// up in it.</para>
/// <para>Here rather than copied into each window: it was in <c>DiffPreviewDialog</c> alone, so the run
/// and comparison windows - the ones people leave open longest - had no way out but the mouse. Generic
/// enough for <c>Fubar.Controls</c>; it knows about windows and text, not about requests.</para>
/// <para>Deliberately NOT applied to a main window. Escape closing the app is never what anybody
/// meant.</para>
/// </remarks>
public static class WindowEscape
{
    /// <summary>
    /// Handles Escape for <paramref name="window"/>. Returns true when it did something, which the
    /// caller should report as <c>e.Handled</c>.
    /// </summary>
    public static bool Handle(Window window, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key != Key.Escape || e.Handled)
        {
            return false;
        }

        if (window.FocusManager?.GetFocusedElement() is Visual focused && IsTextEntry(focused))
        {
            // To the window, not to the next control: Escape is a way out, not a way onward, and
            // tabbing somewhere the user did not ask for is its own surprise.
            //
            // Focusable has to be set first. A Window is not focusable by default, and Focus() on one
            // that is not simply does nothing - leaving the caret where it was, so the NEXT Escape
            // reads as "still in a text box" and the window can never be closed from the keyboard at
            // all.
            window.Focusable = true;
            window.Focus();
            return true;
        }

        window.Close();
        return true;
    }

    /// <summary>
    /// Whether the focus is inside something that takes typed text.
    /// </summary>
    /// <remarks>
    /// Walks ANCESTORS as well as testing the element itself: focus inside an editor lands on its
    /// <see cref="TextArea"/> rather than on the <see cref="TextEditor"/> anyone would name, and a
    /// templated <see cref="TextBox"/> can hand focus to a part inside itself.
    /// </remarks>
    private static bool IsTextEntry(Visual focused)
    {
        for (var current = focused; current is not null; current = current.GetVisualParent())
        {
            if (current is TextBox or TextArea or TextEditor or AutoCompleteBox)
            {
                return true;
            }
        }

        return false;
    }
}
