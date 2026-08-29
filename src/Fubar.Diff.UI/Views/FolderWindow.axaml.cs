using Avalonia.Controls;
using Avalonia.Input;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// The folder comparison window.
///
/// Code-behind exists for one thing: double-click. It is a gesture, not a command, and routing it
/// through a key binding would mean the row under the pointer and the row that is selected could
/// disagree - so the view handles the tap and lets the view model decide whether the selection is
/// something that can be opened at all.
/// </summary>
public partial class FolderWindow : Window
{
    public FolderWindow() => InitializeComponent();

    private void OnRowActivated(object? sender, TappedEventArgs e)
    {
        if (DataContext is FolderViewModel folders)
        {
            folders.OpenCommand.Execute(null);
        }
    }
}
