using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The diff renderers resolve their brushes from the palette on each render pass, but nothing
        // tells AvaloniaEdit's TextView that a theme swap invalidated what it already painted - so
        // without this the tints keep the old theme's colours until the next scroll.
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        // Dropping files is the fastest way to start a comparison, and dropping two at once is the
        // whole interaction - so it is handled on the window rather than per pane.
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Ctrl+F is handled here rather than as a KeyBinding because it targets a control, not a
        // command - it has to know which pane has focus. Tunnelling so it wins before the editor's own
        // handling of the gesture.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e is not { Key: Key.F, KeyModifiers: KeyModifiers.Control })
        {
            return;
        }

        // Whichever view is actually on screen owns the find bar. Routing to the side-by-side one
        // unconditionally would open a search over panes nobody is looking at.
        if (Diff.IsVisible)
        {
            Diff.OpenSearch();
            e.Handled = true;
        }
        else if (Unified.IsVisible)
        {
            Unified.OpenSearch();
            e.Handled = true;
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        Diff.OnThemeChanged();
        Unified.OnThemeChanged();
    }

    /// <summary>
    /// Opens the detailed settings window for the CURRENT tab. Read off the button's own DataContext
    /// rather than the shell's SelectedTab: the button lives inside the per-tab-scoped ContentControl
    /// (see MainWindow.axaml), so its DataContext already IS the right ComparisonViewModel - reaching
    /// back up through the shell would work too, but would silently break if a future refactor moved
    /// this button outside that scope.
    /// </summary>
    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ComparisonViewModel tab })
        {
            return;
        }

        new SettingsWindow { DataContext = tab }.ShowDialog(this);
    }

    /// <summary>
    /// Opens a three-way merge in its own window.
    ///
    /// Shown rather than shown-as-dialog: resolving a merge is a long task, and blocking the
    /// comparison window for its duration would take away the one thing most likely to be wanted
    /// alongside it - a two-way diff of the same files.
    /// </summary>
    private void OnMergeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        new MergeWindow { DataContext = shell.CreateMerge() }.Show(this);
    }

    /// <summary>
    /// Opens a folder comparison in its own window.
    ///
    /// Shown rather than modal, and deliberately: opening a file pair from it creates a TAB in this
    /// window, so the two are used together - blocking this one would make the feature's entire point
    /// unreachable.
    /// </summary>
    private void OnFoldersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        new FolderWindow { DataContext = shell.CreateFolderComparison() }.Show(this);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        // Advertise Copy only when the payload actually contains files; otherwise the cursor promises
        // a drop that would do nothing.
        e.DragEffects = LocalPaths(e.DataTransfer).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        var paths = LocalPaths(e.DataTransfer);
        if (paths.Count > 0)
        {
            // Fire-and-forget: the drop handler must return promptly to release the drag source, and
            // the comparison reports its own errors through the view model.
            _ = shell.OpenFilesAsync(paths);
        }
    }

    /// <summary>
    /// The dropped items that are real files on disk. Directories and virtual items are skipped -
    /// folder comparison is a later phase, and silently comparing something unexpected is worse than
    /// ignoring it.
    /// </summary>
    private static List<string> LocalPaths(IDataTransfer data) =>
        data.TryGetFiles() is { } items
            ? [.. items
                .OfType<IStorageFile>()
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => path!)]
            : [];
}
