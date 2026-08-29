using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Languages;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Infrastructure.Comparison;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// The merge end to end, through the REAL aligner and normalizer.
///
/// The Core tests drive <c>ThreeWayMerger</c> with a textbook LCS alignment; these drive it with the
/// alignment the app actually produces - DiffPlex, then the slider - which is the pairing that has to
/// work in front of a user.
/// </summary>
public class ThreeWayComparisonServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class StubReader(Dictionary<string, string[]> files) : ITextFileReader
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            files.TryGetValue(path, out var lines)
                ? Task.FromResult(new TextDocument(path, lines, TextFormat.Default))
                : throw new TextFileReadException(path, "the file does not exist.");
    }

    /// <summary>Captures what would be written, so a save can be asserted without touching a disk.</summary>
    private sealed class RecordingWriter : ITextFileWriter
    {
        public string? Path { get; private set; }

        public IReadOnlyList<string>? Lines { get; private set; }

        public Task WriteAsync(
            string path,
            IReadOnlyList<string> lines,
            TextFormat format,
            CancellationToken cancellationToken = default)
        {
            Path = path;
            Lines = lines;

            return Task.CompletedTask;
        }
    }

    private static Task<ThreeWayComparison> Merge(
        string[] ancestor,
        string[] left,
        string[] right,
        ComparisonOptions? options = null,
        string extension = ".txt")
    {
        var files = new Dictionary<string, string[]>
        {
            ["base" + extension] = ancestor,
            ["left" + extension] = left,
            ["right" + extension] = right,
        };

        var service = new ThreeWayComparisonService(
            new StubReader(files),
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer());

        return service.CompareFilesAsync(
            "base" + extension,
            "left" + extension,
            "right" + extension,
            options ?? ComparisonOptions.Default,
            Token);
    }

    private static string[] Merged(ThreeWayComparison comparison, ThreeWayMergeState? state = null) =>
        [.. ThreeWayMergedDocument.Build(comparison.Result, state ?? ThreeWayMergeState.Empty)];

    [Fact]
    public async Task Three_identical_files_have_nothing_to_merge()
    {
        var comparison = await Merge(["a", "b"], ["a", "b"], ["a", "b"]);

        Assert.True(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task Independent_edits_are_merged_without_asking()
    {
        // The whole point of the feature, through the real engine.
        var comparison = await Merge(
            ["one", "two", "three", "four", "five"],
            ["ONE", "two", "three", "four", "five"],
            ["one", "two", "three", "four", "FIVE"]);

        Assert.Equal(0, comparison.Result.ConflictCount);
        Assert.Equal(["ONE", "two", "three", "four", "FIVE"], Merged(comparison));
    }

    [Fact]
    public async Task Two_edits_to_the_same_line_conflict()
    {
        var comparison = await Merge(
            ["one", "two", "three"],
            ["one", "LEFT", "three"],
            ["one", "RIGHT", "three"]);

        Assert.Equal(1, comparison.Result.ConflictCount);
        Assert.Equal(1, ThreeWayMergeState.Empty.UnresolvedConflicts(comparison.Result));
    }

    [Fact]
    public async Task Two_methods_appended_at_the_same_point_conflict()
    {
        // Both sides added a method in the same gap - between the last method's brace and the class's.
        // There is no surviving line between the two insertions, so there is no way to say which comes
        // first, and no basis for the tool to choose. git reports this the same way, and inventing an
        // order here would be inventing a merge nobody approved.
        string[] ancestor = ["class C", "{", "    void A()", "    {", "        a();", "    }", "}"];

        string[] left =
        [
            "class C", "{", "    void A()", "    {", "        a();", "    }", "",
            "    void FromLeft()", "    {", "        l();", "    }", "}",
        ];

        string[] right =
        [
            "class C", "{", "    void A()", "    {", "        a();", "    }", "", "    void FromRight()",
            "    {", "        r();", "    }", "}",
        ];

        var comparison = await Merge(ancestor, left, right, extension: ".cs");

        Assert.Equal(SourceLanguage.CSharp, comparison.Language);
        Assert.Equal(1, comparison.Result.ConflictCount);

        // ...and picking a side gives that side's method, whole.
        var merged = Merged(comparison, ThreeWayMergeState.Empty.With(0, MergeChoice.TakeLeft));
        Assert.Contains("    void FromLeft()", merged);
        Assert.DoesNotContain("    void FromRight()", merged);
    }

    [Fact]
    public async Task Methods_added_in_different_places_merge_cleanly()
    {
        // The same edit made in two places that are separated by surviving code: both land, nobody is
        // asked anything. This is what a three-way merge buys over comparing two files.
        string[] ancestor =
        [
            "class C", "{", "    void Middle()", "    {", "        m();", "    }", "}",
        ];

        string[] left =
        [
            "class C", "{", "    void FromLeft()", "    {", "        l();", "    }", "",
            "    void Middle()", "    {", "        m();", "    }", "}",
        ];

        string[] right =
        [
            "class C", "{", "    void Middle()", "    {", "        m();", "    }", "",
            "    void FromRight()", "    {", "        r();", "    }", "}",
        ];

        var comparison = await Merge(ancestor, left, right, extension: ".cs");
        var merged = Merged(comparison);

        Assert.Equal(0, comparison.Result.ConflictCount);
        Assert.Contains("    void FromLeft()", merged);
        Assert.Contains("    void FromRight()", merged);
        Assert.Contains("    void Middle()", merged);
    }

    [Fact]
    public async Task A_merge_with_no_conflicts_still_contains_every_original_line_it_should()
    {
        var comparison = await Merge(
            ["header", "a", "b", "c", "footer"],
            ["header", "a", "b", "c", "footer", "left tail"],
            ["right head", "header", "a", "b", "c", "footer"]);

        var merged = Merged(comparison);

        Assert.Equal(0, comparison.Result.ConflictCount);
        Assert.Equal(["right head", "header", "a", "b", "c", "footer", "left tail"], merged);
    }

    [Fact]
    public async Task Comparison_options_reach_the_merge()
    {
        // With case folding on, the two edits are the same edit, so there is nothing to resolve.
        var comparison = await Merge(
            ["one", "two"],
            ["one", "EDITED"],
            ["one", "edited"],
            new ComparisonOptions { IgnoreCase = true });

        Assert.Equal(0, comparison.Result.ConflictCount);
        Assert.Equal(MergeKind.BothSame, Assert.Single(comparison.Result.Regions).Kind);
    }

    [Fact]
    public async Task The_code_rules_reach_the_merge_too()
    {
        // Each side changed only a comment, differently. With comments ignored, neither changed
        // anything at all.
        var comparison = await Merge(
            ["foo(); // original"],
            ["foo(); // left note"],
            ["foo(); // right note"],
            new ComparisonOptions { Code = new CodeComparisonOptions { IgnoreComments = true } },
            ".cs");

        Assert.True(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task Without_the_code_rules_the_same_pair_conflicts()
    {
        var comparison = await Merge(
            ["foo(); // original"],
            ["foo(); // left note"],
            ["foo(); // right note"],
            options: null,
            ".cs");

        Assert.Equal(1, comparison.Result.ConflictCount);
    }

    [Fact]
    public async Task Displayed_text_is_the_document_not_the_comparison_key()
    {
        // The invariant that outranks everything: with case folding on, the merge matched on folded
        // keys, but the panes must show the user their own file.
        var comparison = await Merge(
            ["One"],
            ["One"],
            ["ONE"],
            new ComparisonOptions { IgnoreCase = true });

        Assert.Equal("One", comparison.Result.Lines[0].BaseText);
        Assert.Equal("ONE", comparison.Result.Lines[0].RightText);
    }

    [Fact]
    public async Task Recomparing_applies_new_options_without_re_reading()
    {
        var files = new Dictionary<string, string[]>
        {
            ["base.txt"] = ["one", "two"],
            ["left.txt"] = ["one", "EDITED"],
            ["right.txt"] = ["one", "edited"],
        };

        var service = new ThreeWayComparisonService(
            new StubReader(files),
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer());

        var first = await service.CompareFilesAsync("base.txt", "left.txt", "right.txt", ComparisonOptions.Default, Token);
        Assert.Equal(1, first.Result.ConflictCount);

        // The reader would throw for any path it does not know, so reaching this at all proves nothing
        // was read again.
        var second = await service.RecompareAsync(first, new ComparisonOptions { IgnoreCase = true }, Token);
        Assert.Equal(0, second.Result.ConflictCount);
    }

    // ---- Character spans ------------------------------------------------------------------------

    [Fact]
    public async Task Each_edit_reports_what_it_altered_within_the_line()
    {
        // The gap this closes: two nearly-identical conflicting lines used to have to be read side by
        // side, because the only signal was a full-row tint on both.
        var comparison = await Merge(
            ["timeout = 30;"],
            ["timeout = 45;"],
            ["timeout = 60;"],
            extension: ".cs");

        var row = Assert.Single(comparison.Result.Lines);

        Assert.Equal(["45"], Selected(row.LeftText!, row.LeftSpans));
        Assert.Equal(["60"], Selected(row.RightText!, row.RightSpans));
    }

    [Fact]
    public async Task The_side_that_did_not_change_reports_nothing()
    {
        var comparison = await Merge(["a = 1;"], ["a = 2;"], ["a = 1;"], extension: ".cs");

        var row = Assert.Single(comparison.Result.Lines);

        Assert.NotEmpty(row.LeftSpans);
        Assert.Empty(row.RightSpans);
    }

    [Fact]
    public async Task A_line_with_no_ancestor_counterpart_gets_no_spans()
    {
        // The whole row is the change; picking out characters within it would be noise.
        var comparison = await Merge(["a"], ["a", "brand new"], ["a"]);

        var added = Assert.Single(comparison.Result.Lines, l => l.LeftText == "brand new");

        Assert.Empty(added.LeftSpans);
    }

    [Fact]
    public async Task Unchanged_rows_get_no_spans()
    {
        var comparison = await Merge(["keep", "a"], ["keep", "b"], ["keep", "a"]);

        var kept = comparison.Result.Lines[0];

        Assert.Equal(MergeKind.Unchanged, kept.Kind);
        Assert.Empty(kept.LeftSpans);
        Assert.Empty(kept.RightSpans);
    }

    [Fact]
    public async Task Spans_address_the_display_text_even_when_the_keys_were_folded()
    {
        // Offsets computed against a normalised key would point at the wrong characters, silently.
        var comparison = await Merge(
            ["  value = 1;"],
            ["  value = 2;"],
            ["  value = 1;"],
            new ComparisonOptions { IgnoreWhitespace = true },
            ".cs");

        var row = Assert.Single(comparison.Result.Lines);

        Assert.All(row.LeftSpans, span =>
            Assert.True(span.Start >= 0 && span.End <= row.LeftText!.Length, "a span must address the display text"));
        Assert.Equal(["2"], Selected(row.LeftText!, row.LeftSpans));
    }

    private static string[] Selected(string text, IReadOnlyList<Core.Models.CharSpan> spans) =>
        [.. spans.Select(s => text.Substring(s.Start, s.Length))];

    // ---- Saving ---------------------------------------------------------------------------------

    [Fact]
    public async Task Saving_writes_the_merged_content_to_the_chosen_destination()
    {
        var comparison = await Merge(
            ["one", "two", "three"],
            ["ONE", "two", "three"],
            ["one", "two", "THREE"]);

        var writer = new RecordingWriter();
        var path = await new MergeService(writer).SaveThreeWayAsync(
            comparison,
            ThreeWayMergeState.Empty,
            MergeSide.Right,
            cancellationToken: Token);

        Assert.Equal("right.txt", path);
        Assert.Equal(["ONE", "two", "THREE"], writer.Lines);
    }

    [Fact]
    public async Task Save_as_writes_somewhere_else_entirely()
    {
        var comparison = await Merge(["a"], ["a"], ["a"]);

        var writer = new RecordingWriter();
        var path = await new MergeService(writer).SaveThreeWayAsync(
            comparison,
            ThreeWayMergeState.Empty,
            MergeSide.Right,
            "merged.txt",
            Token);

        Assert.Equal("merged.txt", path);
    }

    [Fact]
    public async Task Saving_past_an_unresolved_conflict_keeps_the_ancestor_rather_than_refusing()
    {
        // Deliberate: the domain has a defined answer, and a service that threw would make "save what
        // I have so far" impossible half way through a long merge. Warning is the UI's job.
        var comparison = await Merge(
            ["one", "two", "three"],
            ["one", "LEFT", "three"],
            ["one", "RIGHT", "three"]);

        var writer = new RecordingWriter();
        await new MergeService(writer).SaveThreeWayAsync(
            comparison,
            ThreeWayMergeState.Empty,
            MergeSide.Right,
            cancellationToken: Token);

        Assert.Equal(["one", "two", "three"], writer.Lines);
    }

    [Fact]
    public async Task A_resolved_conflict_is_what_gets_written()
    {
        var comparison = await Merge(
            ["one", "two", "three"],
            ["one", "LEFT", "three"],
            ["one", "RIGHT", "three"]);

        var writer = new RecordingWriter();
        await new MergeService(writer).SaveThreeWayAsync(
            comparison,
            ThreeWayMergeState.Empty.With(0, MergeChoice.TakeLeft),
            MergeSide.Right,
            cancellationToken: Token);

        Assert.Equal(["one", "LEFT", "three"], writer.Lines);
    }

    [Fact]
    public async Task A_merge_with_no_destination_path_fails_loudly()
    {
        var comparison = await Merge(["a"], ["a"], ["a"]);
        var stripped = comparison with { Right = comparison.Right with { Path = string.Empty } };

        await Assert.ThrowsAsync<TextFileWriteException>(() =>
            new MergeService(new RecordingWriter()).SaveThreeWayAsync(
                stripped,
                ThreeWayMergeState.Empty,
                MergeSide.Right,
                cancellationToken: Token));
    }
}
