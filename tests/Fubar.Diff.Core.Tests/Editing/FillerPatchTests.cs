using Fubar.Diff.Core.Editing;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests.Editing;

/// <summary>
/// Turning one alignment of a side into another by moving blank lines.
///
/// This is what saves the caret, the selection and the undo history after every edit: re-diffing
/// produces a new alignment, and handing the editor a whole new document would throw all three away
/// mid-sentence. The file's own lines are identical either side of a re-alignment, so the difference
/// is only ever where the fillers sit.
///
/// The edits are expressed in the line numbers the document will have WHILE they are applied in
/// order, which is what lets a caller run the list straight through. Several tests below exist purely
/// to pin that, because it is the part that is easy to get subtly wrong and hard to see.
/// </summary>
public class FillerPatchTests
{
    /// <summary>Reads a compact shape: '.' is a real line, 'f' is a filler.</summary>
    private static bool[] Shape(string shape) => [.. shape.Select(c => c == 'f')];

    private static IReadOnlyList<FillerEdit>? Patch(string from, string to) =>
        FillerPatch.Compute(Shape(from), Shape(to));

    /// <summary>Applies a patch the way the editor would, so the result can be checked.</summary>
    private static string Apply(string from, IReadOnlyList<FillerEdit> edits)
    {
        var lines = new List<char>(from);

        foreach (var edit in edits)
        {
            var index = edit.LineNumber - 1;

            if (edit.Kind == FillerEditKind.InsertBlank)
            {
                lines.Insert(index, 'f');
            }
            else
            {
                lines.RemoveAt(index);
            }
        }

        return new string([.. lines]);
    }

    [Fact]
    public void An_unchanged_alignment_needs_no_edits()
    {
        Assert.Empty(Patch("..f..", "..f..")!);
    }

    [Fact]
    public void A_filler_that_is_no_longer_wanted_is_removed()
    {
        var edits = Patch("..f..", "....")!;

        Assert.Equal([new FillerEdit(3, FillerEditKind.RemoveLine)], edits);
        Assert.Equal("....", Apply("..f..", edits));
    }

    [Fact]
    public void A_filler_that_is_now_wanted_is_inserted()
    {
        var edits = Patch("....", "..f..")!;

        Assert.Equal([new FillerEdit(3, FillerEditKind.InsertBlank)], edits);
        Assert.Equal("..f..", Apply("....", edits));
    }

    [Fact]
    public void A_filler_that_moved_is_one_removal_and_one_insertion()
    {
        var edits = Patch("f....", "....f")!;

        Assert.Equal("....f", Apply("f....", edits));
    }

    [Fact]
    public void The_line_numbers_are_the_ones_the_document_has_WHILE_the_edits_run()
    {
        // Two insertions: the second is numbered against a document the first has already lengthened.
        // Numbering both against the original would put the second one a line too high.
        var edits = Patch("...", "f.f..")!;

        Assert.Equal(
            [new FillerEdit(1, FillerEditKind.InsertBlank), new FillerEdit(3, FillerEditKind.InsertBlank)],
            edits);

        Assert.Equal("f.f..", Apply("...", edits));
    }

    [Fact]
    public void Two_removals_in_a_row_address_the_same_line_number_twice()
    {
        // Removing a line leaves the next one at the SAME number, so consecutive removals repeat it.
        // Decrementing here would skip every other line.
        var edits = Patch(".ff.", "..")!;

        Assert.Equal(
            [new FillerEdit(2, FillerEditKind.RemoveLine), new FillerEdit(2, FillerEditKind.RemoveLine)],
            edits);

        Assert.Equal("..", Apply(".ff.", edits));
    }

    /// <summary>
    /// Every pair here has the SAME number of real lines on both sides, because that is what a
    /// re-alignment is: the file did not change, only where the blanks sit. A pair that does not is
    /// not a valid input, and <see cref="It_refuses_when_the_two_differ_by_more_than_fillers"/> covers
    /// what happens then.
    /// </summary>
    [Theory]
    [InlineData("....", "f....f")]
    [InlineData("f.f.f", "..")]
    [InlineData(".....", "ff.....")]
    [InlineData("..ff..", "..f..f")]
    [InlineData("", "ff")]
    [InlineData("ff", "")]
    [InlineData(".f.f.f.", "....ff")]
    [InlineData("f...", "...f")]
    public void Applying_a_patch_produces_exactly_the_alignment_that_was_asked_for(string from, string to)
    {
        var edits = Patch(from, to);

        Assert.NotNull(edits);
        Assert.Equal(to, Apply(from, edits));
    }

    [Fact]
    public void It_refuses_when_the_two_differ_by_more_than_fillers()
    {
        // The premise is that the file's own lines are unchanged. If they are not, patching would
        // silently drop or duplicate the user's text - so it says no and the caller replaces the
        // document wholesale, losing the caret but never the content.
        Assert.Null(Patch("...", "...."));
        Assert.Null(Patch("....", "..."));
    }

    // ---- Reading the flags off a real alignment ---------------------------------------------------

    [Fact]
    public void A_row_with_no_source_number_is_a_filler()
    {
        var result = DiffResult.Create(
        [
            new DiffLine(1, "a", 1, "a", ChangeKind.Unchanged),
            new DiffLine(2, "gone", null, null, ChangeKind.Deleted),
            new DiffLine(3, "b", 2, "b", ChangeKind.Unchanged),
        ]);

        var right = AlignedText.Build(result, DiffSide.Right);
        var flags = FillerPatch.FillerFlags(right.Lines);

        Assert.Equal([false, true, false], flags);
        Assert.Equal([2], FillerPatch.FillerLines(flags));
    }

    [Fact]
    public void The_side_that_HAS_the_line_has_no_filler_there()
    {
        var result = DiffResult.Create(
        [
            new DiffLine(1, "a", 1, "a", ChangeKind.Unchanged),
            new DiffLine(2, "gone", null, null, ChangeKind.Deleted),
        ]);

        Assert.Equal(
            [false, false],
            FillerPatch.FillerFlags(AlignedText.Build(result, DiffSide.Left).Lines));
    }

    [Fact]
    public void A_real_realignment_round_trips()
    {
        // A line deleted from the left moves the right side's filler down one row. The patch has to
        // turn the old right-hand alignment into the new one exactly.
        var before = DiffResult.Create(
        [
            new DiffLine(1, "gone", null, null, ChangeKind.Deleted),
            new DiffLine(2, "a", 1, "a", ChangeKind.Unchanged),
            new DiffLine(3, "b", 2, "b", ChangeKind.Unchanged),
        ]);

        var after = DiffResult.Create(
        [
            new DiffLine(1, "a", 1, "a", ChangeKind.Unchanged),
            new DiffLine(2, "gone", null, null, ChangeKind.Deleted),
            new DiffLine(3, "b", 2, "b", ChangeKind.Unchanged),
        ]);

        var current = FillerPatch.FillerFlags(AlignedText.Build(before, DiffSide.Right).Lines);
        var wanted = FillerPatch.FillerFlags(AlignedText.Build(after, DiffSide.Right).Lines);

        var edits = FillerPatch.Compute(current, wanted);

        Assert.NotNull(edits);
        Assert.Equal("f..", string.Concat(current.Select(f => f ? 'f' : '.')));
        Assert.Equal(".f.", string.Concat(wanted.Select(f => f ? 'f' : '.')));
        Assert.Equal(".f.", Apply("f..", edits));
    }
}
