using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fubar.Diff.Controls.ViewModels;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// The semantic change tree.
///
/// The code-behind exists for two things, both of them "make the tree behave the way it looks".
///
/// A tree row reads as a single control, so clicking anywhere along it - the chevron, the name, the
/// space after it - expands or collapses it, rather than only the few pixels of the chevron doing that
/// while the rest merely selects.
///
/// And a selection made by NAVIGATION is scrolled to. The view model opens the ancestors (it owns
/// IsExpanded), but opening a row does not move the scrollbar, so stepping to a difference far down a
/// long tree still selected something nobody could see. Only a view can do this part: it needs the
/// container, which does not exist until the row it belongs to has been realised.
/// </summary>
public partial class JsonTreeView : UserControl
{
    public JsonTreeView()
    {
        InitializeComponent();

        // Tapped rather than PointerPressed: a tap is a completed click in one place, so dragging out
        // of a row does not toggle it, and it does not fight the tree's own selection handling.
        Tree.AddHandler(TappedEvent, OnTapped, handledEventsToo: false);

        DataContextChanged += OnDataContextChanged;
    }

    private DiffPaneViewModel? _viewModel;

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as DiffPaneViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffPaneViewModel.CurrentTreeNode))
        {
            ScrollSelectionIntoView();
        }
    }

    /// <summary>
    /// Brings the selected row on screen, after the ancestors it needed have been opened.
    ///
    /// <para>Posted rather than run inline: the view model has just set IsExpanded on the ancestors,
    /// and the containers for the rows those reveal do not exist until the next layout pass. Asking for
    /// a container before then finds nothing and scrolls nowhere - the failure is silent, which is
    /// exactly the kind that survives a code review.</para>
    /// </summary>
    private void ScrollSelectionIntoView()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_viewModel?.CurrentTreeNode is not { } node)
                {
                    return;
                }

                // BringIntoView on the container rather than TreeView.ScrollIntoView: the latter only
                // looks among the top-level items, and every row worth navigating to here is nested.
                if (Tree.TreeContainerFromItem(node) is Control container)
                {
                    container.BringIntoView();
                }
            },
            DispatcherPriority.Loaded);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && RowToToggle(source.GetSelfAndVisualAncestors()) is { } item)
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }

    /// <summary>
    /// The row a tap should expand or collapse, or null when it should do neither.
    ///
    /// Walks outwards from whatever was tapped and stops at the first thing with its own answer. The
    /// chevron is the subtle one: it already toggles by itself, so handling it here as well would
    /// toggle twice and land back where it started - the click would look broken rather than doing
    /// nothing visible. The row's own buttons mean something else entirely, and a leaf has nothing to
    /// expand.
    ///
    /// Separated from the event so the rule can be tested without a pointer: headless Avalonia will
    /// not deliver a real press to a row, and a test that sets IsExpanded itself proves nothing.
    /// </summary>
    internal static TreeViewItem? RowToToggle(IEnumerable<Visual> selfAndAncestors)
    {
        foreach (var element in selfAndAncestors)
        {
            switch (element)
            {
                case Button or ToggleButton:
                    return null;

                case TreeViewItem item:
                    return item.ItemCount > 0 ? item : null;

                case TreeView:
                    return null;
            }
        }

        return null;
    }
}
