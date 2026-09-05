using System;

namespace Fubar.Controls;

/// <summary>
/// A <see cref="Avalonia.Controls.TreeView"/> carrying the design system's explorer-tree look: taller
/// rows with a full-width hover/selection highlight, rounded row corners, and an enlarged chevron hit
/// area. Drop it in anywhere the app needs a navigation / data tree (a workspace explorer, a JSON tree,
/// an outline) - the host supplies only <c>ItemsSource</c> + a <c>TreeDataTemplate</c> for the row.
///
/// The appearance lives in <c>Themes/TreeView.axaml</c>. This subclass keeps the Fluent tree/branch
/// template (chevron, expand/collapse, indentation) and only restyles the row - hence
/// <see cref="StyleKeyOverride"/> points at the base type so that template is still resolved.
///
/// <para>That override has a consequence that cost real time to find: Avalonia matches type SELECTORS
/// against the style key too, so <c>fc|TreeView</c> matches no instance of this class. The theme selects
/// on the base <c>TreeView</c> type instead, which means it dresses every TreeView in the app.</para>
///
/// <para>Row indentation and its guide rails come from <see cref="TreeIndentGuides"/> inside the row
/// template, so a host supplies only the row's content - never a level-based margin of its own.</para>
/// </summary>
public class TreeView : Avalonia.Controls.TreeView
{
    protected override Type StyleKeyOverride => typeof(Avalonia.Controls.TreeView);
}
