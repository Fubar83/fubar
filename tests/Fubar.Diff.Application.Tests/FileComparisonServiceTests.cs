using Fubar.Diff.Infrastructure.Json;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Languages;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// The orchestration contract. The engine and normalizer are faked so these test the ORDER of
/// operations and the projection rule, which is what this service actually owns.
/// </summary>
public class FileComparisonServiceTests
{
    /// <summary>A trivial engine: matches keys positionally and pairs up whatever is there.</summary>
    private sealed class PositionalEngine : IDiffEngine
    {
        public IReadOnlyList<string> LastLeftKeys { get; private set; } = [];
        public IReadOnlyList<string> LastRightKeys { get; private set; } = [];

        public IReadOnlyList<DiffLine> Align(
            IReadOnlyList<string> left, IReadOnlyList<string> right, ComparisonOptions options)
        {
            LastLeftKeys = left;
            LastRightKeys = right;

            var count = Math.Max(left.Count, right.Count);
            var rows = new List<DiffLine>(count);

            for (var i = 0; i < count; i++)
            {
                var l = i < left.Count ? i + 1 : (int?)null;
                var r = i < right.Count ? i + 1 : (int?)null;

                var kind = (l, r) switch
                {
                    (null, not null) => ChangeKind.Inserted,
                    (not null, null) => ChangeKind.Deleted,
                    _ when left[i] != right[i] => ChangeKind.Modified,
                    _ => ChangeKind.Unchanged,
                };

                // Deliberately echoes the KEYS back as text - the service must overwrite them.
                rows.Add(new DiffLine(l, l is null ? null : left[i], r, r is null ? null : right[i], kind));
            }

            return rows;
        }
    }

    private sealed class StubReader(Dictionary<string, string[]> files) : ITextFileReader
    {
        public Task<TextDocument> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            files.TryGetValue(path, out var lines)
                ? Task.FromResult(new TextDocument(path, lines, TextFormat.Default))
                : throw new TextFileReadException(path, "the file does not exist.");
    }

    private sealed class UpperCasingNormalizer : ILineNormalizer
    {
        public string ToComparisonKey(string line, ComparisonOptions options) =>
            options.IgnoreCase ? line.ToUpperInvariant() : line;

        public IReadOnlyList<string> Canonicalize(IReadOnlyList<string> lines, ComparisonOptions options) =>
            options.NormalizeStructure ? [.. lines.Select(l => l.Trim())] : lines;
    }

    /// <summary>
    /// Marks the whole of each side as changed. Trivial on purpose: these tests are about WHICH text
    /// the spans are computed against, not about how words are matched within a line.
    /// </summary>
    private sealed class WholeLineInlineEngine : IInlineDiffEngine
    {
        public string? LastLeft { get; private set; }

        public string? LastRight { get; private set; }

        /// <summary>The language the service decided the pair was, so a test can assert it was detected.</summary>
        public SourceLanguage LastLanguage { get; private set; } = SourceLanguage.None;

        public (IReadOnlyList<CharSpan> Left, IReadOnlyList<CharSpan> Right) DiffWithinLine(
            string left,
            string right,
            SourceLanguage language = SourceLanguage.None)
        {
            LastLeft = left;
            LastRight = right;
            LastLanguage = language;

            return (
                left.Length > 0 ? [new CharSpan(0, left.Length, ChangeKind.Deleted)] : [],
                right.Length > 0 ? [new CharSpan(0, right.Length, ChangeKind.Inserted)] : []);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (FileComparisonService Service, PositionalEngine Engine, WholeLineInlineEngine Inline) Build(
        params (string Path, string[] Lines)[] files)
    {
        var engine = new PositionalEngine();
        var inline = new WholeLineInlineEngine();
        var reader = new StubReader(files.ToDictionary(f => f.Path, f => f.Lines));

        var service = new FileComparisonService(
            reader,
            engine,
            inline,
            new UpperCasingNormalizer(),
            new JsonSemanticPass(new JsonAstParser()));

        return (service, engine, inline);
    }

    [Fact]
    public async Task Displayed_text_comes_from_the_document_not_the_comparison_key()
    {
        // The regression this guards: with IgnoreCase on, the engine is fed upper-cased keys. If the
        // service failed to project the rows back onto the documents, the user would be shown a
        // SHOUTING copy of their own file.
        var (service, engine, _) = Build(
            ("left", ["Hello"]),
            ("right", ["hello"]));

        var comparison = await service.CompareFilesAsync(
            "left", "right", new ComparisonOptions { IgnoreCase = true }, Token);

        Assert.Equal("HELLO", engine.LastLeftKeys[0]);   // engine matched on the folded key...
        Assert.Equal("Hello", comparison.Result.Lines[0].LeftText);   // ...user sees the original
        Assert.Equal("hello", comparison.Result.Lines[0].RightText);
        Assert.Equal(ChangeKind.Unchanged, comparison.Result.Lines[0].Kind);
    }

    [Fact]
    public async Task Canonicalisation_output_IS_displayed()
    {
        // Unlike keys, canonicalised content is what the user is comparing, so it must be shown.
        var (service, _, _) = Build(
            ("left", ["   padded   "]),
            ("right", ["padded"]));

        var comparison = await service.CompareFilesAsync(
            "left", "right", new ComparisonOptions { NormalizeStructure = true }, Token);

        Assert.Equal("padded", comparison.Result.Lines[0].LeftText);
    }

    [Fact]
    public async Task Fillers_carry_no_text_on_the_missing_side()
    {
        var (service, _, _) = Build(
            ("left", ["a"]),
            ("right", ["a", "b"]));

        var comparison = await service.CompareFilesAsync("left", "right", ComparisonOptions.Default, Token);

        var inserted = comparison.Result.Lines[1];
        Assert.Null(inserted.LeftText);
        Assert.Equal("b", inserted.RightText);
    }

    [Fact]
    public async Task Recompare_does_not_touch_the_disk_again()
    {
        var (service, _, _) = Build(
            ("left", ["Hello"]),
            ("right", ["hello"]));

        var first = await service.CompareFilesAsync("left", "right", ComparisonOptions.Default, Token);
        Assert.False(first.Result.AreIdentical);

        // The stub reader would throw for any path it does not know; passing the loaded comparison
        // back in proves no read happened.
        var second = service.Recompare(first, new ComparisonOptions { IgnoreCase = true });

        Assert.True(second.Result.AreIdentical);
        Assert.Equal("Hello", second.Left.Lines[0]);
    }

    [Fact]
    public async Task A_missing_file_surfaces_as_a_domain_exception()
    {
        var (service, _, _) = Build(("left", ["a"]));

        var ex = await Assert.ThrowsAsync<TextFileReadException>(
            () => service.CompareFilesAsync("left", "nope", ComparisonOptions.Default, Token));

        Assert.Equal("nope", ex.Path);
    }

    [Fact]
    public async Task Inline_spans_are_computed_against_display_text_not_comparison_keys()
    {
        // The offset trap: with IgnoreWhitespace on, the key is trimmed, so a span computed against
        // the key would be shifted left by the indent and highlight the wrong characters. The inline
        // engine must therefore be handed the DISPLAY text.
        var (service, _, inline) = Build(
            ("left", ["      alpha"]),
            ("right", ["      omega"]));

        var comparison = await service.CompareFilesAsync(
            "left", "right", new ComparisonOptions { IgnoreCase = true }, Token);

        // Not "ALPHA" (the folded key), and not a trimmed variant.
        Assert.Equal("      alpha", inline.LastLeft);
        Assert.Equal("      omega", inline.LastRight);

        // And the resulting span addresses the full display string.
        var span = Assert.Single(comparison.Result.Lines[0].LeftSpans);
        Assert.Equal(0, span.Start);
        Assert.Equal("      alpha".Length, span.Length);
    }

    [Fact]
    public async Task Only_modified_rows_get_inline_spans()
    {
        // On a wholly inserted or deleted line the entire row is already the change, so picking out
        // characters within it would be noise.
        var (service, _, _) = Build(
            ("left", ["same", "gone"]),
            ("right", ["same"]));

        var comparison = await service.CompareFilesAsync("left", "right", ComparisonOptions.Default, Token);

        Assert.All(comparison.Result.Lines, line =>
        {
            // Ignored rows carry spans too, and for the opposite reason: their difference is usually a
            // couple of characters, so the renderers mark those instead of banding the whole row.
            if (line.Kind != ChangeKind.Modified && !line.IsIgnored)
            {
                Assert.Empty(line.LeftSpans);
                Assert.Empty(line.RightSpans);
            }
        });
    }

    [Fact]
    public async Task The_options_used_travel_with_the_result()
    {
        var (service, _, _) = Build(("left", ["a"]), ("right", ["a"]));
        var options = new ComparisonOptions { IgnoreWhitespace = true };

        var comparison = await service.CompareFilesAsync("left", "right", options, Token);

        Assert.Equal(options, comparison.Options);
        Assert.True(comparison.HasBothSides);
    }
}
