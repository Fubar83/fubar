using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// Asks for one line of text. Closes with null for anything that is not an answer - Cancel, Escape,
/// or the window's own close box - so a caller can tell "they typed nothing" from "they went away".
/// </summary>
public partial class PromptWindow : Window
{
    public PromptWindow()
    {
        InitializeComponent();
    }

    public PromptWindow(string title, string message, string initial)
        : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
        Input.Text = initial;
        Title = title;

        // Focused with the initial value selected, so typing replaces it - the value offered is a
        // starting point, not something to edit around.
        Opened += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    private void OnAccept(object? sender, RoutedEventArgs e) =>
        Close(string.IsNullOrWhiteSpace(Input.Text) ? null : Input.Text.Trim());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
