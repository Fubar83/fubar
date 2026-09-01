using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Controls.Views;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// Word wrap, which the unified view has and the side-by-side view deliberately does not.
///
/// The rule worth protecting is the second half of that sentence. The two columns are aligned by
/// having the same number of visual lines - which is what makes scroll sync a plain offset copy - and
/// a line long enough to wrap on one side and not the other breaks it silently: the panes drift apart
/// by one line for every wrap above the viewport, and nothing throws.
///
/// As in <see cref="CollapseUnchangedTests"/>, nothing here asserts that text visually wraps. Headless
/// Avalonia never lays visual lines out, so such an assertion would compare zero to zero and pass
/// whatever the code did. What these pin is the wiring on our side of that boundary.
/// </summary>
public class WordWrapTests
{
    private static DiffPaneViewModel Populated()
    {
        var rows = new List<DiffLine>
        {
            new(1, "context", 1, "context", ChangeKind.Unchanged),
            new(2, new string('x', 400), 2, new string('y', 400), ChangeKind.Modified),
            new(3, "tail", 3, "tail", ChangeKind.Unchanged),
        };

        var pane = new DiffPaneViewModel();
        pane.Show(DiffResult.Create(rows));

        return pane;
    }

    private static AvaloniaEdit.TextEditor EditorIn(DiffEditorPane pane) =>
        pane.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().First();

    private static (Window Window, T View) Show<T>(DiffPaneViewModel pane)
        where T : Control, new()
    {
        var view = new T { DataContext = pane };
        var window = new Window { Content = view, Width = 900, Height = 400 };

        window.Show();
        window.UpdateLayout();

        return (window, view);
    }

    [AvaloniaFact]
    public void The_unified_editor_wraps_when_asked()
    {
        var pane = Populated();
        var (window, view) = Show<UnifiedView>(pane);

        var editorPane = view.GetVisualDescendants().OfType<DiffEditorPane>().Single();

        Assert.False(EditorIn(editorPane).WordWrap);

        pane.WordWrap = true;
        window.UpdateLayout();

        Assert.True(editorPane.WordWrap);
        Assert.True(EditorIn(editorPane).WordWrap);
    }

    [AvaloniaFact]
    public void Turning_it_off_again_stops_wrapping()
    {
        var pane = Populated();
        var (window, view) = Show<UnifiedView>(pane);

        pane.WordWrap = true;
        window.UpdateLayout();

        pane.WordWrap = false;
        window.UpdateLayout();

        Assert.False(EditorIn(view.GetVisualDescendants().OfType<DiffEditorPane>().Single()).WordWrap);
    }

    [AvaloniaFact]
    public void The_side_by_side_panes_never_wrap_however_the_setting_is_left()
    {
        // The one that matters. The two columns are aligned by row count; a wrapped line on one side
        // and not the other pulls them apart with no error anywhere.
        var pane = Populated();
        pane.WordWrap = true;

        var (window, view) = Show<DiffView>(pane);
        window.UpdateLayout();

        var editors = view.GetVisualDescendants().OfType<DiffEditorPane>().ToList();

        Assert.NotEmpty(editors);
        Assert.All(editors, p => Assert.False(p.WordWrap));
        Assert.All(editors, p => Assert.False(EditorIn(p).WordWrap));
    }

    [AvaloniaFact]
    public void A_wrapping_document_still_has_one_line_per_row()
    {
        // Wrapping is a VIEW state, exactly like folding: the document keeps every line, so the
        // unified view's own row indices - which its hunks and scrolling are expressed in - keep
        // meaning what they meant.
        var pane = Populated();
        pane.WordWrap = true;

        var (_, view) = Show<UnifiedView>(pane);

        var editor = EditorIn(view.GetVisualDescendants().OfType<DiffEditorPane>().Single());

        Assert.Equal(pane.UnifiedDocument.Document.Lines.Count, editor.Document.LineCount);
    }
}
