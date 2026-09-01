using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// A question with room for the detail behind it, and one button per answer.
///
/// Closes with the index of the chosen answer, or -1 for anything that is not an answer - Cancel,
/// Escape, or the window's own close box. The one thing a prompt must never do is treat "went away"
/// as agreement, and that is the whole reason the cancelled case has a value of its own rather than
/// defaulting to the first choice.
/// </summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string message, IReadOnlyList<string> choices, string cancelLabel = "Cancel")
        : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
        Title = title;

        // Cancel first, and it is the one Escape and Return both reach. The safe answer should be the
        // one a stray keypress gives, because the other answers here overwrite or discard files.
        Buttons.Children.Add(Button(cancelLabel, -1, primary: false, isCancel: true));

        for (var i = 0; i < choices.Count; i++)
        {
            Buttons.Children.Add(Button(choices[i], i, primary: i == 0, isCancel: false));
        }
    }

    private Button Button(string content, int result, bool primary, bool isCancel)
    {
        var button = new Button
        {
            Content = content,
            IsCancel = isCancel,
            IsDefault = isCancel,
            VerticalAlignment = VerticalAlignment.Center,
        };

        button.Classes.Add(primary ? "primary-btn" : "toolbar-btn");
        button.Click += (_, _) => Close(result);

        return button;
    }
}
