using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Files;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// The semantic path end to end, through the real parser, differ and text engine - so these assert
/// what the app actually shows, not what a fake agreed to.
///
/// The contract being pinned: semantic refinement changes which rows COUNT as changes, and nothing
/// else. The alignment, the filler rows and the row count all stay exactly as the text pass produced
/// them, which is what lets every renderer, the diff map, navigation and merge work in both modes.
/// </summary>
public class JsonSemanticComparisonTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class StubReader(Dictionary<string, string[]> files) : ITextFileReader
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            files.TryGetValue(path, out var lines)
                ? Task.FromResult(new TextDocument(path, lines, TextFormat.Default))
                : throw new TextFileReadException(path, "the file does not exist.");
    }

    /// <summary>Builds the real pipeline over two in-memory documents.</summary>
    private static FileComparisonService Build(string leftText, string rightText)
    {
        var files = new Dictionary<string, string[]>
        {
            ["left"] = leftText.Split('\n'),
            ["right"] = rightText.Split('\n'),
        };

        return new FileComparisonService(
            new StubReader(files),
            new DiffPlexDiffEngine(),
            new DiffPlexInlineDiffEngine(),
            new TextLineNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));
    }

    private static Task<FileComparison> CompareAsync(
        string leftText,
        string rightText,
        ComparisonOptions? options = null) =>
        Build(leftText, rightText)
            .CompareFilesAsync("left", "right", options ?? ComparisonOptions.Default, Token);

    // ---- The headline behaviours ----------------------------------------------------------------

    [Fact]
    public async Task Reordered_properties_report_as_identical()
    {
        // A text diff calls this two changed lines. The whole point of Phase 3 is that it is not a
        // change at all: JSON objects are unordered.
        var comparison = await CompareAsync(
            "{\n  \"a\": 1,\n  \"b\": 2\n}",
            "{\n  \"b\": 2,\n  \"a\": 1\n}");

        Assert.True(comparison.IsSemantic);
        Assert.True(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task Reformatting_alone_reports_as_identical()
    {
        var comparison = await CompareAsync(
            "{\"a\":1,\"b\":[2,3]}",
            "{\n  \"a\": 1,\n  \"b\": [\n    2,\n    3\n  ]\n}");

        Assert.True(comparison.IsSemantic);
        Assert.True(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task A_real_value_change_is_still_reported()
    {
        var comparison = await CompareAsync(
            "{\n  \"a\": 1\n}",
            "{\n  \"a\": 2\n}");

        Assert.True(comparison.IsSemantic);
        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task An_element_inserted_mid_array_marks_only_itself()
    {
        var left = "{\n  \"items\": [\n    {\"id\": 1},\n    {\"id\": 2}\n  ]\n}";
        var right = "{\n  \"items\": [\n    {\"id\": 1},\n    {\"id\": 9},\n    {\"id\": 2}\n  ]\n}";

        var comparison = await CompareAsync(left, right);

        Assert.True(comparison.IsSemantic);

        // One inserted line, and nothing else touched - the id:2 element must not be reported just
        // because it moved down.
        Assert.Equal(1, comparison.Result.Inserted);
        Assert.Equal(0, comparison.Result.Deleted);
        Assert.Equal(0, comparison.Result.Modified);
    }

    [Fact]
    public async Task Reordered_keyed_array_elements_report_as_identical()
    {
        var left = "{\n  \"items\": [\n    {\"id\": 1},\n    {\"id\": 2}\n  ]\n}";
        var right = "{\n  \"items\": [\n    {\"id\": 2},\n    {\"id\": 1}\n  ]\n}";

        Assert.True((await CompareAsync(left, right)).Result.AreIdentical);
    }

    [Fact]
    public async Task Property_order_can_be_reported_when_asked_for()
    {
        var options = ComparisonOptions.Default with
        {
            Json = new JsonComparisonOptions { ReportPropertyOrder = true },
        };

        var comparison = await CompareAsync(
            "{\n  \"a\": 1,\n  \"b\": 2\n}",
            "{\n  \"b\": 2,\n  \"a\": 1\n}",
            options);

        Assert.False(comparison.Result.AreIdentical);
    }

    // ---- Fallback ------------------------------------------------------------------------------

    [Fact]
    public async Task Plain_text_falls_back_silently()
    {
        // Most files are not JSON. Failing to parse one is not news, so nothing is reported.
        var comparison = await CompareAsync("hello\nworld", "hello\nthere");

        Assert.False(comparison.IsSemantic);
        Assert.Null(comparison.SemanticFallbackReason);
        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task Malformed_json_still_diffs_as_text()
    {
        // A broken file is exactly when a diff is most wanted.
        var comparison = await CompareAsync("{\n  \"a\": 1,\n", "{\n  \"a\": 2,\n");

        Assert.False(comparison.IsSemantic);
        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task Explicitly_requesting_json_explains_a_parse_failure()
    {
        // Silence is right for Auto, but a user who asked for JSON deserves to know why they did not
        // get it.
        var options = ComparisonOptions.Default with { Mode = ComparisonMode.Json };

        var comparison = await CompareAsync("not json", "also not json", options);

        Assert.False(comparison.IsSemantic);
        Assert.NotNull(comparison.SemanticFallbackReason);
        Assert.Contains("left", comparison.SemanticFallbackReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Text_mode_skips_the_semantic_pass_entirely()
    {
        var options = ComparisonOptions.Default with { Mode = ComparisonMode.Text };

        var comparison = await CompareAsync(
            "{\n  \"a\": 1,\n  \"b\": 2\n}",
            "{\n  \"b\": 2,\n  \"a\": 1\n}",
            options);

        Assert.False(comparison.IsSemantic);
        Assert.False(comparison.Result.AreIdentical);   // the text differs, and text mode says so
    }

    // ---- Shape invariants ------------------------------------------------------------------------

    [Fact]
    public async Task Refinement_preserves_the_alignment_row_for_row()
    {
        // The contract the renderers depend on: semantic refinement only downgrades change kinds, it
        // never adds, removes or re-pairs rows.
        const string left = "{\n  \"a\": 1,\n  \"b\": 2\n}";
        const string right = "{\n  \"b\": 2,\n  \"a\": 1\n}";

        var semantic = await CompareAsync(left, right);
        var text = await CompareAsync(left, right, ComparisonOptions.Default with { Mode = ComparisonMode.Text });

        Assert.Equal(text.Result.Lines.Count, semantic.Result.Lines.Count);

        for (var i = 0; i < text.Result.Lines.Count; i++)
        {
            Assert.Equal(text.Result.Lines[i].LeftNumber, semantic.Result.Lines[i].LeftNumber);
            Assert.Equal(text.Result.Lines[i].RightNumber, semantic.Result.Lines[i].RightNumber);
            Assert.Equal(text.Result.Lines[i].LeftText, semantic.Result.Lines[i].LeftText);
        }
    }

    [Fact]
    public async Task A_downgraded_row_keeps_its_text_but_loses_its_spans()
    {
        var comparison = await CompareAsync(
            "{\n  \"a\": 1,\n  \"b\": 2\n}",
            "{\n  \"b\": 2,\n  \"a\": 1\n}");

        Assert.All(comparison.Result.Lines, line =>
        {
            Assert.Empty(line.LeftSpans);
            Assert.Empty(line.RightSpans);
        });
    }

    [Fact]
    public async Task Semantic_changes_are_exposed_for_the_tree_view()
    {
        var comparison = await CompareAsync("{\n  \"a\": 1\n}", "{\n  \"a\": 2\n}");

        var change = Assert.Single(comparison.SemanticChanges);
        Assert.Equal("$.a", change.Path.ToString());
        Assert.Equal(ChangeKind.Modified, change.Kind);
    }

    [Fact]
    public async Task Identical_json_reports_no_semantic_changes()
    {
        var comparison = await CompareAsync("{\n  \"a\": 1\n}", "{\n  \"a\": 1\n}");

        Assert.Empty(comparison.SemanticChanges);
        Assert.True(comparison.Result.AreIdentical);
    }
}
