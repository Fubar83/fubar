using System;

namespace Fubar.Controls;

/// <summary>
/// A <see cref="Avalonia.Controls.TreeView"/> carrying the design system's explorer-tree look: taller
/// rows with a full-width hover/selection highlight, rounded row corners, and an enlarged chevron hit
/// area. Drop it in anywhere the app needs a navigation / data tree (a workspace explorer, a JSON tree,
/// an outline) - the host supplies only <c>ItemsSource</c> + a <c>TreeDataTemplate</c> for the row.
///
/// The appearance lives in <c>Themes/TreeView.axaml</c>, scoped under <c>fc|TreeView</c> so ordinary
/// <see cref="Avalonia.Controls.TreeView"/>s elsewhere keep the framework default. This subclass keeps
/// the Fluent tree/branch template (chevron, expand/collapse, indentation) and only restyles the row -
/// hence <see cref="StyleKeyOverride"/> points at the base type so that template is still resolved.
///
/// For per-row level indent that doesn't depend on the template, apply
/// <see cref="TreeLevelIndentConverter"/> to the DataTemplate root's Margin.
/// </summary>
public class TreeView : Avalonia.Controls.TreeView
{
    protected override Type StyleKeyOverride => typeof(Avalonia.Controls.TreeView);
}
