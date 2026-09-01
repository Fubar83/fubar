using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// The large-document alignment path: break the pair into small problems, and get the same answer.
///
/// Fast is the point, but correct is the requirement, and the two failure modes here are both silent.
/// A stitching bug puts the right rows under the wrong line numbers, which reads as a perfectly
/// plausible diff of something else. A bad anchor pairs two lines that merely look alike, and
/// everything between them is reported as changed.
/// </summary>
public class SegmentedLineAlignerTests
{
    private static readonly ComparisonOptions Options = new();

    /// <summary>
    /// The whole pipeline as the app runs it, so these check the ENGINE rather than the helper -
    /// including the threshold, by padding both sides with identical lines until it trips.
    /// </summary>
    private static IReadOnlyList<DiffLine> Align(IEnumerable<string> left, IEnumerable<string> right, bool large)
    {
        var padding = large
            ? Enumerable.Range(0, DiffPlexDiffEngine.SegmentedFrom).Select(i => $"padding line {i}").ToArray()
            : [];

        var rows = new DiffPlexDiffEngine().Align(
            [.. padding, .. left],
            [.. padding, .. right],
            Options);

        // Drop the padding's rows and rebase the numbers off it: the padding is identical on both
        // sides so it aligns one-to-one, and subtracting it leaves every assertion below reading in
        // the coordinates of the lines the test actually wrote - the same ones either side of the
        // threshold.
        return
        [
            .. rows.Skip(padding.Length).Select(row => row with
            {
                LeftNumber = row.LeftNumber is { } l ? l - padding.Length : null,
                RightNumber = row.RightNumber is { } r ? r - padding.Length : null,
            }),
        ];
    }

    private static string Shape(IEnumerable<DiffLine> rows) =>
        string.Concat(rows.Select(r => r.Kind switch
        {
            ChangeKind.Unchanged => "=",
            ChangeKind.Modified => "M",
            ChangeKind.Inserted => "+",
            ChangeKind.Deleted => "-",
            _ => ".",
        }));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_identical_pair_is_all_unchanged(bool large)
    {
        var rows = Align(["a", "b", "c"], ["a", "b", "c"], large);

        Assert.Equal("===", Shape(rows));
        Assert.Equal([1, 2, 3], rows.Select(r => r.LeftNumber));
        Assert.Equal([1, 2, 3], rows.Select(r => r.RightNumber));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Line_numbers_survive_the_stitching(bool large)
    {
        // The bug this exists for: a segment's rows are numbered from 1 within the segment, so every
        // one of them has to be renumbered into the whole document's coordinates on the way out.
        var rows = Align(
            ["same", "old", "same again", "tail"],
            ["same", "new", "same again", "tail"],
            large);

        Assert.Equal("=M==", Shape(rows));

        var modified = rows.Single(r => r.Kind == ChangeKind.Modified);
        Assert.Equal(2, modified.LeftNumber);
        Assert.Equal(2, modified.RightNumber);

        Assert.Equal(4, rows[^1].LeftNumber);
        Assert.Equal(4, rows[^1].RightNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_insertion_gets_a_filler_opposite_it(bool large)
    {
        // Row-count parity between the two sides is what the whole side-by-side view rests on, and a
        // segmented aligner emits some of these rows itself rather than getting them from the engine.
        var rows = Align(["a", "c"], ["a", "b", "c"], large);

        Assert.Equal("=+=", Shape(rows));
        Assert.Equal([1, null, 2], rows.Select(r => r.LeftNumber));
        Assert.Equal([1, 2, 3], rows.Select(r => r.RightNumber));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_deletion_gets_a_filler_opposite_it(bool large)
    {
        var rows = Align(["a", "b", "c"], ["a", "c"], large);

        Assert.Equal("=-=", Shape(rows));
        Assert.Equal([1, 2, 3], rows.Select(r => r.LeftNumber));
        Assert.Equal([1, null, 2], rows.Select(r => r.RightNumber));
    }

    [Fact]
    public void An_empty_side_becomes_all_insertions()
    {
        // A segment with content on one side only is emitted directly rather than handed to the
        // engine - there is one possible answer and no reason to look for it.
        var rows = Align([], ["a", "b"], large: true);

        Assert.Equal("++", Shape(rows));
        Assert.Equal([1, 2], rows.Select(r => r.RightNumber));
        Assert.All(rows, row => Assert.Null(row.LeftNumber));
    }

    [Fact]
    public void A_document_of_nothing_but_repeated_lines_still_aligns()
    {
        // No line is unique, so there are no anchors at all and the middle falls back to one call over
        // everything - the old behaviour. Slow on a huge file, never wrong.
        var repeated = Enumerable.Repeat("same", 200).ToArray();

        var rows = Align(repeated, [.. repeated, "extra"], large: true);

        Assert.Equal(201, rows.Count);
        Assert.Single(rows, r => r.Kind == ChangeKind.Inserted);
    }

    [Fact]
    public void The_same_edit_aligns_the_same_way_either_side_of_the_threshold()
    {
        // The claim the threshold rests on: segmenting is a speed strategy, not a different answer.
        // Padding shifts the numbers, so the SHAPE is what is compared.
        string[] left = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta"];
        string[] right = ["alpha", "beta", "GAMMA", "delta", "inserted", "epsilon", "zeta"];

        Assert.Equal(
            Shape(Align(left, right, large: false)),
            Shape(Align(left, right, large: true)));
    }

    [Fact]
    public void A_change_at_the_very_start_is_not_swallowed_by_the_prefix_trim()
    {
        var rows = Align(["old", "b", "c"], ["new", "b", "c"], large: true);

        Assert.Equal("M==", Shape(rows));
    }

    [Fact]
    public void A_change_at_the_very_end_is_not_swallowed_by_the_suffix_trim()
    {
        var rows = Align(["a", "b", "old"], ["a", "b", "new"], large: true);

        Assert.Equal("==M", Shape(rows));
    }

    [Fact]
    public void A_file_compared_with_itself_never_double_counts_its_own_lines()
    {
        // The prefix and suffix scans would otherwise both claim every line, leaving a negative-length
        // middle - which is an exception rather than a wrong answer, but only just.
        var lines = Enumerable.Range(0, 50).Select(i => $"line {i}").ToArray();

        var rows = Align(lines, lines, large: true);

        Assert.Equal(50, rows.Count);
        Assert.All(rows, row => Assert.Equal(ChangeKind.Unchanged, row.Kind));
    }

    [Fact]
    public void Every_left_and_right_line_appears_exactly_once_and_in_order()
    {
        // The invariant that catches a stitching mistake generically: whatever the alignment decides,
        // each side's lines must come back complete, once each, ascending.
        var left = Enumerable.Range(0, 500).Select(i => $"L{i % 7} {i}").ToArray();
        var right = Enumerable.Range(0, 500).Select(i => i % 5 == 0 ? $"changed {i}" : $"L{i % 7} {i}").ToArray();

        var rows = Align(left, right, large: true);

        Assert.Equal(
            Enumerable.Range(1, 500),
            rows.Select(r => r.LeftNumber).Where(n => n is not null).Select(n => n!.Value));

        Assert.Equal(
            Enumerable.Range(1, 500),
            rows.Select(r => r.RightNumber).Where(n => n is not null).Select(n => n!.Value));
    }
}
