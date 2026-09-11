using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Fubar.Controls;

namespace Fubar.Controls.Tests;

/// <summary>
/// What Escape does in a window that is not the main one.
///
/// <para>Two stages, because Escape means two things and the inner one comes first: leave the field,
/// then leave the window. A single-stage version closes a window out from under someone who was only
/// trying to get out of a filter box.</para>
/// </summary>
public class WindowEscapeTests
{
    private static KeyEventArgs Escape() =>
        new() { Key = Key.Escape, RoutedEvent = InputElement.KeyDownEvent };

    private static (Window Window, TextBox Box) Shown()
    {
        var box = new TextBox();
        var window = new Window { Content = box, Width = 200, Height = 120 };
        window.Show();
        return (window, box);
    }

    [AvaloniaFact]
    public void Escape_closes_a_window_nothing_is_being_typed_into()
    {
        var (window, _) = Shown();

        Assert.True(WindowEscape.Handle(window, Escape()));
        Assert.False(window.IsVisible);
    }

    /// <summary>The first Escape is about the FIELD. Closing here would lose whatever else the window
    /// had set up, for someone who only wanted out of the box.</summary>
    [AvaloniaFact]
    public void Escape_in_a_text_box_leaves_the_box_and_keeps_the_window()
    {
        var (window, box) = Shown();
        box.Focus();

        Assert.True(WindowEscape.Handle(window, Escape()));
        Assert.True(window.IsVisible);
        Assert.False(box.IsFocused);
    }

    [AvaloniaFact]
    public void A_second_escape_then_closes_it()
    {
        var (window, box) = Shown();
        box.Focus();

        WindowEscape.Handle(window, Escape());
        Assert.True(WindowEscape.Handle(window, Escape()));

        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void Any_other_key_is_left_alone()
    {
        var (window, _) = Shown();

        Assert.False(WindowEscape.Handle(
            window, new KeyEventArgs { Key = Key.A, RoutedEvent = InputElement.KeyDownEvent }));

        Assert.True(window.IsVisible);
    }

    /// <summary>Something else on the way up already dealt with it - a completion popup dismissing
    /// itself, say - and a window that closed anyway would be closing on a keystroke that was not
    /// about it.</summary>
    [AvaloniaFact]
    public void An_escape_something_else_handled_is_left_alone()
    {
        var (window, _) = Shown();
        var e = Escape();
        e.Handled = true;

        Assert.False(WindowEscape.Handle(window, e));
        Assert.True(window.IsVisible);
    }
}
