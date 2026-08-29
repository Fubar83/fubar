using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Controls.Views;
using Fubar.Diff.Core.Merge;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// That the three-pane view actually WIRES UP.
///
/// The failure this exists for is silent. A mistyped binding path in XAML is not a compile error and
/// does not throw - Avalonia logs it and leaves the control empty - so a merge window with a typo in
/// one of its nine bindings looks exactly like a merge with nothing in it. Everything below asserts
/// through the rendered controls rather than the view model, because the view model was already right
/// in the case this is guarding against.
/// </summary>
public class ThreeWayViewTests
{
    /// <summary>base "b" changed to "L" on the left only, plus a conflict on the following line.</summary>
    private static ThreeWayPaneViewModel Populated()
    {
        var result = ThreeWayResult.Create(
        [
            new ThreeWayLine(1, "shared", 1, "shared", 1, "shared", MergeKind.Unchanged, -1),
            new ThreeWayLine(2, "b", 2, "L", 2, "b", MergeKind.LeftOnly, 0),
            new ThreeWayLine(3, "c", 3, "cl", 3, "cr", MergeKind.Conflict, 1),
        ]);

        var pane = new ThreeWayPaneViewModel();
        pane.Show(result);

        return pane;
    }

    /// <summary>Builds the view inside a window and lays it out, so bindings and templates run.</summary>
    private static (Window Window, ThreeWayView View) Show(ThreeWayPaneViewModel pane)
    {
        var view = new ThreeWayView { DataContext = pane };
        var window = new Window { Content = view, Width = 1200, Height = 400 };

        window.Show();
        window.UpdateLayout();

        return (window, view);
    }

    /// <summary>
    /// The three COLUMN editors. Since the close-up arrived there are six panes in the tree, and the
    /// two sets answer different questions - the columns show the whole document, the close-up shows
    /// one region - so a test has to say which it means.
    /// </summary>
    private static IReadOnlyList<DiffEditorPane> Columns(ThreeWayView view) =>
    [
        .. view.GetVisualDescendants()
            .OfType<DiffEditorPane>()
            .Where(pane => !pane.GetVisualAncestors().OfType<MergeDetailPane>().Any()),
    ];

    /// <summary>The three stacked editors inside the close-up.</summary>
    private static IReadOnlyList<DiffEditorPane> DetailPanes(ThreeWayView view) =>
    [
        .. view.GetVisualDescendants()
            .OfType<DiffEditorPane>()
            .Where(pane => pane.GetVisualAncestors().OfType<MergeDetailPane>().Any()),
    ];

    [AvaloniaFact]
    public void The_view_has_exactly_three_panes()
    {
        var (_, view) = Show(Populated());

        Assert.Equal(3, Columns(view).Count);
    }

    [AvaloniaFact]
    public void Each_pane_shows_its_own_document()
    {
        // The binding test proper: three different documents have to reach three different editors, in
        // the order the column headers claim.
        var (_, view) = Show(Populated());
        var panes = Columns(view);

        var texts = panes.Select(p => p.Document?.Text).ToList();

        Assert.Contains("shared\nL\ncl", texts);
        Assert.Contains("shared\nb\nc", texts);
        Assert.Contains("shared\nb\ncr", texts);
    }

    [AvaloniaFact]
    public void Every_pane_has_the_same_row_count()
    {
        // What lets the three scroll as one. A binding that fed a pane the wrong document would
        // usually still satisfy this, but a missing one would not.
        var (_, view) = Show(Populated());

        Assert.All(Columns(view), pane => Assert.Equal(3, pane.Document?.Lines.Count));
    }

    [AvaloniaFact]
    public void A_conflicting_row_is_marked_in_all_three_panes()
    {
        var (_, view) = Show(Populated());

        Assert.All(Columns(view), pane => Assert.True(pane.Document!.Lines[2].IsConflict));
    }

    [AvaloniaFact]
    public void An_empty_merge_renders_without_content()
    {
        // The state the window opens in, before any files are chosen - it must lay out rather than
        // throw on a null document.
        var (_, view) = Show(new ThreeWayPaneViewModel());

        Assert.All(Columns(view), pane => Assert.True(string.IsNullOrEmpty(pane.Document?.Text)));
    }

    [AvaloniaFact]
    public void Display_settings_reach_every_pane()
    {
        var pane = Populated();
        pane.ShowInvisibles = true;
        pane.SyntaxExtension = ".cs";

        var (_, view) = Show(pane);

        Assert.All(Columns(view), p =>
        {
            Assert.True(p.ShowInvisibles);
            Assert.Equal(".cs", p.SyntaxExtension);
            Assert.True(p.SyntaxHighlighting);
        });
    }

    [AvaloniaFact]
    public void The_close_up_shows_the_selected_region_in_all_three_versions()
    {
        var pane = Populated();
        var (_, view) = Show(pane);

        pane.CurrentRegion = 1; // the conflict

        Assert.Equal("cl", pane.DetailLeft!.Text);
        Assert.Equal("c", pane.DetailBase!.Text);
        Assert.Equal("cr", pane.DetailRight!.Text);
        Assert.True(pane.HasDetail);

        // Six editors in total: three columns, plus the three stacked in the close-up. Its own
        // bindings are separate from the columns' and can be wrong independently, so they get their
        // own assertion rather than riding on the view model's properties.
        Assert.Equal(3, Columns(view).Count);

        var detail = DetailPanes(view);
        Assert.Equal(3, detail.Count);
        Assert.Contains("cl", detail.Select(p => p.Document?.Text));
        Assert.Contains("c", detail.Select(p => p.Document?.Text));
        Assert.Contains("cr", detail.Select(p => p.Document?.Text));
    }

    [AvaloniaFact]
    public void The_close_up_empties_when_nothing_is_selected()
    {
        var pane = Populated();
        Show(pane);

        pane.CurrentRegion = 1;
        Assert.True(pane.HasDetail);

        pane.CurrentRegion = -1;

        Assert.False(pane.HasDetail);
        Assert.Null(pane.DetailBase);
    }

    [AvaloniaFact]
    public void Hiding_the_close_up_collapses_its_row_rather_than_leaving_a_blank_band()
    {
        // IsVisible alone would leave the row occupying its height - the gotcha DiffView already
        // documents, and the reason both need their RowDefinition zeroed.
        var pane = Populated();
        var (window, view) = Show(pane);

        pane.IsDetailVisible = false;
        window.UpdateLayout();

        var root = view.GetVisualDescendants().OfType<Grid>().First(g => g.RowDefinitions.Count == 3);

        Assert.Equal(0, root.RowDefinitions[1].Height.Value);
        Assert.Equal(0, root.RowDefinitions[2].Height.Value);
    }

    [AvaloniaFact]
    public void Showing_the_close_up_again_restores_its_height()
    {
        var pane = Populated();
        var (window, view) = Show(pane);

        pane.IsDetailVisible = false;
        window.UpdateLayout();
        pane.IsDetailVisible = true;
        window.UpdateLayout();

        var root = view.GetVisualDescendants().OfType<Grid>().First(g => g.RowDefinitions.Count == 3);

        Assert.True(root.RowDefinitions[2].Height.Value > 0);
    }

    [AvaloniaFact]
    public void Navigation_reaches_the_view()
    {
        var pane = Populated();
        var (_, view) = Show(pane);

        // Conflicts-only is on by default, so Next skips the left-only region and lands on the
        // conflict - which is the behaviour that makes a merge faster than a diff.
        pane.NextRegionCommand.Execute(null);

        Assert.Equal(1, pane.CurrentRegion);
        Assert.Equal(2, pane.ScrollToRow);
    }
}
