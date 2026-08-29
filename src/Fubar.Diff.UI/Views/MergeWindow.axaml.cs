using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Views;

/// <summary>
/// The three-way merge window. Code-behind mirrors <see cref="MainWindow"/>'s, for the same three
/// reasons: a theme swap has to be pushed into the editors, Ctrl+F targets a CONTROL rather than a
/// command so it cannot be a key binding, and dropping files is the fastest way to start.
/// </summary>
public partial class MergeWindow : Window
{
    public MergeWindow()
    {
        InitializeComponent();

        // The diff renderers resolve their brushes per render pass, but nothing tells AvaloniaEdit's
        // TextView that a theme swap invalidated what it already painted.
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e is { Key: Key.F, KeyModifiers: KeyModifiers.Control })
        {
            Merge.OpenSearch();
            e.Handled = true;
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => Merge.OnThemeChanged();

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        // Three files, not two: anything less cannot start a merge, so promising a drop that would do
        // nothing is worse than declining it.
        e.DragEffects = LocalPaths(e.DataTransfer).Count >= 3 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not MergeViewModel merge)
        {
            return;
        }

        var paths = LocalPaths(e.DataTransfer);
        if (paths.Count >= 3)
        {
            // Fire-and-forget: the drop handler must return promptly to release the drag source, and
            // the merge reports its own errors through the view model.
            _ = merge.OpenFilesAsync(paths);
        }
    }

    private static List<string> LocalPaths(IDataTransfer data) =>
        data.TryGetFiles() is { } items
            ? [.. items
                .OfType<IStorageFile>()
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => path!)]
            : [];
}
