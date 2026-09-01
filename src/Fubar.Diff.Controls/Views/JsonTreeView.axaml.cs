using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// The semantic change tree.
///
/// The code-behind exists for one thing: making a row behave the way it looks. A tree row reads as a
/// single control, so clicking anywhere along it - the chevron, the name, the space after it - expands
/// or collapses it, rather than only the few pixels of the chevron doing that while the rest merely
/// selects.
/// </summary>
public partial class JsonTreeView : UserControl
{
    public JsonTreeView()
    {
        InitializeComponent();

        // Tapped rather than PointerPressed: a tap is a completed click in one place, so dragging out
        // of a row does not toggle it, and it does not fight the tree's own selection handling.
        Tree.AddHandler(TappedEvent, OnTapped, handledEventsToo: false);
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
