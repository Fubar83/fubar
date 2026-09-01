using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// The folder comparison window.
///
/// Code-behind exists for two things the control owns rather than the view model. Double-click is a
/// gesture, not a command, and routing it through a key binding would let the row under the pointer
/// and the selected row disagree. And multiple selection lives on the TreeView - Avalonia's
/// SelectedItems is not usefully bindable - so the selection is pushed up as a plain list, which is
/// all the view model needs to decide whether two files can be paired.
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

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not FolderViewModel folders)
        {
            return;
        }

        IReadOnlyList<FolderEntryViewModel> rows = Tree.SelectedItems is { } selected
            ? [.. selected.OfType<FolderEntryViewModel>()]
            : [];

        folders.SetSelection(rows);
    }
}
