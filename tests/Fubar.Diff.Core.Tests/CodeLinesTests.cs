using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Languages;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// What a line is MATCHED on once the code rules are in play, and which lines carry nothing the reader
/// asked to see. Everything here is about comparison keys - none of it is ever displayed.
/// </summary>
public class CodeLinesTests
{
    private static readonly CodeComparisonOptions Comments = new() { IgnoreComments = true };
    private static readonly CodeComparisonOptions Blanks = new() { IgnoreBlankLines = true };

    private static CodeLines Analyze(
        string[] lines,
        CodeComparisonOptions options,
        SourceLanguage language = SourceLanguage.CSharp) =>
        CodeLines.Analyze(lines, language, options)
        ?? throw new InvalidOperationException("expected the analysis to run for these options");

    [Fact]
    public void Nothing_is_scanned_when_no_rule_is_on()
    {
        // The fast path that keeps an ordinary comparison free of a document-length scan it would
        // never consult.
        Assert.Null(CodeLines.Analyze(["a"], SourceLanguage.CSharp, CodeComparisonOptions.Default));
    }

    [Fact]
    public void Nothing_is_scanned_for_an_unknown_language()
    {
        Assert.Null(CodeLines.Analyze(["a"], SourceLanguage.None, Comments));
    }

    [Fact]
    public void A_trailing_comment_is_removed_along_with_the_space_before_it()
    {
        // The rule that makes this work at all: the key has to come out EXACTLY equal to the same line
        // written without the comment, dangling space included, or nothing matches.
        var analysis = Analyze(["foo(); // note", "foo();"], Comments);

        Assert.Equal("foo();", analysis.ComparisonLines[0]);
        Assert.Equal(analysis.ComparisonLines[1], analysis.ComparisonLines[0]);
    }

    [Fact]
    public void An_interior_comment_leaves_one_space_between_the_code_around_it()
    {
        var analysis = Analyze(["f(a /* x */, b)", "f(a, b)"], Comments);

        Assert.Equal("f(a, b)", analysis.ComparisonLines[0]);
    }

    [Fact]
    public void Code_on_a_commented_line_still_compares()
    {
        // "Ignore comments" must not become "ignore lines that have comments on them".
        var analysis = Analyze(["foo(); // note", "bar(); // note"], Comments);

        Assert.NotEqual(analysis.ComparisonLines[1], analysis.ComparisonLines[0]);
    }

    [Fact]
    public void A_comment_only_line_is_ignorable_when_comments_are()
    {
        var analysis = Analyze(["    // just a note", "foo();"], Comments);

        Assert.True(analysis.IsIgnorable(1));
        Assert.False(analysis.IsIgnorable(2));
    }

    [Fact]
    public void A_line_inside_a_block_comment_is_ignorable_too()
    {
        // The case a per-line scanner gets wrong: on its own, the middle line reads as code.
        var analysis = Analyze(["/* one", "two", "three */", "foo();"], Comments);

        Assert.True(analysis.IsIgnorable(2));
        Assert.False(analysis.IsIgnorable(4));
    }

    [Fact]
    public void A_blank_line_is_not_ignorable_unless_blank_lines_are()
    {
        Assert.False(Analyze(["", "foo();"], Comments).IsIgnorable(1));
        Assert.True(Analyze(["", "foo();"], Blanks).IsIgnorable(1));
    }

    [Fact]
    public void A_whitespace_only_line_counts_as_blank()
    {
        Assert.True(Analyze(["   \t ", "foo();"], Blanks).IsIgnorable(1));
    }

    [Fact]
    public void A_comment_only_line_is_not_ignorable_when_only_blank_lines_are()
    {
        Assert.False(Analyze(["// note"], Blanks).IsIgnorable(1));
    }

    [Fact]
    public void Comparison_lines_are_untouched_when_only_blank_lines_are_ignored()
    {
        // Stripping is what IgnoreComments buys; IgnoreBlankLines must not quietly do it too.
        var analysis = Analyze(["foo(); // note"], Blanks);

        Assert.Equal("foo(); // note", analysis.ComparisonLines[0]);
    }

    [Fact]
    public void A_line_number_outside_the_document_is_not_ignorable()
    {
        // Called with numbers taken off diff rows, which can outlive the analysis they were built
        // against for a frame while a new comparison is applied.
        var analysis = Analyze(["// note"], Comments);

        Assert.False(analysis.IsIgnorable(0));
        Assert.False(analysis.IsIgnorable(2));
    }

    [Fact]
    public void A_comment_marker_inside_a_string_is_left_alone()
    {
        var analysis = Analyze(["var url = \"http://x\"; // real"], Comments);

        Assert.Equal("var url = \"http://x\";", analysis.ComparisonLines[0]);
    }
}
