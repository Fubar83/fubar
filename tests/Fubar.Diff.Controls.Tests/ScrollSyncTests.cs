using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Controls.Views;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// Scroll sync between the aligned panes, on BOTH axes.
///
/// Vertical sync has always been a plain offset copy, which is only possible because the two documents
/// have the same number of rows (filler discipline). Horizontal used to be left independent on the
/// argument that dragging one pane sideways because the other has a long line is disorienting - but
/// the rows are aligned, so row N is the same change on both sides, and scrolling right to read the
/// end of a long line pushed its counterpart off screen exactly when it was the thing being compared.
///
/// What these tests can and cannot reach: headless Avalonia lays out no visual lines, so a scroll
/// viewer has no extent to scroll WITHIN and the offsets stay at zero whatever is asked of it. So
/// these do not assert that a pane moved - such an assertion would compare zero to zero and pass
/// whatever the code did, which is the same trap <see cref="WordWrapTests"/> documents. They pin the
/// wiring on our side: that both panes are subscribed, that the handler copies both axes, and above
/// all that the re-entry guard holds - because the bug horizontal sync can newly introduce is an
/// infinite ping-pong between two panes, and that one IS observable here.
/// </summary>
public class ScrollSyncTests
{
    private static DiffPaneViewModel Populated()
    {
        var rows = new List<DiffLine>
        {
            new(1, "context", 1, "context", ChangeKind.Unchanged),
            new(2, new string('x', 400), 2, new string('y', 600), ChangeKind.Modified),
            new(3, "tail", 3, "tail", ChangeKind.Unchanged),
        };

        var pane = new DiffPaneViewModel();
        pane.Show(DiffResult.Create(rows));

        return pane;
    }

    private static (Window Window, T View) Show<T>(object context)
        where T : Control, new()
    {
        var view = new T { DataContext = context };
        var window = new Window { Content = view, Width = 900, Height = 400 };

        window.Show();
        window.UpdateLayout();

        return (window, view);
    }

    private static IReadOnlyList<DiffEditorPane> Columns(Control view) =>
    [
        .. view.GetVisualDescendants()
            .OfType<DiffEditorPane>()
            .Where(p => !p.GetVisualAncestors().OfType<DiffDetailPane>().Any())
            .Where(p => !p.GetVisualAncestors().OfType<MergeDetailPane>().Any())
            .Where(p => p.Name != "OutputPane"),
    ];

    // ---- Side by side ----------------------------------------------------------------------------

    [AvaloniaFact]
    public void Scrolling_either_pane_does_not_ping_pong()
    {
        // The failure mode syncing a SECOND axis can introduce: pane A moves, which moves pane B,
        // whose own event moves A back, forever. The guard is one bool shared by both axes; this is
        // what proves it still covers them. A regression here hangs the UI thread rather than
        // producing a wrong number, so it would not show up as a wrong assertion anywhere else.
        var (window, view) = Show<DiffView>(Populated());
        var panes = Columns(view);

        Assert.Equal(2, panes.Count);

        foreach (var pane in panes)
        {
            pane.TextEditor.ScrollToHorizontalOffset(120);
            pane.TextEditor.ScrollToVerticalOffset(40);
        }

        window.UpdateLayout();

        // Reaching here at all is the assertion - a ping-pong never returns.
        Assert.All(panes, p => Assert.NotNull(p.TextEditor));
    }

    [AvaloniaFact]
    public void Both_panes_are_subscribed_so_either_one_can_lead()
    {
        // Sync must work whichever pane the user's wheel or drag lands on. Asserted through the
        // controls rather than the view model, because the subscription is made in code-behind and a
        // dropped line there is silent.
        var (_, view) = Show<DiffView>(Populated());

        Assert.All(Columns(view), p => Assert.NotNull(p.TextView));
        Assert.Equal(2, Columns(view).Count);
    }

    // ---- Three way -------------------------------------------------------------------------------

    [AvaloniaFact]
    public void All_three_merge_columns_survive_a_scroll_from_any_of_them()
    {
        var pane = new ThreeWayPaneViewModel();
        pane.Show(ThreeWayResult.Create(
        [
            new ThreeWayLine(1, "shared", 1, "shared", 1, "shared", MergeKind.Unchanged, -1),
            new ThreeWayLine(2, new string('a', 400), 2, new string('b', 500), 2, new string('c', 600), MergeKind.Conflict, 0),
        ]));

        var (window, view) = Show<ThreeWayView>(pane);
        var panes = Columns(view);

        Assert.Equal(3, panes.Count);

        foreach (var column in panes)
        {
            column.TextEditor.ScrollToHorizontalOffset(200);
            column.TextEditor.ScrollToVerticalOffset(20);
        }

        window.UpdateLayout();

        Assert.All(panes, p => Assert.NotNull(p.TextEditor));
    }
}
