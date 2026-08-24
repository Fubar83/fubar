using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// What actually gets written to the user's file. Every one of these is a data-loss scenario if it
/// regresses, which is why merge lives in Core as a pure function rather than in a view model.
/// </summary>
public class MergedDocumentTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    private static DiffLine Modified(int n) => new(n, "left", n, "right", ChangeKind.Modified);

    private static DiffLine Inserted(int n) => new(null, null, n, "added", ChangeKind.Inserted);

    private static DiffLine Deleted(int n) => new(n, "gone", null, null, ChangeKind.Deleted);

    [Fact]
    public void With_no_decisions_the_base_side_round_trips_exactly()
    {
        var result = DiffResult.Create([Unchanged(1), Modified(2), Inserted(3), Deleted(3)]);

        var merged = MergedDocument.Build(result, MergeState.Empty, DiffSide.Right);

        // Exactly the right-hand file: unchanged context, the right version of the modification, the
        // inserted line, and nothing for the row the right side does not have.
        Assert.Equal(["same", "right", "added"], merged);
    }

    [Fact]
    public void With_no_decisions_the_left_base_round_trips_exactly()
    {
        var result = DiffResult.Create([Unchanged(1), Modified(2), Inserted(3), Deleted(3)]);

        var merged = MergedDocument.Build(result, MergeState.Empty, DiffSide.Left);

        Assert.Equal(["same", "left", "gone"], merged);
    }

    [Fact]
    public void Taking_left_on_a_modification_swaps_that_line_only()
    {
        var result = DiffResult.Create([Unchanged(1), Modified(2), Unchanged(3)]);
        var state = MergeState.Empty.With(0, HunkResolution.TakeLeft);

        Assert.Equal(["same", "left", "same"], MergedDocument.Build(result, state, DiffSide.Right));
    }

    [Fact]
    public void Taking_left_on_an_insertion_removes_the_added_line()
    {
        // The left side has no line here at all, so "take left" must DROP the row rather than write a
        // blank one - the difference between undoing an addition and leaving an empty line behind.
        var result = DiffResult.Create([Unchanged(1), Inserted(2)]);
        var state = MergeState.Empty.With(0, HunkResolution.TakeLeft);

        Assert.Equal(["same"], MergedDocument.Build(result, state, DiffSide.Right));
    }

    [Fact]
    public void Taking_left_on_a_deletion_restores_the_removed_line()
    {
        var result = DiffResult.Create([Unchanged(1), Deleted(2)]);
        var state = MergeState.Empty.With(0, HunkResolution.TakeLeft);

        Assert.Equal(["same", "gone"], MergedDocument.Build(result, state, DiffSide.Right));
    }

    [Fact]
    public void Taking_right_on_a_deletion_keeps_it_deleted()
    {
        var result = DiffResult.Create([Unchanged(1), Deleted(2)]);
        var state = MergeState.Empty.With(0, HunkResolution.TakeRight);

        Assert.Equal(["same"], MergedDocument.Build(result, state, DiffSide.Right));
    }

    [Fact]
    public void Adjacent_rows_in_one_hunk_all_follow_that_hunks_decision()
    {
        // Deleted+Inserted are adjacent so they group into a single hunk; resolving it must move both
        // rows together, not just the one the cursor happened to be on.
        var result = DiffResult.Create([Unchanged(1), Deleted(2), Inserted(2), Unchanged(3)]);
        Assert.Single(result.Hunks);

        var state = MergeState.Empty.With(0, HunkResolution.TakeLeft);

        Assert.Equal(["same", "gone", "same"], MergedDocument.Build(result, state, DiffSide.Right));
    }

    [Fact]
    public void Separate_hunks_are_resolved_independently()
    {
        var result = DiffResult.Create([Modified(1), Unchanged(2), Modified(3)]);
        Assert.Equal(2, result.Hunks.Count);

        var state = MergeState.Empty
            .With(0, HunkResolution.TakeLeft)
            .With(1, HunkResolution.TakeRight);

        Assert.Equal(["left", "same", "right"], MergedDocument.Build(result, state, DiffSide.Right));
    }

    [Fact]
    public void An_unresolved_hunk_between_resolved_ones_keeps_the_base()
    {
        var result = DiffResult.Create([Modified(1), Unchanged(2), Modified(3), Unchanged(4), Modified(5)]);
        var state = MergeState.Empty
            .With(0, HunkResolution.TakeLeft)
            .With(2, HunkResolution.TakeLeft);

        Assert.Equal(["left", "same", "right", "same", "left"], MergedDocument.Build(result, state, DiffSide.Right));
    }

    [Fact]
    public void Identical_files_merge_to_themselves()
    {
        var result = DiffResult.Create([Unchanged(1), Unchanged(2)]);

        Assert.Equal(["same", "same"], MergedDocument.Build(result, MergeState.Empty, DiffSide.Right));
    }

    private static TextFormat Format(LineEnding lineEnding, bool endsWithNewline = false) =>
        TextFormat.Default with { LineEnding = lineEnding, EndsWithNewline = endsWithNewline };

    [Theory]
    [InlineData(LineEnding.Lf, "a\nb")]
    [InlineData(LineEnding.Crlf, "a\r\nb")]
    [InlineData(LineEnding.Cr, "a\rb")]
    public void ToText_uses_the_documents_own_terminator(LineEnding lineEnding, string expected) =>
        Assert.Equal(expected, MergedDocument.ToText(["a", "b"], Format(lineEnding)));

    [Theory]
    [InlineData(LineEnding.Lf, "a\nb\n")]
    [InlineData(LineEnding.Crlf, "a\r\nb\r\n")]
    public void ToText_restores_the_trailing_newline_when_the_source_had_one(LineEnding lineEnding, string expected)
    {
        // The reader drops the empty string after a final newline, so without this a save would strip
        // it - which git reports as a change to the last line of the file.
        Assert.Equal(expected, MergedDocument.ToText(["a", "b"], Format(lineEnding, endsWithNewline: true)));
    }

    [Fact]
    public void ToText_of_nothing_is_empty()
    {
        // Even when the format says a trailing newline: appending one would turn a zero-byte file
        // into a one-byte file.
        Assert.Equal(string.Empty, MergedDocument.ToText([], Format(LineEnding.Lf, endsWithNewline: true)));
    }
}
