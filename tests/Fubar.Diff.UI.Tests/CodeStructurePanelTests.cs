using Fubar.Diff.Core.Code;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// The structure panel's own behaviour: that a member resolves to the aligned row that shows it, and
/// that presentation-only changes are marked as such so the view can quieten them.
///
/// The matching that produces the changes is tested in Infrastructure; what is under test here is the
/// bridge back to the text view, which is the half that can silently point at the wrong line.
/// </summary>
public class CodeStructurePanelTests
{
    private static CodeNode Member(string name, int line) =>
        new(CodeMemberKind.Method, name, name + "()", new SourceSpan(line, 1, line + 2, 2), []);

    /// <summary>Three rows, with the two sides' line numbers deliberately out of step.</summary>
    private static DiffResult Rows() => DiffResult.Create(
    [
        new DiffLine(1, "a", 1, "a", ChangeKind.Unchanged),
        new DiffLine(2, "b", null, null, ChangeKind.Deleted),
        new DiffLine(3, "c", 2, "C", ChangeKind.Modified),
    ]);

    private static CodeStructureViewModel Shown(params CodeChange[] changes)
    {
        var panel = new CodeStructureViewModel();

        panel.Show(changes, CodeStructureSummary.Of(changes), Rows(), null);

        return panel;
    }

    [Fact]
    public void A_change_resolves_to_the_row_carrying_its_RIGHT_hand_line()
    {
        // The right side is the file as it now is, and the one a reader is deciding about. Right line
        // 2 is row 2, not row 1 - which is what a naive "line minus one" would give.
        var panel = Shown(new CodeChange("Total()", CodeChangeKind.Modified, Member("Total", 3), Member("Total", 2)));

        Assert.Equal(2, Assert.Single(panel.Items).Row);
    }

    [Fact]
    public void A_removed_member_falls_back_to_where_it_used_to_be()
    {
        // There is no right side to point at, and refusing to navigate would leave the one kind of
        // change the reader most wants to look at unreachable from the panel.
        var panel = Shown(new CodeChange("Gone()", CodeChangeKind.Removed, Member("Gone", 2), null));

        Assert.Equal(1, Assert.Single(panel.Items).Row);
    }

    [Fact]
    public void Picking_a_member_asks_the_host_to_scroll_there()
    {
        var panel = Shown(new CodeChange("Total()", CodeChangeKind.Modified, Member("Total", 3), Member("Total", 2)));

        var jumped = -1;
        panel.JumpRequested += (_, row) => jumped = row;

        panel.SelectedItem = panel.Items[0];

        Assert.Equal(2, jumped);
    }

    [Fact]
    public void Clearing_the_selection_asks_for_nothing()
    {
        // Show() clears it, and a scroll on every new comparison would fight whatever the user was
        // looking at.
        var panel = Shown(new CodeChange("Total()", CodeChangeKind.Modified, Member("Total", 3), Member("Total", 2)));

        panel.SelectedItem = panel.Items[0];

        var jumps = 0;
        panel.JumpRequested += (_, _) => jumps++;

        panel.SelectedItem = null;

        Assert.Equal(0, jumps);
    }

    [Fact]
    public void A_reformatted_or_moved_member_is_marked_as_presentational()
    {
        var panel = Shown(
            new CodeChange("A()", CodeChangeKind.Cosmetic, Member("A", 1), Member("A", 1)),
            new CodeChange("B()", CodeChangeKind.Moved, Member("B", 1), Member("B", 1)) { IsMoved = true },
            new CodeChange("C()", CodeChangeKind.Modified, Member("C", 1), Member("C", 1)));

        Assert.True(panel.Items[0].IsPresentational);
        Assert.True(panel.Items[1].IsPresentational);
        Assert.False(panel.Items[2].IsPresentational);
    }

    [Fact]
    public void The_headline_is_only_claimed_when_something_was_actually_found()
    {
        // "No functional changes" about two identical files is technically true and reads as though a
        // difference had been found and dismissed.
        Assert.False(Shown().NoFunctionalChange);
        Assert.Equal(string.Empty, Shown().Caption);

        var reformatted = Shown(new CodeChange("A()", CodeChangeKind.Cosmetic, Member("A", 1), Member("A", 1)));

        Assert.True(reformatted.NoFunctionalChange);
    }

    [Fact]
    public void A_skip_reason_is_only_shown_when_there_is_nothing_else_to_show()
    {
        var panel = new CodeStructureViewModel();

        panel.Show([], CodeStructureSummary.None, Rows(), "The files are too large.");
        Assert.Equal("The files are too large.", panel.Message);

        var change = new CodeChange("A()", CodeChangeKind.Modified, Member("A", 1), Member("A", 1));
        panel.Show([change], CodeStructureSummary.Of([change]), Rows(), "The files are too large.");

        Assert.Null(panel.Message);
    }

    [Fact]
    public void The_indent_comes_from_the_change_and_never_from_the_path()
    {
        // Counting dots says a top-level `using System.Collections.Generic` is two levels deep,
        // because a namespace name has dots of its own.
        var use = new CodeChange(
            "System.Collections.Generic",
            CodeChangeKind.Added,
            null,
            new CodeNode(CodeMemberKind.Import, "System.Collections.Generic", "System.Collections.Generic", new SourceSpan(1, 1, 1, 2), []));

        var panel = Shown(use);

        Assert.Equal(0, panel.Items[0].Depth);
        Assert.Equal(0, panel.Items[0].Indent.Left);
    }
}
