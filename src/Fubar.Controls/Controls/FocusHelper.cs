using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Fubar.Controls;

/// <summary>
/// Attached behavior that focuses (and, for a TextBox, selects all text in) an element the moment
/// a bound flag flips true - e.g. <c>fc:FocusHelper.FocusOnTrue="{Binding IsEditing}"</c> on
/// an inline-rename TextBox. Toggling a TextBox's <c>IsVisible</c> alone does not focus it in
/// Avalonia, so without this, entering rename mode shows an editable-looking box that silently
/// doesn't receive keystrokes until the user clicks into it.
/// </summary>
public static class FocusHelper
{
    public static readonly AttachedProperty<bool> FocusOnTrueProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("FocusOnTrue", typeof(FocusHelper));

    public static void SetFocusOnTrue(Control element, bool value) => element.SetValue(FocusOnTrueProperty, value);

    public static bool GetFocusOnTrue(Control element) => element.GetValue(FocusOnTrueProperty);

    static FocusHelper()
    {
        FocusOnTrueProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is not true)
            {
                return;
            }

            // Deferred: the control has typically just become IsVisible="True" in the same tick,
            // and isn't focusable until layout has actually measured/arranged it.
            Dispatcher.UIThread.Post(() =>
            {
                control.Focus();
                if (control is TextBox textBox)
                {
                    textBox.SelectAll();
                }
            }, DispatcherPriority.Input);
        });
    }
}
