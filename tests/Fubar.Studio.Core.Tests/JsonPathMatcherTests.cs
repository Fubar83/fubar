using Fubar.Studio.Core.Json;

namespace Fubar.Studio.Core.Tests;

/// <summary>
/// Whether a rule's pattern names the path a difference happened at. The same subset
/// <c>JsonPathRewriter</c> applies on the write side, so a path written once means one thing.
/// </summary>
public class JsonPathMatcherTests
{
    [Theory]
    [InlineData("$.total", "$.total")]
    [InlineData("$.a.b", "$.a.b")]
    [InlineData("$.orders[*].total", "$.orders[3].total")]
    [InlineData("$.orders[0]", "$.orders[0]")]
    [InlineData("$..id", "$.a.b.id")]
    [InlineData("$..id", "$.id")]
    [InlineData("$..items[*].id", "$.a.items[2].id")]
    public void Matches(string pattern, string path) =>
        Assert.True(JsonPathMatcher.Matches(pattern, path));

    [Theory]
    [InlineData("$.total", "$.totals")]
    [InlineData("$.total", "$.a.total")]
    [InlineData("$.orders[0]", "$.orders[1]")]
    [InlineData("$.a.b", "$.a")]
    [InlineData("$.a", "$.a.b")]
    [InlineData("$..id", "$.a.identifier")]
    public void Does_not_match(string pattern, string path) =>
        Assert.False(JsonPathMatcher.Matches(pattern, path));

    /// <summary>A rule nobody can parse matches nothing rather than throwing - it must not take a run
    /// down, and <c>IsSupported</c> is how a caller warns about it instead.</summary>
    [Fact]
    public void An_unparseable_pattern_matches_nothing_and_says_it_is_unsupported()
    {
        Assert.False(JsonPathMatcher.Matches("orders[0]", "$.orders[0]"));
        Assert.False(JsonPathMatcher.Matches("$.a[?(@.x)]", "$.a"));
        Assert.False(JsonPathMatcher.IsSupported("$.a[?(@.x)]"));
        Assert.True(JsonPathMatcher.IsSupported("$..token"));
    }

    [Theory]
    [InlineData("$.orders[10]", "$.orders")]
    [InlineData("$.a.b", "$.a")]
    [InlineData("$.a", "$")]
    public void Parent_is_the_path_without_its_last_step(string path, string parent) =>
        Assert.Equal(parent, JsonPathMatcher.ParentOf(path));

    /// <summary>A key containing a dot arrives bracket-quoted, and the last dot in the string is then
    /// inside the key rather than before it - so the parent cannot be found by searching backwards.</summary>
    [Fact]
    public void A_quoted_key_containing_a_dot_does_not_confuse_the_parent()
    {
        Assert.Equal("$.headers", JsonPathMatcher.ParentOf("$.headers['content.type']"));
        Assert.True(JsonPathMatcher.Matches("$.headers['content.type']", "$.headers['content.type']"));
    }

    [Fact]
    public void The_root_has_no_parent() => Assert.Null(JsonPathMatcher.ParentOf("$"));
}
