using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// The open dialog - see <see cref="OpenComparisonViewModel"/>.
///
/// The code-behind exists for drag and drop, which has no data representation: only the controls know
/// what the pointer is over, and "which side is about to take this file" is the whole reason there are
/// two targets rather than one window-sized one.
///
/// Unlike the main window's drop handler, FOLDERS ARE ACCEPTED here. There they are ignored, because a
/// dropped folder would have to guess between opening a folder comparison and doing nothing; here the
/// dialog is already the place where that question is asked and answered on screen before anything
/// happens.
/// </summary>
public partial class OpenComparisonWindow : Window
{
    public OpenComparisonWindow()
    {
        InitializeComponent();

        // Per-side handlers first, then the window as a fallback for a drop that lands on neither.
        Wire(LeftDrop, OpenSide.Left);
        Wire(RightDrop, OpenSide.Right);

        AddHandler(DragDrop.DragOverEvent, (_, e) => Advertise(e), handledEventsToo: false);
        AddHandler(DragDrop.DropEvent, OnWindowDrop, handledEventsToo: false);

        // The first thing anyone does here is name the left-hand side, so the caret starts there.
        // Without this, focus lands on whichever control the template reached first - one of the
        // option toggles - which both wastes the keyboard and draws a focus ring on a setting,
        // making it look switched on.
        Opened += (_, _) => LeftBox.Focus();
    }

    private void Wire(Border target, OpenSide side)
    {
        DragDrop.SetAllowDrop(target, true);

        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            Advertise(e);

            // The hover state has to be unmistakable - it is the only thing telling the user which of
            // the two sides is about to take what they are holding.
            target.Classes.Set("over", Paths(e.DataTransfer).Count > 0);
        });

        target.AddHandler(DragDrop.DragLeaveEvent, (_, _) => target.Classes.Set("over", false));

        target.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            target.Classes.Set("over", false);
            e.Handled = true;

            if (DataContext is OpenComparisonViewModel model)
            {
                model.Drop(side, Paths(e.DataTransfer));
            }
        });
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is OpenComparisonViewModel model)
        {
            model.Drop(Paths(e.DataTransfer));
        }
    }

    /// <summary>Advertises a copy only when the payload holds something droppable.</summary>
    private static void Advertise(DragEventArgs e)
    {
        e.DragEffects = Paths(e.DataTransfer).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// The local paths in a drop - files AND folders.
    ///
    /// <see cref="IStorageItem"/> rather than <c>IStorageFile</c>, which is what the main window
    /// filters to and what makes it ignore folders. Both kinds are meaningful here.
    /// </summary>
    private static List<string> Paths(IDataTransfer data) =>
        data.TryGetFiles() is { } items
            ? [.. items
                .Select(item => item.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)]
            : [];
}
