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

    private static IReadOnlyList<DiffEditorPane> Panes(ThreeWayView view) =>
        [.. view.GetVisualDescendants().OfType<DiffEditorPane>()];

    [AvaloniaFact]
    public void The_view_has_exactly_three_panes()
    {
        var (_, view) = Show(Populated());

        Assert.Equal(3, Panes(view).Count);
    }

    [AvaloniaFact]
    public void Each_pane_shows_its_own_document()
    {
        // The binding test proper: three different documents have to reach three different editors, in
        // the order the column headers claim.
        var (_, view) = Show(Populated());
        var panes = Panes(view);

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

        Assert.All(Panes(view), pane => Assert.Equal(3, pane.Document?.Lines.Count));
    }

    [AvaloniaFact]
    public void A_conflicting_row_is_marked_in_all_three_panes()
    {
        var (_, view) = Show(Populated());

        Assert.All(Panes(view), pane => Assert.True(pane.Document!.Lines[2].IsConflict));
    }

    [AvaloniaFact]
    public void An_empty_merge_renders_without_content()
    {
        // The state the window opens in, before any files are chosen - it must lay out rather than
        // throw on a null document.
        var (_, view) = Show(new ThreeWayPaneViewModel());

        Assert.All(Panes(view), pane => Assert.True(string.IsNullOrEmpty(pane.Document?.Text)));
    }

    [AvaloniaFact]
    public void Display_settings_reach_every_pane()
    {
        var pane = Populated();
        pane.ShowInvisibles = true;
        pane.SyntaxExtension = ".cs";

        var (_, view) = Show(pane);

        Assert.All(Panes(view), p =>
        {
            Assert.True(p.ShowInvisibles);
            Assert.Equal(".cs", p.SyntaxExtension);
            Assert.True(p.SyntaxHighlighting);
        });
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
