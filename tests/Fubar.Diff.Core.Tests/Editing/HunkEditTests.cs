using Fubar.Diff.Core.Editing;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests.Editing;

/// <summary>
/// Resolving a difference by rewriting the file rather than by recording a decision.
///
/// The important thing these pin is not that the code runs but that it AGREES with the model it
/// replaces: for a two-way comparison, resolving every hunk one at a time has to give the same file
/// that <c>MergedDocument</c> would have produced from the same choices. Several tests below assert
/// exactly that, because "the merge quietly changed" is the kind of regression nobody notices until
/// they have saved over something.
/// </summary>
public class HunkEditTests
{
    private static DiffResult Result(params DiffLine[] rows) => DiffResult.Create(rows);

    private static DiffLine Same(int l, int r, string text) => new(l, text, r, text, ChangeKind.Unchanged);

    private static DiffLine Changed(int l, string left, int r, string right) =>
        new(l, left, r, right, ChangeKind.Modified);

    private static DiffLine OnlyLeft(int l, string text) => new(l, text, null, null, ChangeKind.Deleted);

    private static DiffLine OnlyRight(int r, string text) => new(null, null, r, text, ChangeKind.Inserted);

    private static IReadOnlyList<string> Resolve(
        DiffResult result, int hunk, DiffSide take, DiffSide target, IReadOnlyList<string> targetLines) =>
        HunkEdit.Resolve(result, result.Hunks[hunk], take, target, targetLines);

    [Fact]
    public void Taking_the_left_version_of_a_changed_line_rewrites_it()
    {
        var result = Result(
            Same(1, 1, "a"),
            Changed(2, "left", 2, "right"),
            Same(3, 3, "c"));

        Assert.Equal(
            ["a", "left", "c"],
            Resolve(result, 0, DiffSide.Left, DiffSide.Right, ["a", "right", "c"]));
    }

    [Fact]
    public void Taking_the_side_you_are_already_on_changes_nothing()
    {
        var result = Result(Changed(1, "left", 1, "right"));

        Assert.Equal(["right"], Resolve(result, 0, DiffSide.Right, DiffSide.Right, ["right"]));
    }

    [Fact]
    public void Taking_the_left_version_of_a_DELETION_puts_the_line_back()
    {
        // The right file does not have this line at all, so there is nothing to replace - the text is
        // inserted, and where it goes is the whole question.
        var result = Result(
            Same(1, 1, "a"),
            OnlyLeft(2, "restored"),
            Same(3, 2, "c"));

        Assert.Equal(
            ["a", "restored", "c"],
            Resolve(result, 0, DiffSide.Left, DiffSide.Right, ["a", "c"]));
    }

    [Fact]
    public void Taking_the_left_version_of_an_INSERTION_removes_the_line()
    {
        // The left has nothing here, so taking it means the right's line goes. Blanking it instead
        // would leave an empty line behind, which is the classic way to get this wrong.
        var result = Result(
            Same(1, 1, "a"),
            OnlyRight(2, "added"),
            Same(2, 3, "c"));

        Assert.Equal(
            ["a", "c"],
            Resolve(result, 0, DiffSide.Left, DiffSide.Right, ["a", "added", "c"]));
    }

    [Fact]
    public void A_restored_line_at_the_very_start_goes_to_the_top()
    {
        var result = Result(
            OnlyLeft(1, "header"),
            Same(2, 1, "a"));

        Assert.Equal(["header", "a"], Resolve(result, 0, DiffSide.Left, DiffSide.Right, ["a"]));
    }

    [Fact]
    public void A_restored_line_at_the_very_end_goes_to_the_bottom()
    {
        var result = Result(
            Same(1, 1, "a"),
            OnlyLeft(2, "footer"));

        Assert.Equal(["a", "footer"], Resolve(result, 0, DiffSide.Left, DiffSide.Right, ["a"]));
    }

    [Fact]
    public void A_block_of_several_lines_is_replaced_as_one()
    {
        var result = Result(
            Same(1, 1, "a"),
            Changed(2, "L1", 2, "R1"),
            Changed(3, "L2", 3, "R2"),
            Same(4, 4, "z"));

        Assert.Equal(
            ["a", "L1", "L2", "z"],
            Resolve(result, 0, DiffSide.Left, DiffSide.Right, ["a", "R1", "R2", "z"]));
    }

    [Fact]
    public void A_block_that_is_longer_on_one_side_still_replaces_cleanly()
    {
        var result = Result(
            Same(1, 1, "a"),
            Changed(2, "L1", 2, "R1"),
            OnlyLeft(3, "L2"),
            OnlyLeft(4, "L3"),
            Same(5, 3, "z"));

        Assert.Equal(
            ["a", "L1", "L2", "L3", "z"],
            Resolve(result, 0, DiffSide.Left, DiffSide.Right, ["a", "R1", "z"]));
    }

    [Fact]
    public void Writing_the_LEFT_file_works_the_same_way_round()
    {
        // Both panes are editable, so both directions have to be right - and this one exercises the
        // mirror of every lookup in the implementation.
        var result = Result(
            Same(1, 1, "a"),
            Changed(2, "left", 2, "right"),
            Same(3, 3, "c"));

        Assert.Equal(
            ["a", "right", "c"],
            Resolve(result, 0, DiffSide.Right, DiffSide.Left, ["a", "left", "c"]));
    }

    [Fact]
    public void Taking_the_right_version_of_an_insertion_adds_it_to_the_LEFT()
    {
        var result = Result(
            Same(1, 1, "a"),
            OnlyRight(2, "added"),
            Same(2, 3, "c"));

        Assert.Equal(
            ["a", "added", "c"],
            Resolve(result, 0, DiffSide.Right, DiffSide.Left, ["a", "c"]));
    }

    // ---- Agreement with the model this replaces ---------------------------------------------------

    /// <summary>
    /// Resolves every hunk in turn, re-deriving nothing - which is what the app will do one hunk at a
    /// time as the user clicks, except that the app re-diffs in between and this does not. Applying
    /// them back to front means each edit's line numbers are still valid when it runs.
    /// </summary>
    private static IReadOnlyList<string> ResolveAll(DiffResult result, DiffSide take, DiffSide target)
    {
        var lines = target == DiffSide.Right
            ? result.Lines.Where(r => r.RightText is not null).Select(r => r.RightText!).ToList()
            : result.Lines.Where(r => r.LeftText is not null).Select(r => r.LeftText!).ToList();

        IReadOnlyList<string> current = lines;

        for (var i = result.Hunks.Count - 1; i >= 0; i--)
        {
            current = HunkEdit.Resolve(result, result.Hunks[i], take, target, current);
        }

        return current;
    }

    [Fact]
    public void Taking_every_hunk_from_one_side_gives_that_side_exactly()
    {
        // The strongest statement available: resolve everything in favour of the left and the right
        // file IS the left file. Any off-by-one in the insertion point or the replaced range shows up
        // here immediately.
        var result = Result(
            Same(1, 1, "a"),
            Changed(2, "L1", 2, "R1"),
            Same(3, 3, "b"),
            OnlyLeft(4, "onlyL"),
            Same(5, 4, "c"),
            OnlyRight(5, "onlyR"),
            Same(6, 6, "d"));

        Assert.Equal(
            ["a", "L1", "b", "onlyL", "c", "d"],
            ResolveAll(result, DiffSide.Left, DiffSide.Right));
    }

    [Fact]
    public void And_the_same_the_other_way_round()
    {
        var result = Result(
            Same(1, 1, "a"),
            Changed(2, "L1", 2, "R1"),
            OnlyLeft(3, "onlyL"),
            Same(4, 3, "c"),
            OnlyRight(4, "onlyR"),
            Same(5, 5, "d"));

        Assert.Equal(
            ["a", "R1", "c", "onlyR", "d"],
            ResolveAll(result, DiffSide.Right, DiffSide.Left));
    }

    [Fact]
    public void It_agrees_with_the_merge_model_it_replaces()
    {
        // Same comparison, same choices, two completely different mechanisms - MergedDocument builds
        // the file from rows plus a decision list, HunkEdit rewrites the file one hunk at a time. They
        // must produce the same bytes, or "take left" quietly means something different than it did.
        var result = Result(
            Same(1, 1, "a"),
            Changed(2, "L1", 2, "R1"),
            Same(3, 3, "b"),
            OnlyLeft(4, "onlyL"),
            Same(5, 4, "c"),
            OnlyRight(5, "onlyR"),
            Same(6, 6, "d"));

        var state = MergeState.Empty;
        for (var i = 0; i < result.Hunks.Count; i++)
        {
            state = state.With(i, HunkResolution.TakeLeft);
        }

        Assert.Equal(
            MergedDocument.Build(result, state, DiffSide.Right),
            ResolveAll(result, DiffSide.Left, DiffSide.Right));
    }

    [Fact]
    public void It_agrees_with_the_merge_model_for_a_SINGLE_decision_too()
    {
        // The common case: one hunk resolved, the rest left alone.
        var result = Result(
            Same(1, 1, "a"),
            Changed(2, "L1", 2, "R1"),
            Same(3, 3, "b"),
            OnlyLeft(4, "onlyL"),
            Same(5, 4, "c"));

        var lines = result.Lines.Where(r => r.RightText is not null).Select(r => r.RightText!).ToList();

        Assert.Equal(
            MergedDocument.Build(result, MergeState.Empty.With(1, HunkResolution.TakeLeft), DiffSide.Right),
            HunkEdit.Resolve(result, result.Hunks[1], DiffSide.Left, DiffSide.Right, lines));
    }

    [Fact]
    public void An_identical_comparison_has_nothing_to_resolve()
    {
        var result = Result(Same(1, 1, "a"), Same(2, 2, "b"));

        Assert.Empty(result.Hunks);
        Assert.Equal(["a", "b"], ResolveAll(result, DiffSide.Left, DiffSide.Right));
    }
}
