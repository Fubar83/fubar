using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests.Json;

/// <summary>
/// The Json view's per-side pretty-printing.
///
/// The property everything else rests on: the text and the change spans into it are produced
/// together. A change carries offsets into one specific string, so reformatting a side without
/// re-deriving them would leave every highlight pointing at the line a value used to be on - which
/// looks exactly like the comparison having gone wrong.
/// </summary>
public class JsonDisplayFormattingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FileComparisonService Build() => new(
        new Fubar.Diff.Infrastructure.Files.TextFileReader(),
        new DiffPlexDiffEngine(),
        new DiffPlexInlineDiffEngine(),
        new TextLineNormalizer(),
        new JsonSemanticPass(new JsonAstParser()));

    private static async Task<(FileComparisonService Service, FileComparison Comparison)> Compare(
        string left,
        string right)
    {
        var service = Build();

        var comparison = await service.CompareTextAsync(
            left, right, new ComparisonOptions { Mode = ComparisonMode.Json }, "l.json", "r.json", Token);

        return (service, comparison);
    }

    [Fact]
    public async Task Nothing_is_reformatted_until_it_is_asked_for()
    {
        var (service, comparison) = await Compare("""{"a":1}""", """{"a":2}""");

        var display = service.FormatJsonForDisplay(comparison, false, false, JsonFormatOptions.Default);

        Assert.Equal(comparison.OriginalLeftText, display.LeftText);
        Assert.Equal(comparison.OriginalRightText, display.RightText);
        Assert.Same(comparison.OriginalSemanticChanges, display.Changes);
    }

    [Fact]
    public async Task One_side_can_be_reformatted_on_its_own()
    {
        // The case it exists for: a minified file next to a formatted one.
        var (service, comparison) = await Compare("""{"a":{"b":1}}""", "{\n  \"a\": { \"b\": 2 }\n}");

        var display = service.FormatJsonForDisplay(comparison, prettyLeft: true, prettyRight: false, JsonFormatOptions.Default);

        Assert.Contains("\n", display.LeftText, StringComparison.Ordinal);
        Assert.Equal(comparison.OriginalRightText, display.RightText);
    }

    [Fact]
    public async Task The_highlight_spans_follow_the_text_they_are_shown_over()
    {
        // The whole reason the two are produced together. On one line the change is at line 1; once
        // the document is laid out it is further down, and a span still saying line 1 would highlight
        // the opening brace.
        var (service, comparison) = await Compare("""{"a":{"b":1}}""", """{"a":{"b":2}}""");

        var before = comparison.OriginalSemanticChanges.Single();
        Assert.Equal(1, before.LeftSpan.StartLine);

        var display = service.FormatJsonForDisplay(comparison, prettyLeft: true, prettyRight: true, JsonFormatOptions.Default);

        var after = Assert.Single(display.Changes);
        Assert.True(after.LeftSpan.StartLine > 1, "the change moved down when the document was laid out");
    }

    [Fact]
    public async Task Reformatting_does_not_change_what_the_comparison_FOUND()
    {
        var (service, comparison) = await Compare("""{"a":1,"b":{"c":2}}""", """{"a":9,"b":{"c":2}}""");

        var display = service.FormatJsonForDisplay(comparison, true, true, JsonFormatOptions.Default);

        Assert.Equal(comparison.OriginalSemanticChanges.Count, display.Changes.Count);
        Assert.Equal(
            comparison.OriginalSemanticChanges.Select(c => c.Path.ToString()),
            display.Changes.Select(c => c.Path.ToString()));
    }

    [Fact]
    public async Task The_format_options_are_honoured()
    {
        var (service, comparison) = await Compare("""{"a":{"b":1}}""", """{"a":{"b":1}}""");

        var tabbed = service.FormatJsonForDisplay(
            comparison, true, false, JsonFormatOptions.Default with { UseTabs = true });

        Assert.Contains("\n\t", tabbed.LeftText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_text_comparison_is_left_alone_even_if_asked()
    {
        // Not JSON, so there is nothing to lay out - and the Json view is not showing anyway.
        var service = Build();

        var comparison = await service.CompareTextAsync(
            "hello", "world", new ComparisonOptions { Mode = ComparisonMode.Text }, "l.txt", "r.txt", Token);

        var display = service.FormatJsonForDisplay(comparison, true, true, JsonFormatOptions.Default);

        Assert.Equal("hello", display.LeftText);
        Assert.Equal("world", display.RightText);
    }
}
