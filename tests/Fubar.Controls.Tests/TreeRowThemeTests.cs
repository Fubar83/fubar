using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Fubar.Controls.Tests;

/// <summary>
/// Guards the explorer-tree row look against the way it failed before: silently.
///
/// <para>Every one of these setters used to live in styles selected as <c>fc|TreeView TreeViewItem</c>,
/// and not one of them ever applied. Two independent reasons, both of which a style selector reports by
/// simply doing nothing:</para>
///
/// <list type="number">
/// <item>Avalonia matches a type selector against a control's STYLE KEY, and
/// <see cref="Fubar.Controls.TreeView"/> overrides its style key to the base
/// <see cref="Avalonia.Controls.TreeView"/> (it has to - that is how it keeps the Fluent template). So
/// <c>fc|TreeView</c> matches no control that exists.</item>
/// <item>A descendant combinator stops matching once a selector reaches into a template, so even spelled
/// as <c>TreeView TreeViewItem:selected /template/ Border#PART_LayoutRoot</c> it would have missed.</item>
/// </list>
///
/// <para>The visible cost was a tree in stock Fluent - 32px rows and a full-bleed accent-blue selected
/// row - while the palette said otherwise and everyone read the palette. Hence assertions on the RENDERED
/// values rather than on the markup.</para>
/// </summary>
public class TreeRowThemeTests
{
    [AvaloniaFact]
    public void A_tree_row_takes_its_metrics_from_the_design_system_not_from_Fluent()
    {
        var (_, item) = ShowTree();

        Assert.Equal(30, item.MinHeight);

        // No vertical padding, so rows touch and a row's indent rail meets its neighbours'.
        Assert.Equal(new Avalonia.Thickness(4, 0, 4, 0), item.Padding);
    }

    [AvaloniaFact]
    public void A_selected_row_is_filled_from_the_palette_not_with_the_raw_accent()
    {
        var (window, item) = ShowTree();
        item.IsSelected = true;
        Dispatcher.UIThread.RunJobs();

        var expected = (ISolidColorBrush)Find(window, "BgSelected");
        var accent = (Color)Find(window, "SystemAccentColor");

        Assert.Equal(expected.Color, LayoutRootFill(item));
        Assert.NotEqual(accent, LayoutRootFill(item));
    }

    private static object Find(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), "no resource " + key);
        return value!;
    }

    private static (Window Window, TreeViewItem Item) ShowTree()
    {
        var tree = new Fubar.Controls.TreeView { ItemsSource = new[] { "a", "b" } };
        var window = new Window { Content = tree, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, tree.GetVisualDescendants().OfType<TreeViewItem>().First());
    }

    private static Color? LayoutRootFill(TreeViewItem item) =>
        (item.GetVisualDescendants()
            .OfType<Border>()
            .First(b => b.Name == "PART_LayoutRoot")
            .Background as ISolidColorBrush)?.Color;
}
