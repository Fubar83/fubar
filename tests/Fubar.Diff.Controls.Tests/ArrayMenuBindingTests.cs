using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Controls.Views;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// The array right-click menu's COMMAND BINDING.
///
/// Every layer under this was already tested and passing while the menu item did nothing on a real
/// file, which narrows the suspect to the one thing a view-model test cannot reach: whether the
/// generated <c>MenuItem</c> actually gets a command. A context menu is a popup and therefore its own
/// namescope, so an <c>#ElementName</c> binding reaching out of it is exactly the kind of thing that
/// silently resolves to null - rendering a perfectly ordinary menu item that does nothing when clicked.
/// </summary>
public class ArrayMenuBindingTests
{
    private static JsonAstScalar Str(string value) =>
        new(JsonAstKind.String, $"\"{value}\"", value, SourceSpan.None);

    /// <summary>A pane showing one reordered array of strings, so the tree has exactly one array row.</summary>
    private static DiffPaneViewModel Pane()
    {
        var path = JsonPath.Root.Property("tags");

        var changes = new List<JsonChange>
        {
            new(path.Index(0), ChangeKind.Modified, Str("GML"), Str("XML")),
            new(path.Index(1), ChangeKind.Modified, Str("XML"), Str("GML")),
        };

        var arrayKeys = new Dictionary<string, ArrayKeyChoices>
        {
            ["$.tags"] = new("$.tags", null, [], false, ArrayMatchMode.Position),
        };

        var (roots, _) = JsonChangeNodeViewModel.Build(changes, arrayKeys);

        return new DiffPaneViewModel { ArrayKeys = arrayKeys, SemanticTree = roots };
    }

    [AvaloniaFact]
    public void The_generated_menu_item_actually_gets_a_command()
    {
        var pane = Pane();
        var view = new JsonTreeView { DataContext = pane };

        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();

        var row = view.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.ContextMenu is not null && b.DataContext is JsonChangeNodeViewModel { IsArray: true });

        Assert.NotNull(row);

        var menu = row!.ContextMenu!;
        menu.Open(row);

        // The DIRECT child settles the question on its own: it reaches for the same #Root element
        // binding the generated items do, and a context menu is a popup with its own namescope. If this
        // one has no command, none of them do - and the menu renders perfectly while doing nothing,
        // which is exactly the reported symptom.
        var direct = menu.GetLogicalDescendants().OfType<MenuItem>()
            .FirstOrDefault(m => Equals(m.Header, "Match by another field…"));

        Assert.NotNull(direct);
        Assert.NotNull(direct!.Command);

        var submenu = menu.GetLogicalDescendants().OfType<MenuItem>()
            .FirstOrDefault(m => Equals(m.Header, "Compare this list"));

        Assert.NotNull(submenu);

        // Submenu containers are built lazily, so it has to be opened before there is anything to look
        // at.
        submenu!.IsSubMenuOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var items = submenu.GetLogicalDescendants().OfType<MenuItem>()
            .Where(m => m.DataContext is ArrayKeyOption)
            .ToList();

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.NotNull(item.Command));
    }

    [AvaloniaFact]
    public void The_generated_menu_items_say_which_one_is_current()
    {
        // Same class of defect as the missing command, one layer over: ArrayKeyOption.IsCurrent was
        // computed correctly and bound to NOTHING, so the menu offered four ways to match an array and
        // said nothing at all about the one already in force. A view-model test cannot see that - the
        // property was right the whole time - so this asks the rendered items.
        var pane = Pane();
        var view = new JsonTreeView { DataContext = pane };

        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();

        var row = view.GetVisualDescendants().OfType<Border>()
            .First(b => b.ContextMenu is not null && b.DataContext is JsonChangeNodeViewModel { IsArray: true });

        var menu = row.ContextMenu!;
        menu.Open(row);

        var submenu = menu.GetLogicalDescendants().OfType<MenuItem>()
            .First(m => Equals(m.Header, "Compare this list"));

        submenu.IsSubMenuOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var items = menu.GetLogicalDescendants().OfType<MenuItem>()
            .Where(m => m.DataContext is ArrayKeyOption)
            .ToList();

        Assert.NotEmpty(items);

        // Radio, not check: these are one mutually exclusive setting and exactly one is always in force.
        Assert.All(items, item => Assert.Equal(MenuItemToggleType.Radio, item.ToggleType));

        var checkedItem = Assert.Single(items, item => item.IsChecked);
        Assert.Equal(ArrayMatchMode.Position, ((ArrayKeyOption)checkedItem.DataContext!).Mode);
    }

    [AvaloniaFact]
    public void The_menu_heading_states_the_rule_in_force()
    {
        // Before opening any submenu. The marks answer the same question but only once you have gone
        // looking, and "what is the rule for this list right now" is what a right-click is asking.
        var pane = Pane();
        var view = new JsonTreeView { DataContext = pane };

        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();

        var row = view.GetVisualDescendants().OfType<Border>()
            .First(b => b.ContextMenu is not null && b.DataContext is JsonChangeNodeViewModel { IsArray: true });

        row.ContextMenu!.Open(row);

        var heading = row.ContextMenu.GetLogicalDescendants().OfType<MenuItem>()
            .FirstOrDefault(m => Equals(m.Header, "Matched by position"));

        Assert.NotNull(heading);
    }
}
