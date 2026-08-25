using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The ignore-rule matcher. Its whole value is being predictable, so the cases here are the ones a
/// user would reasonably expect to work - and the ones that must NOT match, which is where an
/// over-eager pattern would silently hide real differences.
/// </summary>
public class JsonPathPatternTests
{
    private static JsonPathPattern Parse(string text)
    {
        Assert.True(JsonPathPattern.TryParse(text, out var pattern));
        return pattern!;
    }

    private static JsonPath Path(params object[] steps)
    {
        var path = JsonPath.Root;
        foreach (var step in steps)
        {
            path = step is int index ? path.Index(index) : path.Property((string)step);
        }

        return path;
    }

    [Fact]
    public void An_exact_path_matches_itself()
    {
        Assert.True(Parse("$.meta.requestId").Matches(Path("meta", "requestId")));
    }

    [Fact]
    public void An_exact_path_does_not_match_a_different_one()
    {
        var pattern = Parse("$.meta.requestId");

        Assert.False(pattern.Matches(Path("meta", "traceId")));
        Assert.False(pattern.Matches(Path("data", "requestId")));
    }

    /// <summary>The leading $ is optional, because people type the bare path.</summary>
    [Fact]
    public void The_dollar_prefix_is_optional()
    {
        Assert.True(Parse("meta.requestId").Matches(Path("meta", "requestId")));
    }

    /// <summary>Ignoring a container ignores what is in it - there is no other sensible reading.</summary>
    [Fact]
    public void A_rule_on_a_container_covers_its_contents()
    {
        var pattern = Parse("$.meta");

        Assert.True(pattern.Matches(Path("meta")));
        Assert.True(pattern.Matches(Path("meta", "requestId")));
        Assert.True(pattern.Matches(Path("meta", "trace", "id")));
    }

    [Fact]
    public void A_rule_does_not_leak_to_a_sibling()
    {
        Assert.False(Parse("$.meta").Matches(Path("metadata", "requestId")));
    }

    // ---- Array wildcard -------------------------------------------------------------------------

    [Fact]
    public void An_index_wildcard_matches_every_element()
    {
        var pattern = Parse("$.items[*].updatedAt");

        Assert.True(pattern.Matches(Path("items", 0, "updatedAt")));
        Assert.True(pattern.Matches(Path("items", 7, "updatedAt")));
    }

    [Fact]
    public void An_index_wildcard_still_pins_the_property()
    {
        Assert.False(Parse("$.items[*].updatedAt").Matches(Path("items", 0, "createdAt")));
    }

    [Fact]
    public void An_explicit_index_matches_only_that_element()
    {
        var pattern = Parse("$.items[2].id");

        Assert.True(pattern.Matches(Path("items", 2, "id")));
        Assert.False(pattern.Matches(Path("items", 3, "id")));
    }

    // ---- Recursive descent ----------------------------------------------------------------------

    [Fact]
    public void A_descendant_rule_matches_at_any_depth()
    {
        var pattern = Parse("$..timestamp");

        Assert.True(pattern.Matches(Path("timestamp")));
        Assert.True(pattern.Matches(Path("data", "timestamp")));
        Assert.True(pattern.Matches(Path("data", "items", 3, "meta", "timestamp")));
    }

    [Fact]
    public void A_descendant_rule_still_pins_the_name()
    {
        Assert.False(Parse("$..timestamp").Matches(Path("data", "createdAt")));
    }

    [Fact]
    public void A_descendant_rule_can_be_anchored_to_a_subtree()
    {
        var pattern = Parse("$.data..id");

        Assert.True(pattern.Matches(Path("data", "user", "id")));
        Assert.False(pattern.Matches(Path("meta", "user", "id")));
    }

    // ---- Parsing --------------------------------------------------------------------------------

    /// <summary>
    /// Rules live in a hand-editable request file, so a bad one is rejected rather than throwing -
    /// one typo must not stop a response being compared.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("$")]
    [InlineData("$.items[")]
    [InlineData("$.items[abc]")]
    [InlineData("$.items[-1]")]
    public void A_malformed_pattern_is_rejected(string? text)
    {
        Assert.False(JsonPathPattern.TryParse(text, out var pattern));
        Assert.Null(pattern);
    }

    /// <summary>"$" alone would ignore the whole document and report no differences at all, ever.</summary>
    [Fact]
    public void The_root_alone_is_not_a_usable_rule()
    {
        Assert.False(JsonPathPattern.TryParse("$", out _));
    }

    [Fact]
    public void A_parsed_pattern_normalizes_back_to_canonical_form()
    {
        Assert.Equal("$.items[*].updatedAt", Parse("items[*].updatedAt").Text);
    }

    /// <summary>Nesting depth comes from the document, so matching must not recurse over it.</summary>
    [Fact]
    public void A_deeply_nested_path_does_not_overflow_the_stack()
    {
        var path = JsonPath.Root;
        for (var i = 0; i < 20_000; i++)
        {
            path = path.Property("a");
        }

        Assert.False(Parse("$.b").Matches(path));
    }
}
