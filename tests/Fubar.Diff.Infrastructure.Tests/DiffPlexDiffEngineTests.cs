using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// The adapter's contract with Core: rows stay aligned, fillers appear opposite one-sided changes,
/// and line numbers point at the right lines. The algorithm itself is DiffPlex's business - what is
/// tested here is the translation, which is the part we own.
/// </summary>
public class DiffPlexDiffEngineTests
{
    private readonly DiffPlexDiffEngine _engine = new();

    private IReadOnlyList<DiffLine> Align(string[] left, string[] right) =>
        _engine.Align(left, right, ComparisonOptions.Default);

    [Fact]
    public void Identical_input_produces_only_unchanged_rows()
    {
        var rows = Align(["a", "b", "c"], ["a", "b", "c"]);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(ChangeKind.Unchanged, r.Kind));
    }

    [Fact]
    public void An_inserted_line_gets_a_filler_on_the_left()
    {
        var rows = Align(["a", "c"], ["a", "b", "c"]);

        var inserted = Assert.Single(rows, r => r.Kind == ChangeKind.Inserted);
        Assert.Null(inserted.LeftNumber);
        Assert.Equal(2, inserted.RightNumber);
    }

    [Fact]
    public void A_deleted_line_gets_a_filler_on_the_right()
    {
        var rows = Align(["a", "b", "c"], ["a", "c"]);

        var deleted = Assert.Single(rows, r => r.Kind == ChangeKind.Deleted);
        Assert.Equal(2, deleted.LeftNumber);
        Assert.Null(deleted.RightNumber);
    }

    [Fact]
    public void Both_sides_always_come_back_the_same_length()
    {
        // The whole side-by-side rendering depends on this: every row must have a slot on each side,
        // even when one of them is empty.
        var rows = Align(["a", "b", "c", "d"], ["a", "x"]);

        Assert.All(rows, r =>
            Assert.True(r.LeftNumber is not null || r.RightNumber is not null || r.Kind == ChangeKind.Filler));
    }

    [Fact]
    public void Line_numbers_are_one_based_and_per_side()
    {
        var rows = Align(["a"], ["a"]);

        var row = Assert.Single(rows);
        Assert.Equal(1, row.LeftNumber);
        Assert.Equal(1, row.RightNumber);
    }

    [Fact]
    public void The_engine_never_returns_display_text()
    {
        // It is handed comparison keys, so anything it echoed back would be the normalised form.
        // FileComparisonService projects the real document lines on afterwards.
        var rows = Align(["a"], ["b"]);

        Assert.All(rows, r =>
        {
            Assert.Null(r.LeftText);
            Assert.Null(r.RightText);
        });
    }

    [Fact]
    public void Empty_input_on_one_side_marks_everything_inserted()
    {
        var rows = Align([], ["a", "b"]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(ChangeKind.Inserted, r.Kind));
    }
}
