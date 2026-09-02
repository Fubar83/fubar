using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Fubar.Diff.Core.Files;
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

        Closing += OnClosing;
    }

    /// <summary>
    /// Set once the tabs have all agreed to close, so the second Close() is not intercepted again.
    /// </summary>
    private bool _closeConfirmed;

    // No full-screen support, for the same reason API Studio has none: Avalonia's extended-client-area
    // chrome draws a full-screen caption button this version exposes no way to remove, and the button
    // is outside the window's own visual tree, so no style or tree walk can hide it either. Removed at
    // the state level instead - anything driving the window into FullScreen snaps back to Maximized.
    // Minimize / maximize / restore / close are untouched.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty && WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Maximized;
        }
    }

    // The title-bar row is our own content (ExtendClientAreaToDecorationsHint), so none of the usual
    // drag-to-move / double-click-to-maximize behaviour exists unless it is implemented here.
    private void TitleBarDragArea_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void TitleBarDragArea_OnDoubleTapped(object? sender, TappedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>
    /// Stops the window closing over unsaved changes.
    ///
    /// The prompt is asynchronous and Closing is not, so the close is cancelled first and re-issued
    /// once the answer is in - which is the standard shape for this and the only one that works
    /// without blocking the UI thread inside an event handler.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not ShellViewModel shell)
        {
            return;
        }

        e.Cancel = true;

        _ = ConfirmThenCloseAsync(shell);
    }

    private async System.Threading.Tasks.Task ConfirmThenCloseAsync(ShellViewModel shell)
    {
        if (!await shell.ConfirmCloseAsync().ConfigureAwait(true))
        {
            return;
        }

        _closeConfirmed = true;
        Close();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+O opens the comparison dialog. Handled here rather than as a KeyBinding for the same
        // reason Ctrl+F is: it opens a WINDOW, which is a view's job, not a command on a view model.
        // It is also a window-level action now rather than a tab-level one, because the dialog can
        // open a pair of folders and no tab can hold those.
        if (e is { Key: Key.O, KeyModifiers: KeyModifiers.Control })
        {
            OnOpenClick(sender, new RoutedEventArgs());
            e.Handled = true;

            return;
        }

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
        // The menu item's own DataContext first (it inherits the per-tab scope), falling back to the
        // shell's selection: a flyout lives in a popup, and a popup's inherited DataContext is one
        // more thing that could be changed by a future refactor without anything failing loudly.
        var tab = (sender as Control)?.DataContext as ComparisonViewModel
            ?? (DataContext as ShellViewModel)?.SelectedTab;

        if (tab is null)
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
    /// Opens the comparison dialog - the single way in, for files and folders alike.
    ///
    /// Modal, unlike the folder and merge windows. Those two are shown alongside this one because
    /// they are used TOGETHER with it; this one is a question with an answer, and leaving it open
    /// beside the comparison it just started would be a second place to type paths that no longer
    /// mean anything.
    /// </summary>
    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        var model = shell.CreateOpenDialog();
        var dialog = new OpenComparisonWindow { DataContext = model };

        OpenComparisonRequest? request = null;

        model.Accepted += (_, r) =>
        {
            request = r;
            dialog.Close();
        };

        model.Cancelled += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);

        if (request is null)
        {
            return;
        }

        // Folders open their own window, files open a tab. The dialog decided WHICH - see
        // ComparisonTargets - and this only carries out the answer.
        if (request.Kind is ComparisonTargetKind.Folders or ComparisonTargetKind.LinkedFolder)
        {
            var folders = shell.CreateFolderComparison();

            folders.LeftPath = request.Left;
            folders.RightPath = request.Right;
            folders.LinkedMode = request.Kind == ComparisonTargetKind.LinkedFolder;

            new FolderWindow { DataContext = folders }.Show(this);

            return;
        }

        await shell.OpenAsync(request);
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
