using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace Fubar.Controls;

/// <summary>
/// Binds a tree row's folded state to a property on the item behind it, in both directions:
/// <code>
/// &lt;Style Selector="TreeViewItem"&gt;
///     &lt;Setter Property="fc:TreeItemState.ExpandedPath" Value="IsExpanded" /&gt;
/// &lt;/Style&gt;
/// </code>
/// A path rather than a value, so a tree whose items have no such property simply does not set it.
/// </summary>
/// <remarks>
/// <para>
/// Binding <see cref="TreeViewItem.IsExpandedProperty"/> from a style setter looks like it works and
/// then quietly stops. The expander writes the user's fold as a LOCAL value, and a local value outranks
/// a style setter permanently - so the first fold by hand severs the connection, and everything the view
/// model says about that row afterwards is ignored. That is how a filter came to report matches inside a
/// folder while leaving the folder shut.
/// </para>
/// <para>
/// So the binding is established on the row itself, where it sits at the same LOCAL priority the
/// expander writes at: the two share one slot and the later write wins, in whichever direction it came
/// from. Mirroring the two properties with change handlers instead does NOT work - a container is born
/// collapsed, and the mirror dutifully reports that back to the view model before the row has even been
/// shown, folding a tree that nobody folded.
/// </para>
/// </remarks>
public static class TreeItemState
{
    /// <summary>Path, on the row's own item, to the two-way bindable expansion flag.</summary>
    public static readonly AttachedProperty<string?> ExpandedPathProperty =
        AvaloniaProperty.RegisterAttached<TreeViewItem, string?>("ExpandedPath", typeof(TreeItemState));

    static TreeItemState()
    {
        ExpandedPathProperty.Changed.AddClassHandler<TreeViewItem, string?>((item, e) =>
        {
            if (e.NewValue.Value is { Length: > 0 } path)
            {
                item.Bind(TreeViewItem.IsExpandedProperty, new Binding(path) { Mode = BindingMode.TwoWay });
            }
        });
    }

    public static string? GetExpandedPath(TreeViewItem item) => item.GetValue(ExpandedPathProperty);

    public static void SetExpandedPath(TreeViewItem item, string? value) =>
        item.SetValue(ExpandedPathProperty, value);
}
