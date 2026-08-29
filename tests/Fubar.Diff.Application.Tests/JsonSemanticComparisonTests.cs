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
        // The contract the renderers depend on: GIVEN THE SAME TEXT TO ALIGN, semantic refinement only
        // downgrades change kinds - it never adds, removes or re-pairs rows.
        //
        // "The same text" is not the ORIGINAL source here: semantic mode reformats JSON before
        // aligning (see FileComparisonService.Compare and CanonicalizeJson's doc comment) precisely so
        // a minified file diffed against a pretty one still lines up sanely, so its row count can
        // legitimately differ from a literal-bytes Text-mode run of the un-reformatted source. What
        // must still hold is that semantic mode does not itself reshape the alignment it computes from
        // whatever text it ends up using - which this checks by re-running Text mode over THAT exact
        // (already-canonicalized) text and comparing shapes.
        const string left = "{\n  \"a\": 1,\n  \"b\": 2\n}";
        const string right = "{\n  \"b\": 2,\n  \"a\": 1\n}";

        var semantic = await CompareAsync(left, right);

        var canonicalLeft = string.Join('\n', semantic.Left.Lines);
        var canonicalRight = string.Join('\n', semantic.Right.Lines);
        var text = await Build(canonicalLeft, canonicalRight)
            .CompareFilesAsync("left", "right", ComparisonOptions.Default with { Mode = ComparisonMode.Text }, Token);

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

    // ---- Alignment when the two sides are formatted very differently ----------------------------
    //
    // Text mode used to pretty-print both sides before alignment automatically, specifically so a
    // minified-vs-pretty pair still aligned sanely. That was removed: it silently rewrote the user's
    // content to compensate for Text mode's own alignment limitation, solving the problem in the
    // wrong view. The Json view exists precisely for this case - no alignment needed at all, and it
    // is the default the moment semantic comparison applies - so Text mode is free to just show
    // exactly what it was given, however badly that then lines up as text.

    /// <summary>
    /// Neither side is reformatted, even though the pairing is exactly the case that used to trigger
    /// automatic canonicalisation. The SEMANTIC comparison is unaffected either way - it parses and
    /// diffs the AST directly, never the text alignment - so the one real difference is still found
    /// precisely regardless of how the two sides look as text.
    /// </summary>
    [Fact]
    public async Task A_minified_file_compared_against_a_pretty_one_is_never_reformatted()
    {
        const string pretty = """
            {
              "glossary": {
                "title": "example glossary",
                "GlossDiv": {
                  "title": "S",
                  "GlossList": {
                    "GlossEntry": {
                      "ID": "SGML",
                      "GlossSee": "markup"
                    }
                  }
                }
              }
            }
            """;

        const string minified =
            "{\"glossary\":{\"title\":\"example glossary\",\"GlossDiv\":{\"title\":\"S\"," +
            "\"GlossList\":{\"GlossEntry\":{\"ID\":\"SGML\",\"GlossSee\":\"gone\"}}}}}";

        var comparison = await CompareAsync(pretty, minified);

        Assert.True(comparison.IsSemantic);

        Assert.Single(comparison.Right.Lines);
        Assert.Equal(pretty.Split('\n').Length, comparison.Left.Lines.Count);

        var change = Assert.Single(comparison.SemanticChanges);
        Assert.Equal("$.glossary.GlossDiv.GlossList.GlossEntry.GlossSee", change.Path.ToString());
    }

    /// <summary>Mode=Text never reformatted for alignment either - this just confirms Auto now agrees.</summary>
    [Fact]
    public async Task Text_mode_does_not_canonicalize_for_alignment_either()
    {
        var options = ComparisonOptions.Default with { Mode = ComparisonMode.Text };

        var comparison = await CompareAsync("{\"a\":1}", "{\"a\":1}", options);

        Assert.Equal("{\"a\":1}", Assert.Single(comparison.Left.Lines));
    }

    // ---- Original (unaligned) text, for the Json view ---------------------------------------------

    /// <summary>
    /// The common case, and worth pinning explicitly now that nothing reformats JSON automatically:
    /// OriginalLeftText/RightText and the displayed Left/Right are simply the SAME text by default.
    /// They exist for the one case where that is no longer true - see the NormalizeStructure test
    /// below - not because Text mode routinely rewrites what it shows.
    /// </summary>
    [Fact]
    public async Task Original_text_equals_the_displayed_text_by_default()
    {
        const string minified = "{\"a\":1,\"nested\":{\"b\":2}}";

        var comparison = await CompareAsync("{\"a\":1}", minified);

        Assert.Equal(minified, comparison.OriginalRightText);
        Assert.Equal(minified, string.Join('\n', comparison.Right.Lines));
    }

    /// <summary>
    /// The one remaining way Left/Right can still diverge from the original: the user explicitly
    /// turns on "Reformat" (NormalizeStructure) for a JSON file, which still reaches the JSON
    /// branch of Canonicalize. Even then, the Json view's copy must stay the TRUE original - that
    /// view's whole point is showing what is actually there, regardless of what Text mode is
    /// currently configured to display.
    /// </summary>
    [Fact]
    public async Task Original_text_stays_true_even_when_NormalizeStructure_reformats_the_displayed_copy()
    {
        const string minified = "{\"a\":1,\"nested\":{\"b\":2}}";
        var options = ComparisonOptions.Default with { NormalizeStructure = true };

        var comparison = await CompareAsync("{\n  \"a\": 1,\n  \"nested\": {\n    \"b\": 2\n  }\n}", minified, options);

        Assert.NotEqual(1, comparison.Right.Lines.Count);
        Assert.Equal(minified, comparison.OriginalRightText);
    }

    /// <summary>
    /// Spans in OriginalSemanticChanges must address OriginalRightText/LeftText, not the canonicalized
    /// Left/Right - otherwise the Json view would highlight the wrong characters entirely.
    /// </summary>
    [Fact]
    public async Task Original_semantic_changes_have_spans_into_the_original_text()
    {
        const string minified = "{\"a\":1}";

        var comparison = await CompareAsync("{\n  \"a\": 2\n}", minified);

        var change = Assert.Single(comparison.OriginalSemanticChanges);
        var span = Assert.NotNull(change.Right?.Span);

        // The minified right side is one line; a span into the CANONICALIZED (multi-line) text would
        // report a line number that does not exist in the one-line original.
        Assert.Equal(1, span.StartLine);
    }

    /// <summary>
    /// The path/kind/count must be identical between the two representations - same logical change,
    /// only the span differs - which is what lets a caller pair them up by position (see
    /// JsonSemanticPass.TryCompareOriginalText's doc comment for why that pairing is safe).
    /// </summary>
    [Fact]
    public async Task Original_and_canonicalized_semantic_changes_agree_on_everything_but_span()
    {
        var comparison = await CompareAsync("{\"a\":1,\"b\":2}", "{\"a\":9,\"b\":2}");

        var canonical = Assert.Single(comparison.SemanticChanges);
        var original = Assert.Single(comparison.OriginalSemanticChanges);

        Assert.Equal(canonical.Path.ToString(), original.Path.ToString());
        Assert.Equal(canonical.Kind, original.Kind);
    }

    /// <summary>
    /// The regression this pins: a naive implementation reads "original text" fresh from whatever
    /// Left/Right happen to hold at recompare time - which, one recompare after the fresh load, IS
    /// the canonicalized text, silently losing the true original the moment any option is toggled.
    /// </summary>
    [Fact]
    public async Task Original_text_survives_a_recompare()
    {
        const string minified = "{\"a\":1}";
        var service = Build("{\n  \"a\": 1\n}", minified);

        var first = await service.CompareFilesAsync("left", "right", ComparisonOptions.Default, Token);
        var recompared = await service.RecompareAsync(
            first,
            ComparisonOptions.Default with { IgnoreCase = true },
            Token);

        Assert.Equal(minified, recompared.OriginalRightText);
    }
}
