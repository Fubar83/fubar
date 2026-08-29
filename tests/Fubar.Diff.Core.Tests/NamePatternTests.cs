using Fubar.Diff.Core.Folders;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The exclusion matcher. Small, but it decides what a folder comparison never looks at, so a pattern
/// that matches more than the user meant silently hides real differences.
/// </summary>
public class NamePatternTests
{
    [Theory]
    [InlineData("bin", "bin", true)]
    [InlineData("bin", "obj", false)]
    [InlineData("a.dll", "*.dll", true)]
    [InlineData("a.dll", "*.exe", false)]
    [InlineData("anything", "*", true)]
    [InlineData("", "*", true)]
    [InlineData("abc", "a?c", true)]
    [InlineData("ac", "a?c", false)]
    [InlineData("abbbc", "a*c", true)]
    [InlineData("ac", "a*c", true)]
    [InlineData("node_modules", "node_*", true)]
    [InlineData("a.min.js", "*.min.*", true)]
    public void Patterns_match_what_they_look_like(string name, string pattern, bool expected) =>
        Assert.Equal(expected, NamePattern.Matches(name, pattern, ignoreCase: true));

    [Fact]
    public void A_dot_is_a_literal_dot_not_any_character()
    {
        // The reason this is not a regular expression. Under regex rules ".git" would also exclude
        // "agit", and nobody typing ".git" into an exclusion box means that.
        Assert.True(NamePattern.Matches(".git", ".git", ignoreCase: true));
        Assert.False(NamePattern.Matches("agit", ".git", ignoreCase: true));
    }

    [Fact]
    public void Case_is_ignored_or_not_as_asked()
    {
        Assert.True(NamePattern.Matches("BIN", "bin", ignoreCase: true));
        Assert.False(NamePattern.Matches("BIN", "bin", ignoreCase: false));
    }

    [Fact]
    public void An_empty_pattern_matches_nothing()
    {
        // Otherwise a stray blank line in an exclusion list would hide the entire tree.
        Assert.False(NamePattern.Matches("anything", string.Empty, ignoreCase: true));
    }

    [Fact]
    public void Any_of_several_patterns_can_match()
    {
        string[] patterns = ["bin", "obj", "*.dll"];

        Assert.True(NamePattern.MatchesAny("obj", patterns, ignoreCase: true));
        Assert.True(NamePattern.MatchesAny("thing.dll", patterns, ignoreCase: true));
        Assert.False(NamePattern.MatchesAny("src", patterns, ignoreCase: true));
    }

    [Fact]
    public void A_name_full_of_stars_does_not_take_exponential_time()
    {
        // The classic backtracking blow-up for a recursive matcher. This one keeps a single backtrack
        // point, so it is linear - and a pattern a user can type must not be able to hang the app.
        var name = new string('a', 500) + "b";
        var pattern = string.Concat(Enumerable.Repeat("a*", 40)) + "c";

        Assert.False(NamePattern.Matches(name, pattern, ignoreCase: true));
    }

    [Fact]
    public void Trailing_stars_can_match_nothing()
    {
        Assert.True(NamePattern.Matches("file", "file*", ignoreCase: true));
        Assert.True(NamePattern.Matches("file", "file***", ignoreCase: true));
    }
}
