using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// How a move reaches the renderers.
///
/// The mark is per side on the row, and the renderers see one side at a time, so the translation
/// between the two is where it can quietly go wrong - a row marked on the left showing up blue in the
/// right-hand pane would put one block in two places at once.
/// </summary>
public class MovedRenderingTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    [Fact]
    public void A_deleted_move_is_marked_on_the_left_and_not_on_the_filler_opposite_it()
    {
        var row = new DiffLine(2, "moved();", null, null, ChangeKind.Deleted) { LeftMoveId = 0 };
        var result = DiffResult.Create([Unchanged(1), row]);

        Assert.True(AlignedText.Build(result, DiffSide.Left).Lines[1].IsMoved);
        Assert.False(AlignedText.Build(result, DiffSide.Right).Lines[1].IsMoved);
    }

    [Fact]
    public void An_inserted_move_is_marked_on_the_right_and_not_on_the_filler_opposite_it()
    {
        var row = new DiffLine(null, null, 2, "moved();", ChangeKind.Inserted) { RightMoveId = 0 };
        var result = DiffResult.Create([Unchanged(1), row]);

        Assert.True(AlignedText.Build(result, DiffSide.Right).Lines[1].IsMoved);
        Assert.False(AlignedText.Build(result, DiffSide.Left).Lines[1].IsMoved);
    }

    [Fact]
    public void A_swapped_row_is_marked_on_both_sides()
    {
        // Its left text moved down and its right text moved up. Both are true at once, which is the
        // reason the mark is per side rather than per row.
        var row = new DiffLine(2, "void Helper() {", 2, "void Run() {", ChangeKind.Modified)
        {
            LeftMoveId = 0,
            RightMoveId = 1,
        };

        var result = DiffResult.Create([Unchanged(1), row]);

        Assert.True(AlignedText.Build(result, DiffSide.Left).Lines[1].IsMoved);
        Assert.True(AlignedText.Build(result, DiffSide.Right).Lines[1].IsMoved);
    }

    [Fact]
    public void A_row_moved_only_on_one_side_leaves_the_other_side_an_ordinary_change()
    {
        var row = new DiffLine(2, "moved();", 2, "rewritten();", ChangeKind.Modified) { LeftMoveId = 0 };
        var result = DiffResult.Create([Unchanged(1), row]);

        Assert.True(AlignedText.Build(result, DiffSide.Left).Lines[1].IsMoved);
        Assert.False(AlignedText.Build(result, DiffSide.Right).Lines[1].IsMoved);
    }

    [Fact]
    public void The_unified_view_marks_each_emitted_side_with_its_own_answer()
    {
        // The unified document emits a modified row twice - once as a removal, once as an addition -
        // so it is the one place both halves of a swapped row are separate LINES, and each has to
        // carry the mark belonging to the side it came from.
        var row = new DiffLine(2, "void Helper() {", 2, "void Run() {", ChangeKind.Modified)
        {
            LeftMoveId = 0,
            RightMoveId = 1,
        };

        var unified = UnifiedText.Build(DiffResult.Create([Unchanged(1), row, Unchanged(3)]));

        var changed = unified.Document.Lines.Where(l => l.Kind is ChangeKind.Deleted or ChangeKind.Inserted).ToList();

        Assert.Equal(2, changed.Count);
        Assert.All(changed, l => Assert.True(l.IsMoved));
    }

    [Fact]
    public void Context_in_the_unified_view_is_never_marked()
    {
        var unified = UnifiedText.Build(DiffResult.Create(
        [
            Unchanged(1),
            new DiffLine(2, "moved();", null, null, ChangeKind.Deleted) { LeftMoveId = 0 },
            Unchanged(3),
        ]));

        Assert.All(
            unified.Document.Lines.Where(l => l.Kind == ChangeKind.Unchanged),
            l => Assert.False(l.IsMoved));
    }

    [Fact]
    public void The_compact_excerpt_carries_the_mark_for_the_side_it_shows()
    {
        // The close-up pane stacks one side above the other, so it too shows a single side at a time.
        var result = DiffResult.Create(
        [
            Unchanged(1),
            new DiffLine(2, "moved();", null, null, ChangeKind.Deleted) { LeftMoveId = 0 },
        ]);

        var left = AlignedText.BuildCompact(result, DiffSide.Left, 1, 1);

        Assert.True(left.Lines[0].IsMoved);
        Assert.Empty(AlignedText.BuildCompact(result, DiffSide.Right, 1, 1).Lines);
    }
}
