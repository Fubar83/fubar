using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Languages;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Infrastructure.Comparison;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// The code rules end to end, through the REAL engine and normalizer rather than the fakes the
/// orchestration tests use. What is being checked here is the answer a user gets, not the order the
/// service calls things in.
/// </summary>
public class CodeComparisonTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static FileComparisonService Build() => new(
        new Infrastructure.Files.TextFileReader(),
        new DiffPlexDiffEngine(),
        new DiffPlexInlineDiffEngine(),
        new TextLineNormalizer(),
        new JsonSemanticPass(new JsonAstParser()));

    /// <summary>
    /// Compares two in-memory sources. The LABELS carry the file extension, which is how the service
    /// works out the language - see LanguageDetector.
    /// </summary>
    private static Task<FileComparison> Compare(
        string left,
        string right,
        CodeComparisonOptions code,
        string extension = ".cs") =>
        Build().CompareTextAsync(
            left,
            right,
            new ComparisonOptions { Code = code },
            "left" + extension,
            "right" + extension,
            Token);

    private static readonly CodeComparisonOptions Comments = new() { IgnoreComments = true };
    private static readonly CodeComparisonOptions Blanks = new() { IgnoreBlankLines = true };

    [Fact]
    public async Task The_language_is_detected_from_the_file_names()
    {
        var comparison = await Compare("var a = 1;", "var a = 2;", CodeComparisonOptions.Default);

        Assert.Equal(SourceLanguage.CSharp, comparison.Language);
    }

    [Fact]
    public async Task A_file_that_is_not_code_gets_no_language()
    {
        var comparison = await Compare("one", "two", CodeComparisonOptions.Default, ".txt");

        Assert.Equal(SourceLanguage.None, comparison.Language);
    }

    [Fact]
    public async Task A_changed_comment_is_a_difference_by_default()
    {
        var comparison = await Compare("foo(); // old", "foo(); // new", CodeComparisonOptions.Default);

        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task A_changed_comment_is_not_a_difference_when_comments_are_ignored()
    {
        var comparison = await Compare("foo(); // old", "foo(); // new", Comments);

        Assert.True(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task The_user_still_sees_their_own_comment()
    {
        // The invariant that outranks everything else here: stripping produces a comparison KEY, and
        // a key must never reach the screen.
        var comparison = await Compare("foo(); // old", "foo(); // new", Comments);

        Assert.Equal("foo(); // old", comparison.Result.Lines[0].LeftText);
        Assert.Equal("foo(); // new", comparison.Result.Lines[0].RightText);
    }

    [Fact]
    public async Task Code_on_a_commented_line_still_compares()
    {
        var comparison = await Compare("foo(); // note", "bar(); // note", Comments);

        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task An_added_comment_line_is_ignored_rather_than_counted()
    {
        var comparison = await Compare(
            "foo();",
            "// explain\nfoo();",
            Comments);

        Assert.True(comparison.Result.AreIdentical);
        Assert.Contains(comparison.Result.Lines, l => l.IsIgnored);
    }

    [Fact]
    public async Task An_added_code_line_is_still_counted()
    {
        var comparison = await Compare("foo();", "bar();\nfoo();", Comments);

        Assert.Single(comparison.Result.Hunks);
    }

    [Fact]
    public async Task A_multi_line_comment_is_ignored_across_all_its_lines()
    {
        // The case a per-line rule gets wrong: the middle lines look like prose, not like a comment.
        var comparison = await Compare(
            "foo();",
            "/*\n * explain\n */\nfoo();",
            Comments);

        Assert.True(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task A_url_in_a_string_is_not_mistaken_for_a_comment()
    {
        var comparison = await Compare(
            "var u = \"http://a\";",
            "var u = \"http://b\";",
            Comments);

        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task Added_blank_lines_are_ignored_when_asked()
    {
        var comparison = await Compare("a();\nb();", "a();\n\n\nb();", Blanks);

        Assert.True(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task Added_blank_lines_are_a_difference_by_default()
    {
        var comparison = await Compare("a();\nb();", "a();\n\n\nb();", CodeComparisonOptions.Default);

        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task The_code_rules_do_nothing_for_a_language_the_scanner_cannot_read()
    {
        // A hash-commented file: the options are on, but nothing here knows what "#" means, and the
        // comparison must not pretend otherwise.
        var comparison = await Compare("value # old", "value # new", Comments, ".txt");

        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task Typescript_comments_are_understood_too()
    {
        var comparison = await Compare(
            "const a = 1; // old",
            "const a = 1; // new",
            Comments,
            ".ts");

        Assert.True(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task A_template_literal_spanning_lines_does_not_swallow_the_code_after_it()
    {
        var left = "const q = `a\nb`;\nfoo(); // x";
        var right = "const q = `a\nb`;\nbar(); // x";

        var comparison = await Compare(left, right, Comments, ".ts");

        Assert.False(comparison.Result.AreIdentical);
    }

    [Fact]
    public async Task A_moved_method_reads_as_the_whole_method()
    {
        // End to end through the slider: the removed run must be a method, not a closing brace
        // followed by the start of the next one.
        var left = "class C {\n    void A() {\n        a();\n    }\n    void B() {\n        b();\n    }\n}";
        var right = "class C {\n    void B() {\n        b();\n    }\n    void A() {\n        a();\n    }\n}";

        var comparison = await Compare(left, right, CodeComparisonOptions.Default);

        var deleted = comparison.Result.Lines
            .Where(l => l.Kind == ChangeKind.Deleted)
            .Select(l => l.LeftText)
            .ToList();

        Assert.Equal(["    void B() {", "        b();", "    }"], deleted);
    }

    [Fact]
    public async Task An_operator_change_highlights_the_whole_operator()
    {
        var comparison = await Compare("if (a == b) { }", "if (a === b) { }", CodeComparisonOptions.Default, ".ts");

        var row = Assert.Single(comparison.Result.Lines);
        var highlighted = row.RightSpans.Select(s => row.RightText!.Substring(s.Start, s.Length));

        Assert.Contains("===", highlighted);
    }
}
