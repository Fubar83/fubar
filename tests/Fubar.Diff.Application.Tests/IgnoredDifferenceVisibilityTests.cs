using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// A difference an option was told to ignore is still SHOWN - faintly.
///
/// Turning on "ignore whitespace" used to make the affected lines vanish into ordinary unchanged rows,
/// which is the wrong kind of silence: the reader cannot then tell "these lines agree" from "these lines
/// disagree and I asked not to be told". The second is worth a glance before trusting the diff, and it
/// is also the only way to check that a rule you just added is doing what you thought.
///
/// The mark is <see cref="DiffLine.IsIgnored"/>, which the renderers already draw as a faint neutral
/// band and which keeps the row out of the counts, the hunks and next/previous.
/// </summary>
public class IgnoredDifferenceVisibilityTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private sealed class StubReader(Dictionary<string, string[]> files) : ITextFileReader
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            files.TryGetValue(path, out var lines)
                ? Task.FromResult(new TextDocument(path, lines, TextFormat.Default))
                : throw new TextFileReadException(path, "the file does not exist.");
    }

    private static async Task<FileComparison> CompareAsync(string left, string right, ComparisonOptions options)
    {
        var files = new Dictionary<string, string[]>
        {
            ["left.cs"] = left.Split('\n'),
            ["right.cs"] = right.Split('\n'),
        };

        var service = new FileComparisonService(
            new StubReader(files),
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));

        return await service.CompareFilesAsync("left.cs", "right.cs", options, Token);
    }

    private static IReadOnlyList<DiffLine> IgnoredRows(FileComparison comparison) =>
        [.. comparison.Result.Lines.Where(l => l.IsIgnored)];

    // ---- Whitespace ----------------------------------------------------------------------------

    [Fact]
    public async Task A_line_equalised_by_ignore_whitespace_is_marked_rather_than_erased()
    {
        var comparison = await CompareAsync(
            "int x = 1;\nint y = 2;\n",
            "    int x = 1;\nint y = 2;\n",
            ComparisonOptions.Default with { IgnoreWhitespace = true });

        var ignored = Assert.Single(IgnoredRows(comparison));
        Assert.Equal("int x = 1;", ignored.LeftText);
        Assert.Equal("    int x = 1;", ignored.RightText);
    }

    [Fact]
    public async Task Only_the_whitespace_is_marked_not_the_whole_row()
    {
        // The row is identical apart from four spaces at one end of it. Banding the whole row to report
        // that reads as "this line is involved", when almost none of it is - and on a file where a
        // formatter touched the indentation of every line, that is the entire pane lit up to say
        // nothing. The spans let the renderers mark the characters and leave the rest alone.
        var comparison = await CompareAsync(
            "int x = 1;\n",
            "    int x = 1;\n",
            ComparisonOptions.Default with { IgnoreWhitespace = true });

        var ignored = Assert.Single(IgnoredRows(comparison));

        var span = Assert.Single(ignored.RightSpans);
        Assert.Equal(0, span.Start);
        Assert.Equal(4, span.End);

        // And what it covers really is only the spaces.
        Assert.Equal("    ", ignored.RightText![span.Start..span.End]);
    }

    [Fact]
    public async Task Trailing_whitespace_is_marked_at_the_end_and_nowhere_else()
    {
        var comparison = await CompareAsync(
            "int x = 1;  \n",
            "int x = 1;\n",
            ComparisonOptions.Default with { IgnoreWhitespace = true });

        var ignored = Assert.Single(IgnoredRows(comparison));

        var span = Assert.Single(ignored.LeftSpans);
        Assert.Equal("  ", ignored.LeftText![span.Start..span.End]);
        Assert.Equal(10, span.Start);
    }

    [Fact]
    public async Task An_ordinary_unchanged_row_still_gets_no_spans()
    {
        // The spans exist to localise a difference. A row with none must not acquire any, or every
        // identical line in the file would be carrying an empty highlight the renderers have to skip.
        var comparison = await CompareAsync(
            "int x = 1;\nint y = 2;\n",
            "    int x = 1;\nint y = 2;\n",
            ComparisonOptions.Default with { IgnoreWhitespace = true });

        var untouched = comparison.Result.Lines.Where(l => !l.IsIgnored && !l.IsChange);

        Assert.All(untouched, line =>
        {
            Assert.Empty(line.LeftSpans);
            Assert.Empty(line.RightSpans);
        });
    }

    [Fact]
    public async Task The_mark_keeps_it_out_of_the_differences()
    {
        // Faint, and free: IsIgnored is what keeps a row out of the hunks, the counts and next/previous
        // while still letting a renderer draw it.
        var comparison = await CompareAsync(
            "int x = 1;\n",
            "    int x = 1;\n",
            ComparisonOptions.Default with { IgnoreWhitespace = true });

        Assert.True(comparison.Result.AreIdentical);
        Assert.Empty(comparison.Result.Hunks);
        Assert.Single(IgnoredRows(comparison));
    }

    [Fact]
    public async Task Without_the_option_it_is_an_ordinary_difference_not_an_ignored_one()
    {
        var comparison = await CompareAsync(
            "int x = 1;\n",
            "    int x = 1;\n",
            ComparisonOptions.Default);

        Assert.Empty(IgnoredRows(comparison));
        Assert.NotEmpty(comparison.Result.Hunks);
    }

    [Fact]
    public async Task Lines_that_genuinely_agree_are_not_marked()
    {
        // The mark has to mean something. If every unchanged row carried it, it would carry nothing.
        var comparison = await CompareAsync(
            "same\nlines\nhere\n",
            "same\nlines\nhere\n",
            ComparisonOptions.Default with { IgnoreWhitespace = true, IgnoreCase = true });

        Assert.Empty(IgnoredRows(comparison));
    }

    // ---- The other options that equalise a line ------------------------------------------------

    [Fact]
    public async Task Ignore_case_marks_too()
    {
        var comparison = await CompareAsync(
            "int Value = 1;\n",
            "int VALUE = 1;\n",
            ComparisonOptions.Default with { IgnoreCase = true });

        Assert.Single(IgnoredRows(comparison));
    }

    [Fact]
    public async Task Ignore_comments_marks_too()
    {
        // One implementation covers every option that equalises a line, because it compares the two RAW
        // texts after projection rather than knowing which rule ran.
        var comparison = await CompareAsync(
            "int x = 1; // one note\n",
            "int x = 1; // a different note\n",
            ComparisonOptions.Default with { Code = ComparisonOptions.Default.Code with { IgnoreComments = true } });

        Assert.Single(IgnoredRows(comparison));
    }

    [Fact]
    public async Task An_ignored_line_pattern_marks_too()
    {
        var comparison = await CompareAsync(
            "built at 2024-01-01\n",
            "built at 2025-06-06\n",
            ComparisonOptions.Default with { IgnoredLinePatterns = [@"\d{4}-\d{2}-\d{2}"] });

        Assert.Single(IgnoredRows(comparison));
    }

    // ---- Not everything is a suppressed difference ---------------------------------------------

    [Fact]
    public async Task A_filler_row_is_never_marked()
    {
        // It has no counterpart to differ from; marking it would put a band on the blank half of every
        // insertion.
        var comparison = await CompareAsync(
            "one\ntwo\n",
            "one\ninserted\ntwo\n",
            ComparisonOptions.Default with { IgnoreWhitespace = true });

        Assert.DoesNotContain(IgnoredRows(comparison), l => l.Kind == ChangeKind.Filler);
    }

    [Fact]
    public async Task A_row_that_is_already_a_reported_change_is_not_marked()
    {
        // It is drawn as the change it is; a faint "and it also differs" band would say nothing.
        var comparison = await CompareAsync(
            "alpha\n",
            "beta\n",
            ComparisonOptions.Default with { IgnoreWhitespace = true });

        Assert.Empty(IgnoredRows(comparison));
    }
}
