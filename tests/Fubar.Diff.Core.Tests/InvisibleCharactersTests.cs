using Fubar.Diff.Core.Rendering;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The set is deliberately narrow: everything in it is zero-width or renders exactly like a space, so
/// revealing it is never noise. These pin both halves of that - what is caught, and what is not.
/// </summary>
public class InvisibleCharactersTests
{
    [Theory]
    [InlineData('\u00A0', "NBSP")]
    [InlineData('\u200B', "ZWSP")]
    [InlineData('\u200D', "ZWJ")]
    [InlineData('\uFEFF', "BOM")]
    [InlineData('\u00AD', "SHY")]
    [InlineData('\u202E', "RLO")]
    [InlineData('\u3000', "IDSP")]
    public void Invisible_and_space_like_characters_get_a_marker(char c, string expected) =>
        Assert.Equal(expected, InvisibleCharacters.MarkerFor(c));

    /// <summary>
    /// Ordinary text must stay unmarked, including characters that are merely non-ASCII. Accented
    /// letters, CJK and emoji are visible and legitimate - marking them would make the feature useless
    /// on most of the world's text.
    /// </summary>
    [Theory]
    [InlineData('a')]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('é')]
    [InlineData('中')]
    [InlineData('“')]
    [InlineData('—')]
    public void Ordinary_characters_get_no_marker(char c) =>
        Assert.Null(InvisibleCharacters.MarkerFor(c));

    [Fact]
    public void The_general_space_range_is_covered()
    {
        // U+2000..U+200A are all space variants; every one should be revealed.
        for (var c = '\u2000'; c <= '\u200A'; c++)
        {
            Assert.NotNull(InvisibleCharacters.MarkerFor(c));
        }
    }

    [Fact]
    public void ContainsAny_finds_a_hidden_character_in_a_line() =>
        Assert.True(InvisibleCharacters.ContainsAny("hello\u00A0world"));

    [Fact]
    public void ContainsAny_is_false_for_plain_text() =>
        Assert.False(InvisibleCharacters.ContainsAny("hello world"));

    [Fact]
    public void ContainsAny_is_false_for_ordinary_unicode() =>
        Assert.False(InvisibleCharacters.ContainsAny("café 中文 🎉"));
}
