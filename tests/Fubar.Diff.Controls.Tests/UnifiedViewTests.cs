using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Controls.Views;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// The unified view's wiring, and the mode switching around it.
///
/// Its rows are not the comparison's rows, so the things worth pinning are the translations: that the
/// document reaching the editor is the unified one, that navigation scrolls to a unified row, and that
/// a click in it lands back on the right hunk.
/// </summary>
public class UnifiedViewTests
{
    private static DiffPaneViewModel Populated()
    {
        var rows = new List<DiffLine>
        {
            new(1, "context", 1, "context", ChangeKind.Unchanged),
            new(2, "old", 2, "new", ChangeKind.Modified),
            new(3, "tail", 3, "tail", ChangeKind.Unchanged),
        };

        var pane = new DiffPaneViewModel();
        pane.Show(DiffResult.Create(rows));

        return pane;
    }

    private static (Window Window, UnifiedView View) Show(DiffPaneViewModel pane)
    {
        var view = new UnifiedView { DataContext = pane };
        var window = new Window { Content = view, Width = 900, Height = 400 };

        window.Show();
        window.UpdateLayout();

        return (window, view);
    }

    private static DiffEditorPane EditorPane(UnifiedView view) =>
        view.GetVisualDescendants().OfType<DiffEditorPane>().Single();

    [AvaloniaFact]
    public void The_unified_document_reaches_the_editor()
    {
        var (_, view) = Show(Populated());

        Assert.Equal("context\nold\nnew\ntail", EditorPane(view).Document?.Text);
    }

    [AvaloniaFact]
    public void There_is_only_one_editor()
    {
        // The point of the view: no second column, no scroll sync, no fillers.
        var (_, view) = Show(Populated());

        Assert.Single(view.GetVisualDescendants().OfType<DiffEditorPane>());
    }

    [AvaloniaFact]
    public void Navigating_scrolls_to_the_unified_row_not_the_comparison_row()
    {
        // The hunk is comparison row 1 and unified row 1 here only by coincidence of the fixture; what
        // matters is that both are set, since either view may be the one on screen.
        var pane = Populated();
        Show(pane);

        pane.NextChangeCommand.Execute(null);

        Assert.Equal(0, pane.CurrentHunk);
        Assert.Equal(pane.UnifiedDocument.Hunks[0].StartIndex, pane.UnifiedScrollToRow);
    }

    [AvaloniaFact]
    public void A_click_in_the_unified_view_selects_the_right_hunk()
    {
        var pane = Populated();
        Show(pane);

        // Unified row 2 is the addition half of the modified row.
        pane.JumpToUnifiedRow(2);

        Assert.Equal(0, pane.CurrentHunk);
    }

    [AvaloniaFact]
    public void A_click_outside_the_document_is_ignored()
    {
        var pane = Populated();
        Show(pane);

        pane.JumpToUnifiedRow(999);
        pane.JumpToUnifiedRow(-1);

        Assert.Equal(-1, pane.CurrentHunk);
    }

    // ---- Mode switching -------------------------------------------------------------------------

    [AvaloniaFact]
    public void Side_by_side_is_the_default_for_content_that_is_not_json()
    {
        var pane = Populated();

        Assert.Equal(DiffViewMode.SideBySide, pane.ViewMode);
        Assert.True(pane.IsSideBySideViewVisible);
        Assert.False(pane.IsUnifiedViewVisible);
    }

    [AvaloniaFact]
    public void Only_one_view_is_visible_at_a_time()
    {
        var pane = Populated();

        pane.ViewMode = DiffViewMode.Unified;

        Assert.True(pane.IsUnifiedViewVisible);
        Assert.False(pane.IsSideBySideViewVisible);
        Assert.False(pane.IsJsonViewVisible);
    }

    [AvaloniaFact]
    public void Json_is_not_offered_for_content_that_is_not_json()
    {
        // Offering a mode that would then refuse to show anything is worse than not offering it.
        var pane = Populated();

        Assert.Equal([DiffViewMode.SideBySide, DiffViewMode.Unified], pane.AvailableViewModes);
    }

    [AvaloniaFact]
    public void The_close_up_hides_itself_in_the_unified_view_and_comes_back()
    {
        // In unified the two versions of a change are already one line apart, so a close-up would be
        // showing a copy of what is on screen. Remembered rather than forced off, so a user who turned
        // it off gets it left off.
        var pane = Populated();

        Assert.True(pane.IsDetailVisible);

        pane.ViewMode = DiffViewMode.Unified;
        Assert.False(pane.IsDetailVisible);

        pane.ViewMode = DiffViewMode.SideBySide;
        Assert.True(pane.IsDetailVisible);
    }

    [AvaloniaFact]
    public void A_close_up_the_user_turned_off_stays_off()
    {
        var pane = Populated();
        pane.IsDetailVisible = false;

        pane.ViewMode = DiffViewMode.Unified;
        pane.ViewMode = DiffViewMode.SideBySide;

        Assert.False(pane.IsDetailVisible);
    }

    [AvaloniaFact]
    public void The_unified_view_folds_in_its_own_coordinates()
    {
        var rows = new List<DiffLine>();
        for (var i = 0; i < 40; i++)
        {
            rows.Add(new DiffLine(i + 1, "same", i + 1, "same", ChangeKind.Unchanged));
        }

        rows.Add(new DiffLine(41, "old", 41, "new", ChangeKind.Modified));

        var pane = new DiffPaneViewModel();
        pane.Show(DiffResult.Create(rows));

        var (_, view) = Show(pane);

        Assert.NotEmpty(pane.UnifiedFolds);
        Assert.Equal(pane.UnifiedFolds, EditorPane(view).Folds);

        // The unified document is one line longer than the comparison here (the modified row split),
        // so a fold list computed against the other one would be wrong at the end.
        Assert.Equal(rows.Count + 1, pane.UnifiedDocument.Document.Lines.Count);
    }
}
