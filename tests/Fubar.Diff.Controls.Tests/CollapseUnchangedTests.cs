using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Controls.Views;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// That the folds computed in Core reach the editors, and that folding stays a VIEW state.
///
/// What is deliberately not asserted here is that AvaloniaEdit visually hides the lines. Headless
/// Avalonia never lays visual lines out, so any assertion phrased in terms of them compares zero to
/// zero and passes whatever the code does - a test that cannot fail is worse than no test, because it
/// reads like coverage. The hiding is the library's job and is verified by running the app; what these
/// pin is everything on our side of that boundary, which is where the mistakes would be.
/// </summary>
public class CollapseUnchangedTests
{
    /// <summary>A comparison with one change surrounded by plenty of identical context.</summary>
    private static DiffPaneViewModel Populated(int contextRows = 30)
    {
        var rows = new List<DiffLine>();

        for (var i = 0; i < contextRows; i++)
        {
            rows.Add(new DiffLine(rows.Count + 1, "same", rows.Count + 1, "same", ChangeKind.Unchanged));
        }

        rows.Add(new DiffLine(rows.Count + 1, "before", rows.Count + 1, "after", ChangeKind.Modified));

        for (var i = 0; i < contextRows; i++)
        {
            rows.Add(new DiffLine(rows.Count + 1, "same", rows.Count + 1, "same", ChangeKind.Unchanged));
        }

        var pane = new DiffPaneViewModel();
        pane.Show(DiffResult.Create(rows));

        return pane;
    }

    private static (Window Window, DiffView View) Show(DiffPaneViewModel pane)
    {
        var view = new DiffView { DataContext = pane };
        var window = new Window { Content = view, Width = 1000, Height = 500 };

        window.Show();
        window.UpdateLayout();

        return (window, view);
    }

    /// <summary>The two main side-by-side editors, excluding the close-up's.</summary>
    private static IReadOnlyList<DiffEditorPane> Columns(DiffView view) =>
    [
        .. view.GetVisualDescendants()
            .OfType<DiffEditorPane>()
            .Where(pane => !pane.GetVisualAncestors().OfType<DiffDetailPane>().Any()),
    ];

    /// <summary>
    /// The pane's editor, found through the visual tree rather than through a property.
    /// <c>DiffEditorPane.TextEditor</c> is internal, and it is internal for a good reason - nothing
    /// outside the control library should be reaching into an editor - so a test widens the tree walk
    /// rather than the API.
    /// </summary>
    private static AvaloniaEdit.TextEditor EditorIn(DiffEditorPane pane) =>
        pane.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().First();

    [AvaloniaFact]
    public void The_folds_reach_both_editors()
    {
        var pane = Populated();
        var (_, view) = Show(pane);

        var columns = Columns(view);

        Assert.Equal(2, columns.Count);
        Assert.NotEmpty(pane.Folds);
        Assert.Equal(pane.Folds, columns[0].Folds);
        Assert.Equal(pane.Folds, columns[1].Folds);
    }

    [AvaloniaFact]
    public void Both_panes_are_given_the_very_same_ranges()
    {
        // The invariant the whole side-by-side view rests on. Identical folds over documents that
        // already have identical row counts means identical visual lines, so scroll sync stays the
        // plain offset copy it has always been. Two lists that merely happen to match today would not
        // guarantee that; being the same list does.
        var (_, view) = Show(Populated());
        var columns = Columns(view);

        Assert.Same(columns[0].Folds, columns[1].Folds);
    }

    [AvaloniaFact]
    public void Folding_hides_lines_rather_than_removing_them()
    {
        // The distinction that keeps every row index in the app meaningful: a fold is a view state, so
        // the document still has every line and DiffResult.Lines[i] is still editor line i. An
        // "optimisation" that filtered rows out of the document instead would break the diff map,
        // navigation, the gutter and the merge all at once.
        var pane = Populated();
        var (_, view) = Show(pane);

        var editor = EditorIn(Columns(view)[0]);

        Assert.NotEmpty(pane.Folds);
        Assert.Equal(pane.TotalLines, editor.Document.LineCount);
    }

    [AvaloniaFact]
    public void The_changed_row_is_never_hidden()
    {
        var pane = Populated();
        Show(pane);

        foreach (var fold in pane.Folds)
        {
            Assert.False(
                fold.StartRow <= 30 && 30 <= fold.EndRow,
                "the change at row 30 must stay visible");
        }
    }

    [AvaloniaFact]
    public void Turning_collapsing_off_clears_every_fold()
    {
        var pane = Populated();
        var (window, view) = Show(pane);

        Assert.NotEmpty(pane.Folds);

        pane.CollapseUnchanged = false;
        window.UpdateLayout();

        Assert.Empty(pane.Folds);
        Assert.All(Columns(view), column => Assert.Empty(column.Folds ?? []));
    }

    [AvaloniaFact]
    public void More_context_folds_less()
    {
        var pane = Populated();
        Show(pane);

        var tight = pane.Folds.Sum(f => f.Length);

        pane.ContextLines = 10;
        var loose = pane.Folds.Sum(f => f.Length);

        Assert.True(loose < tight, $"more context should hide fewer lines, but {loose} >= {tight}");
    }

    [AvaloniaFact]
    public void A_new_comparison_recomputes_the_folds()
    {
        // Folds are document offsets; carrying the previous comparison's over would fold the wrong
        // lines, or throw for a shorter document.
        var pane = Populated();
        var (window, view) = Show(pane);

        var before = pane.Folds.Count;

        pane.Show(DiffResult.Create(
        [
            new DiffLine(1, "a", 1, "b", ChangeKind.Modified),
        ]));
        window.UpdateLayout();

        Assert.True(before > 0);
        Assert.Empty(pane.Folds);
        Assert.All(Columns(view), column => Assert.Empty(column.Folds ?? []));
    }

    [AvaloniaFact]
    public void The_close_up_never_folds()
    {
        // It shows one hunk. There is no long stretch of context in it to hide, and a fold margin on a
        // three-line excerpt is pure chrome.
        var pane = Populated();
        pane.CurrentHunk = 0;

        var (window, view) = Show(pane);
        window.UpdateLayout();

        var closeUps = view.GetVisualDescendants()
            .OfType<DiffEditorPane>()
            .Where(p => p.GetVisualAncestors().OfType<DiffDetailPane>().Any());

        Assert.All(closeUps, p => Assert.Null(p.Folds));
    }
}
