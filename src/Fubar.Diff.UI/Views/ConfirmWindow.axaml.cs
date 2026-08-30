using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// A yes/no question with room for the detail behind it.
///
/// Closes with <c>false</c> for anything that is not an explicit yes - the Cancel button, Escape, or
/// the window's own close box - because the one thing a confirmation must never do is treat "went
/// away" as "go ahead".
/// </summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string message, string confirmLabel)
        : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        Title = title;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
