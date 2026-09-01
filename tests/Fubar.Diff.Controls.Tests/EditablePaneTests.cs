using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Fubar.Diff.Controls.Views;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// An editable diff pane, against a real AvaloniaEdit document.
///
/// The rule for reconstructing the file is pure and tested in Core. What can only be checked here is
/// that it survives contact with the editor: that the anchors really do follow the text, that a
/// re-alignment leaves the caret where the user left it, and that one Ctrl+Z takes back an edit and
/// the re-alignment it caused together. Both of those last two were got wrong first - restoring the
/// caret by raw offset silently lands on a different line, and hiding the re-alignment by swapping the
/// undo stack destroys the stack - so they are pinned rather than trusted.
/// </summary>
public class EditablePaneTests
{
    private static DiffResult Result(params DiffLine[] rows) => DiffResult.Create(rows);

    private static DiffLine Same(int l, int r, string text) => new(l, text, r, text, ChangeKind.Unchanged);

    private static DiffLine OnlyLeft(int l, string text) => new(l, text, null, null, ChangeKind.Deleted);

    private static (Window Window, DiffEditorPane Pane, TextEditor Editor) Show(
        DiffResult result,
        DiffSide side = DiffSide.Right,
        bool editable = true)
    {
        var pane = new DiffEditorPane { IsEditable = editable };
        var window = new Window { Content = pane, Width = 800, Height = 400 };

        window.Show();
        window.UpdateLayout();

        pane.Document = AlignedText.Build(result, side);
        window.UpdateLayout();

        return (window, pane, pane.GetVisualDescendants().OfType<TextEditor>().First());
    }

    /// <summary>"one", filler, "three" on the right - the left has a line the right does not.</summary>
    private static DiffResult WithFillerAtRowTwo() => Result(
        Same(1, 1, "one"),
        OnlyLeft(2, "two"),
        Same(3, 2, "three"));

    /// <summary>The same three lines, with the left-only row moved to the top.</summary>
    private static DiffResult WithFillerAtRowOne() => Result(
        OnlyLeft(1, "two"),
        Same(2, 1, "one"),
        Same(3, 2, "three"));

    [AvaloniaFact]
    public void A_read_only_pane_stays_read_only()
    {
        var (_, _, editor) = Show(WithFillerAtRowTwo(), editable: false);

        Assert.True(editor.IsReadOnly);
    }

    [AvaloniaFact]
    public void An_editable_pane_lets_the_editor_be_written()
    {
        var (_, _, editor) = Show(WithFillerAtRowTwo());

        Assert.False(editor.IsReadOnly);
    }

    [AvaloniaFact]
    public void The_pane_hands_back_the_FILE_and_not_what_is_on_screen()
    {
        // The document has three lines, the file has two. Saving what the editor holds would write a
        // blank line into the user's file, which is the whole reason this was read-only for so long.
        var (_, pane, editor) = Show(WithFillerAtRowTwo());

        Assert.Equal(3, editor.Document.LineCount);
        Assert.Equal(["one", "three"], pane.ReadFileLines());
    }

    [AvaloniaFact]
    public void Typing_is_reflected_in_the_file()
    {
        var (_, pane, editor) = Show(WithFillerAtRowTwo());

        editor.Document.Insert(editor.Document.GetLineByNumber(3).EndOffset, "!");

        Assert.Equal(["one", "three!"], pane.ReadFileLines());
    }

    [AvaloniaFact]
    public void Typing_into_a_filler_adds_a_line_to_the_file()
    {
        // The case that makes an aligned pane worth editing at all: the blank row opposite the other
        // side's line is exactly where a new line belongs.
        var (_, pane, editor) = Show(WithFillerAtRowTwo());

        editor.Document.Insert(editor.Document.GetLineByNumber(2).Offset, "two");

        Assert.Equal(["one", "two", "three"], pane.ReadFileLines());
    }

    [AvaloniaFact]
    public void A_blank_line_the_user_types_is_kept()
    {
        var (_, pane, editor) = Show(WithFillerAtRowTwo());

        editor.Document.Insert(editor.Document.GetLineByNumber(1).EndOffset, "\n");

        Assert.Equal(["one", "", "three"], pane.ReadFileLines());
    }

    [AvaloniaFact]
    public void Deleting_a_line_removes_it_from_the_file()
    {
        var (_, pane, editor) = Show(WithFillerAtRowTwo());

        var line = editor.Document.GetLineByNumber(3);
        editor.Document.Remove(line.Offset - 1, line.TotalLength + 1);

        Assert.Equal(["one"], pane.ReadFileLines());
    }

    [AvaloniaFact]
    public void Selecting_across_a_filler_and_replacing_keeps_what_replaced_it()
    {
        // The filler's anchor is destroyed, so the text that took its place is the user's and must
        // survive. Dropping it here would delete code nobody asked to delete.
        var (_, pane, editor) = Show(WithFillerAtRowTwo());

        editor.Document.Replace(2, editor.Document.TextLength - 2, "Z");

        Assert.Equal(["onZ"], pane.ReadFileLines());
    }

    [AvaloniaFact]
    public void The_users_edits_are_reported_and_the_apps_are_not()
    {
        var (_, pane, editor) = Show(WithFillerAtRowTwo());

        var edits = 0;
        pane.Edited += (_, _) => edits++;

        editor.Document.Insert(0, "x");
        Assert.Equal(1, edits);

        // A new alignment is the app writing, not the user. Counting it would make the host re-diff
        // its own re-diff, forever.
        pane.Document = AlignedText.Build(WithFillerAtRowOne(), DiffSide.Right);

        Assert.Equal(1, edits);
    }

    [AvaloniaFact]
    public void A_read_only_pane_reports_nothing_even_when_its_document_changes()
    {
        var (_, pane, _) = Show(WithFillerAtRowTwo(), editable: false);

        var edits = 0;
        pane.Edited += (_, _) => edits++;

        pane.Document = AlignedText.Build(WithFillerAtRowOne(), DiffSide.Right);

        Assert.Equal(0, edits);
    }

    // ---- Re-alignment -----------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_new_alignment_moves_the_fillers_and_keeps_the_file()
    {
        var (window, pane, editor) = Show(WithFillerAtRowTwo());

        pane.Document = AlignedText.Build(WithFillerAtRowOne(), DiffSide.Right);
        window.UpdateLayout();

        Assert.Equal(3, editor.Document.LineCount);
        Assert.Equal(["one", "three"], pane.ReadFileLines());

        // The blank row is now at the top, where the new alignment wants it.
        Assert.Equal(string.Empty, editor.Document.GetText(editor.Document.GetLineByNumber(1)));
    }

    [AvaloniaFact]
    public void The_caret_stays_where_the_user_left_it_across_a_re_alignment()
    {
        // The one that was got wrong first. The filler moves from row 2 to row 1, so the line the
        // caret is on changes number - and restoring by raw offset lands it somewhere else entirely.
        var (window, pane, editor) = Show(WithFillerAtRowTwo());

        // End of "three": document line 3, which is file line 2.
        editor.TextArea.Caret.Line = 3;
        editor.TextArea.Caret.Column = 6;

        pane.Document = AlignedText.Build(WithFillerAtRowOne(), DiffSide.Right);
        window.UpdateLayout();

        var line = editor.Document.GetLineByNumber(editor.TextArea.Caret.Line);

        Assert.Equal("three", editor.Document.GetText(line));
        Assert.Equal(6, editor.TextArea.Caret.Column);
    }

    [AvaloniaFact]
    public void One_undo_takes_back_the_edit_AND_the_re_alignment_it_caused()
    {
        // Its own undo group would make the user press Ctrl+Z twice for one change; hiding it by
        // swapping the undo stack destroys the stack outright. Continuing the group is what is left,
        // and it is also the behaviour a person expects.
        var (window, pane, editor) = Show(WithFillerAtRowTwo());

        editor.Document.Insert(editor.Document.GetLineByNumber(3).EndOffset, "!");
        Assert.Equal(["one", "three!"], pane.ReadFileLines());

        // The alignment a real re-diff would produce: computed FROM the edited text, so it contains
        // the user's "!" - and the left-only row has slid to the top, which is what makes this a
        // re-alignment rather than a no-op.
        pane.Document = AlignedText.Build(
            Result(
                OnlyLeft(1, "two"),
                Same(2, 1, "one"),
                new DiffLine(3, "three", 2, "three!", ChangeKind.Modified)),
            DiffSide.Right);

        window.UpdateLayout();

        editor.Document.UndoStack.Undo();

        // Back to the text before the edit - and, crucially, to the FILE before the edit. The blank
        // row is a filler again, not a line the user typed. Anchors alone would get this wrong: they
        // describe the layout that was in force when they were made, which the undo has just left.
        Assert.Equal(["one", "three"], pane.ReadFileLines());
        Assert.False(editor.Document.UndoStack.CanUndo);
    }

    [AvaloniaFact]
    public void Loading_a_comparison_is_not_undoable()
    {
        // Setting the document is the app opening a file, not the user changing one. Left on the undo
        // stack, one Ctrl+Z in a freshly opened comparison walks back past the load and empties the
        // pane - which looks exactly like the tool having deleted the file's contents.
        var (_, _, editor) = Show(WithFillerAtRowTwo());

        Assert.False(editor.Document.UndoStack.CanUndo);
    }

    [AvaloniaFact]
    public void Undoing_several_edits_in_a_row_keeps_giving_the_right_file()
    {
        // Anchors describe the layout in force when they were made, so undoing past more than one
        // re-alignment cannot be answered from them. Each undo lands on a document this pane has
        // shown before, which is what the record of past layouts is for.
        var (window, pane, editor) = Show(WithFillerAtRowTwo());

        editor.Document.Insert(editor.Document.GetLineByNumber(3).EndOffset, "!");
        pane.Document = AlignedText.Build(
            Result(
                OnlyLeft(1, "two"),
                Same(2, 1, "one"),
                new DiffLine(3, "three", 2, "three!", ChangeKind.Modified)),
            DiffSide.Right);
        window.UpdateLayout();

        editor.Document.Insert(editor.Document.GetLineByNumber(3).EndOffset, "?");
        pane.Document = AlignedText.Build(
            Result(
                Same(1, 1, "one"),
                OnlyLeft(2, "two"),
                new DiffLine(3, "three", 2, "three!?", ChangeKind.Modified)),
            DiffSide.Right);
        window.UpdateLayout();

        Assert.Equal(["one", "three!?"], pane.ReadFileLines());

        editor.Document.UndoStack.Undo();
        Assert.Equal(["one", "three!"], pane.ReadFileLines());

        editor.Document.UndoStack.Undo();
        Assert.Equal(["one", "three"], pane.ReadFileLines());
    }

    [AvaloniaFact]
    public void An_alignment_that_does_not_match_the_edited_text_is_refused_rather_than_patched()
    {
        // A stale alignment - one computed before the edit - describes a different file. Patching it
        // in would show the new arrangement while keeping the old content, so the pane replaces the
        // document instead and the caller's mistake is visible rather than silent.
        var (window, pane, editor) = Show(WithFillerAtRowTwo());

        editor.Document.Insert(editor.Document.GetLineByNumber(3).EndOffset, "!");

        pane.Document = AlignedText.Build(WithFillerAtRowOne(), DiffSide.Right);
        window.UpdateLayout();

        Assert.Equal(["one", "three"], pane.ReadFileLines());
    }

    [AvaloniaFact]
    public void A_comparison_of_different_content_replaces_the_document_outright()
    {
        // Not a re-alignment - the file's own lines changed - so patching is refused and the document
        // is replaced. Losing the caret there is right: nothing the user was in the middle of survives
        // opening a different comparison anyway.
        var (window, pane, editor) = Show(WithFillerAtRowTwo());

        pane.Document = AlignedText.Build(
            Result(Same(1, 1, "completely"), Same(2, 2, "different")),
            DiffSide.Right);

        window.UpdateLayout();

        Assert.Equal(["completely", "different"], pane.ReadFileLines());
        Assert.Equal(2, editor.Document.LineCount);
    }

    [AvaloniaFact]
    public void An_empty_comparison_leaves_an_empty_pane()
    {
        var (_, pane, editor) = Show(DiffResult.Empty);

        Assert.Empty(pane.ReadFileLines());
        Assert.Equal(string.Empty, editor.Document.Text);
    }

    [AvaloniaFact]
    public void The_left_side_is_editable_the_same_way()
    {
        // Both panes are editable, so the mirror image has to work too.
        var (_, pane, editor) = Show(
            Result(Same(1, 1, "one"), new DiffLine(null, null, 2, "added", ChangeKind.Inserted), Same(2, 3, "three")),
            DiffSide.Left);

        Assert.Equal(["one", "three"], pane.ReadFileLines());

        editor.Document.Insert(editor.Document.GetLineByNumber(2).Offset, "mine");

        Assert.Equal(["one", "mine", "three"], pane.ReadFileLines());
    }
}
