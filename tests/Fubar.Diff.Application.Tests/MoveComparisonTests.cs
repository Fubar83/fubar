using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// Move detection end to end, through the real engine, slider and normalizer.
///
/// The unit tests hand <c>MoveDetector</c> an alignment; these check the thing a user would actually
/// see, on the case the feature exists for - a method moved within a file, which DiffPlex reports as a
/// deletion here and an insertion there and which reads as two unrelated changes until something says
/// otherwise.
/// </summary>
public class MoveComparisonTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FileComparisonService Build() => new(
        new Infrastructure.Files.TextFileReader(),
        new DiffPlexDiffEngine(),
        new DiffPlexInlineDiffEngine(),
        new TextLineNormalizer(),
        new JsonSemanticPass(new JsonAstParser()));

    private static Task<FileComparison> Compare(string left, string right, string extension = ".cs") =>
        Build().CompareTextAsync(
            left,
            right,
            new ComparisonOptions(),
            "left" + extension,
            "right" + extension,
            Token);

    private const string Before = """
        public class Service
        {
            public void Helper()
            {
                Console.WriteLine("help");
            }

            public void Run()
            {
                Helper();
            }
        }
        """;

    /// <summary>The same class with Helper moved below Run - nothing else touched.</summary>
    private const string AfterMove = """
        public class Service
        {
            public void Run()
            {
                Helper();
            }

            public void Helper()
            {
                Console.WriteLine("help");
            }
        }
        """;

    [Fact]
    public async Task A_method_moved_within_a_file_is_reported_as_a_move()
    {
        var comparison = await Compare(Before, AfterMove);

        Assert.True(comparison.Result.Moved > 0);
    }

    [Fact]
    public async Task Every_block_that_left_is_a_block_that_arrived()
    {
        // Every id is claimed on both sides: a move with only one half found would be a mark pointing
        // at nothing, which is worse than no mark.
        var comparison = await Compare(Before, AfterMove);

        var gone = comparison.Result.Lines.Select(l => l.LeftMoveId).Where(id => id is not null).Distinct().Order();
        var arrived = comparison.Result.Lines.Select(l => l.RightMoveId).Where(id => id is not null).Distinct().Order();

        Assert.NotEmpty(gone);
        Assert.Equal(gone, arrived);
    }

    [Fact]
    public async Task A_swap_marks_each_side_of_a_row_as_a_different_block()
    {
        // Two methods of the same shape trading places give the aligner nothing one-sided to work
        // with - it pairs the first line of one against the first line of the other. The row's LEFT
        // text moved down and its RIGHT text moved up, which is two different blocks on one row.
        var comparison = await Compare(Before, AfterMove);

        Assert.Contains(
            comparison.Result.Lines,
            l => l.LeftMoveId is not null && l.RightMoveId is not null && l.LeftMoveId != l.RightMoveId);
    }

    [Fact]
    public async Task Every_moved_row_still_carries_the_text_it_had()
    {
        // The mark is added before projection, and projection is what puts the real document lines
        // back over the rows. A pass that ran on keys and forgot to survive that would leave moved
        // rows blank - visible immediately, but only if something looks.
        var comparison = await Compare(Before, AfterMove);

        foreach (var row in comparison.Result.Lines.Where(l => l.IsMoved))
        {
            Assert.NotNull(row.Kind == ChangeKind.Deleted ? row.LeftText : row.RightText);
        }
    }

    [Fact]
    public async Task A_real_edit_alongside_a_move_is_still_reported_as_an_edit()
    {
        // The failure that would make this feature dangerous: marking something as "only moved" when
        // it also changed would tell the reader to skip the one thing they needed to see.
        var edited = AfterMove.Replace("""Console.WriteLine("help");""", """Console.WriteLine("HELP");""", StringComparison.Ordinal);

        var comparison = await Compare(Before, edited);

        var movedText = comparison.Result.Lines
            .Where(l => l.IsMoved)
            .Select(l => (l.LeftText ?? l.RightText) ?? string.Empty);

        Assert.DoesNotContain(movedText, t => t.Contains("HELP", StringComparison.Ordinal));
    }

    /// <summary>
    /// The other shape a move takes: a block that travels far enough that the aligner has nothing to
    /// pair it against and reports it one-sided. Both shapes have to work, and they exercise different
    /// halves of the run matching.
    /// </summary>
    private const string LongBefore = """
        public class Service
        {
            public void Helper()
            {
                Console.WriteLine("help");
            }

            public void A() => a();
            public void B() => b();
            public void C() => c();
            public void D() => d();
        }
        """;

    private const string LongAfter = """
        public class Service
        {
            public void A() => a();
            public void B() => b();
            public void C() => c();
            public void D() => d();

            public void Helper()
            {
                Console.WriteLine("help");
            }
        }
        """;

    [Fact]
    public async Task A_block_that_travels_past_other_code_is_reported_as_a_move()
    {
        var comparison = await Compare(LongBefore, LongAfter);

        Assert.True(comparison.Result.Moved > 0);
    }

    [Fact]
    public async Task The_two_halves_of_a_move_are_the_same_text()
    {
        // The claim the mark makes, asserted directly rather than through whichever block the aligner
        // decided was the one that travelled. Reordering two things is symmetric - "Helper moved down"
        // and "A to D moved up" describe the same edit, and DiffPlex is free to pick either - so a
        // test naming one of them is testing the aligner's tie-break, not this pass.
        var comparison = await Compare(LongBefore, LongAfter);

        var ids = comparison.Result.Lines
            .Where(l => l.LeftMoveId is not null)
            .Select(l => l.LeftMoveId)
            .Distinct();

        Assert.NotEmpty(ids);

        foreach (var id in ids)
        {
            var gone = comparison.Result.Lines.Where(l => l.LeftMoveId == id).Select(l => l.LeftText);
            var arrived = comparison.Result.Lines.Where(l => l.RightMoveId == id).Select(l => l.RightText);

            Assert.Equal(gone, arrived);
        }
    }

    [Fact]
    public async Task A_moved_row_carries_no_word_level_highlights()
    {
        // The aligner may have paired two lines that turn out to be halves of two different blocks.
        // Highlighting the letters that differ between them would invite the reader to read a
        // word-level change nobody made.
        var comparison = await Compare(Before, AfterMove);

        foreach (var row in comparison.Result.Lines.Where(l => l.IsMoved))
        {
            Assert.Empty(row.LeftSpans);
            Assert.Empty(row.RightSpans);
        }
    }

    [Fact]
    public async Task An_ordinary_edit_reports_no_moves_at_all()
    {
        var comparison = await Compare(Before, Before.Replace("help", "assist", StringComparison.Ordinal));

        Assert.Equal(0, comparison.Result.Moved);
        Assert.All(comparison.Result.Lines, l => Assert.False(l.IsMoved));
    }

    [Fact]
    public async Task Identical_files_report_no_moves()
    {
        var comparison = await Compare(Before, Before);

        Assert.True(comparison.Result.AreIdentical);
        Assert.Equal(0, comparison.Result.Moved);
    }

    [Fact]
    public async Task A_moved_block_is_still_counted_and_navigable_as_a_change()
    {
        // Deliberately NOT deducted from the counts or the hunks. It is a difference on disk, the
        // patch will contain it, and F7/F8 must still stop on it - all the mark does is say why.
        //
        // The kinds are whatever the aligner made them: this fixture is a swap, so they are Modified
        // rather than Deleted/Inserted, which is exactly why the marks have to be per side.
        var comparison = await Compare(Before, AfterMove);

        Assert.NotEmpty(comparison.Result.Hunks);
        Assert.False(comparison.Result.AreIdentical);
        Assert.True(comparison.Result.Inserted + comparison.Result.Deleted + comparison.Result.Modified > 0);
        Assert.All(comparison.Result.Lines.Where(l => l.IsMoved), l => Assert.True(l.IsChange));
    }
}
