using Fubar.Diff.Core.Editing;

namespace Fubar.Diff.Core.Tests.Editing;

/// <summary>
/// Taking an edited diff pane back apart into the file it represents.
///
/// The pane shows the file with blank filler rows interleaved. Once it is editable, this rule is the
/// only thing standing between what the user typed and what gets written to disk, so the cases below
/// are the ones a person actually produces while editing rather than a sweep of the input space.
///
/// The rule was checked against a real AvaloniaEdit document before it was written here: anchors were
/// put on the filler lines, realistic edits applied, and the reconstruction compared to what a person
/// would expect. All of these mirror one of those.
/// </summary>
public class AlignedEditTests
{
    private static IReadOnlyList<string> File(string[] documentLines, params int[] fillerLines) =>
        AlignedEdit.ToFileLines(documentLines, new HashSet<int>(fillerLines));

    [Fact]
    public void An_untouched_pane_gives_back_the_file_it_was_built_from()
    {
        // "one", <filler>, "three"
        Assert.Equal(["one", "three"], File(["one", "", "three"], 2));
    }

    [Fact]
    public void A_pane_with_no_fillers_is_the_file()
    {
        Assert.Equal(["a", "b", "c"], File(["a", "b", "c"]));
    }

    [Fact]
    public void Text_typed_into_a_filler_becomes_a_real_line()
    {
        // The case that makes editing worth having: typing where the other side already has a line is
        // how you add one. The anchor is still on line 2, but the line is no longer empty.
        Assert.Equal(["one", "two", "three"], File(["one", "two", "three"], 2));
    }

    [Fact]
    public void A_blank_line_the_user_typed_is_kept()
    {
        // Pressing Enter creates a line with no anchor on it. It is content, and dropping it would
        // silently refuse to let anyone add a blank line to a file.
        Assert.Equal(["one", "", "three"], File(["one", "", "", "three"], 3));
    }

    [Fact]
    public void A_blank_line_the_FILE_already_had_is_kept()
    {
        // The file itself contains an empty line; it is not a filler and never was.
        Assert.Equal(["one", "", "three"], File(["one", "", "three"]));
    }

    [Fact]
    public void Several_fillers_in_a_row_all_go()
    {
        Assert.Equal(["one", "five"], File(["one", "", "", "", "five"], 2, 3, 4));
    }

    [Fact]
    public void A_filler_at_the_very_start_goes()
    {
        Assert.Equal(["one"], File(["", "one"], 1));
    }

    [Fact]
    public void A_filler_at_the_very_end_goes()
    {
        Assert.Equal(["one"], File(["one", ""], 2));
    }

    [Fact]
    public void An_anchor_pointing_at_a_line_that_now_has_text_does_not_eat_it()
    {
        // Deleting a filler line leaves its anchor sitting on whatever moved up into its place. The
        // emptiness half of the rule is what stops that line being dropped as well - without it, this
        // silently deletes the user's code.
        Assert.Equal(["one", "three"], File(["one", "three"], 2));
    }

    [Fact]
    public void An_empty_document_is_an_empty_file()
    {
        Assert.Empty(File([]));
        Assert.Empty(File(["", ""], 1, 2));
    }

    [Fact]
    public void A_line_of_only_whitespace_is_not_a_filler()
    {
        // Fillers are EMPTY, not blank. A line of spaces is content - it may be trailing whitespace
        // the user is looking at on purpose, and it is certainly not ours to remove.
        Assert.Equal(["one", "   ", "three"], File(["one", "   ", "three"], 2));
    }

    // ---- Caret mapping ----------------------------------------------------------------------------

    [Fact]
    public void A_caret_maps_to_the_file_line_it_is_on()
    {
        var fillers = new HashSet<int> { 2 };

        Assert.Equal(1, AlignedEdit.ToFileLine(1, fillers));
        Assert.Equal(2, AlignedEdit.ToFileLine(3, fillers));
    }

    [Fact]
    public void A_caret_on_a_filler_reports_the_line_it_would_push_down()
    {
        // A filler has no file line of its own. Answering with the following one puts the caret back
        // just above the line the user was pointing at, which is where they were.
        Assert.Equal(2, AlignedEdit.ToFileLine(2, new HashSet<int> { 2 }));
    }

    [Fact]
    public void A_file_line_maps_back_to_the_document_line_showing_it()
    {
        // The reverse direction, against a DIFFERENT set of fillers - which is the whole point, since
        // the alignment either side of an edit is not the same alignment.
        Assert.Equal(1, AlignedEdit.ToDocumentLine(1, [3], 5));
        Assert.Equal(2, AlignedEdit.ToDocumentLine(2, [3], 5));
        Assert.Equal(4, AlignedEdit.ToDocumentLine(3, [3], 5));
    }

    [Fact]
    public void Fillers_above_the_target_push_it_down_one_each()
    {
        Assert.Equal(4, AlignedEdit.ToDocumentLine(2, [1, 2], 10));
    }

    [Fact]
    public void A_caret_survives_the_fillers_moving()
    {
        // The round trip the pane performs after every re-alignment. The caret is at the end of
        // "three" - document line 3 while the filler is at line 2, document line 2 once it moves to
        // line 3 - and it must still be at the end of "three" afterwards.
        var before = new HashSet<int> { 2 };
        var fileLine = AlignedEdit.ToFileLine(documentLine: 3, before);

        var after = AlignedEdit.ToDocumentLine(fileLine, [3], documentLineCount: 4);

        Assert.Equal(2, fileLine);
        Assert.Equal(2, after);
    }

    [Fact]
    public void A_caret_beyond_the_new_document_is_clamped_into_it()
    {
        // The document can be shorter after a re-alignment, and a caret offset past the end is an
        // exception rather than a misplacement.
        Assert.Equal(2, AlignedEdit.ToDocumentLine(9, [], 2));
        Assert.Equal(1, AlignedEdit.ToDocumentLine(0, [], 3));
    }
}
