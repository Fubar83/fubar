using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Languages;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The languages beyond C# and JS/TS, tested at the points where each one actually DIFFERS.
///
/// There is no value in re-testing that <c>//</c> comments a line in five languages; the risk is in
/// the handful of forms that behave unlike the C family, and a wrong answer there is not a cosmetic
/// one - "ignore comments" deletes text from a comparison key, so mistaking a string for a comment
/// silently drops real content out of the diff.
/// </summary>
public class SourceScannerLanguageTests
{
    private static IReadOnlyList<SourceToken> Scan(string line, SourceLanguage language)
    {
        var tokens = SourceScanner.ScanLine(line, language);

        var expected = 0;
        foreach (var token in tokens)
        {
            Assert.Equal(expected, token.Start);
            expected = token.End;
        }

        Assert.Equal(line.Length, expected);

        return tokens;
    }

    private static string TextOf(SourceToken token, string line) => token.TextIn(line);

    private static string Stripped(string[] lines, SourceLanguage language)
    {
        var analysis = CodeLines.Analyze(lines, language, new CodeComparisonOptions { IgnoreComments = true });

        return string.Join('\n', analysis!.ComparisonLines);
    }

    // ---- Java -----------------------------------------------------------------------------------

    [Fact]
    public void A_java_text_block_spans_lines()
    {
        string[] lines = ["String s = \"\"\"", "   not code;", "   \"\"\";", "int x = 1;"];

        var scanned = SourceScanner.Scan(lines, SourceLanguage.Java);

        Assert.Equal(SourceTokenKind.String, scanned[1].Tokens[0].Kind);
        Assert.Contains(scanned[3].Tokens, t => t.Kind == SourceTokenKind.Identifier);
    }

    [Fact]
    public void Java_comments_are_stripped_like_any_c_family_language()
    {
        Assert.Equal("foo();", Stripped(["foo(); // note"], SourceLanguage.Java));
    }

    // ---- Go -------------------------------------------------------------------------------------

    [Fact]
    public void A_go_raw_string_treats_a_backslash_as_content()
    {
        // The trap: with JavaScript's escape rules, the backslash before the closing backtick would
        // escape it, the literal would never close, and everything after it would be "string".
        const string line = "path := `C:\\temp\\` + name";
        var tokens = Scan(line, SourceLanguage.Go);

        var text = Assert.Single(tokens, t => t.Kind == SourceTokenKind.String);
        Assert.Equal("`C:\\temp\\`", TextOf(text, line));
        Assert.Contains(tokens, t => t.Kind == SourceTokenKind.Identifier && TextOf(t, line) == "name");
    }

    [Fact]
    public void A_go_raw_string_spans_lines()
    {
        string[] lines = ["const q = `SELECT", "FROM t`", "x := 1"];

        var scanned = SourceScanner.Scan(lines, SourceLanguage.Go);

        Assert.Equal(SourceTokenKind.String, scanned[1].Tokens[0].Kind);
        Assert.Contains(scanned[2].Tokens, t => t.Kind == SourceTokenKind.Identifier);
    }

    [Fact]
    public void A_javascript_template_still_honours_escapes()
    {
        // The other half of the same rule: Go and JS must not be given each other's behaviour.
        const string line = "const a = `x\\`y` + z";
        var tokens = Scan(line, SourceLanguage.JavaScript);

        var text = Assert.Single(tokens, t => t.Kind == SourceTokenKind.String);
        Assert.Equal("`x\\`y`", TextOf(text, line));
    }

    // ---- Python ---------------------------------------------------------------------------------

    [Fact]
    public void Python_comments_start_with_a_hash()
    {
        const string line = "value = 1  # explain";
        var tokens = Scan(line, SourceLanguage.Python);

        var comment = Assert.Single(tokens, t => t.Kind == SourceTokenKind.Comment);
        Assert.Equal("# explain", TextOf(comment, line));
    }

    [Fact]
    public void A_hash_inside_a_python_string_is_not_a_comment()
    {
        const string line = "colour = \"#ff0000\"  # red";
        var tokens = Scan(line, SourceLanguage.Python);

        var comment = Assert.Single(tokens, t => t.Kind == SourceTokenKind.Comment);
        Assert.Equal("# red", TextOf(comment, line));
        Assert.Equal("colour = \"#ff0000\"", Stripped([line], SourceLanguage.Python));
    }

    [Fact]
    public void Python_has_no_block_comments()
    {
        // /* */ is not a comment in Python, and treating it as one would swallow real code.
        const string line = "x = a /* b";
        var tokens = Scan(line, SourceLanguage.Python);

        Assert.DoesNotContain(tokens, t => t.Kind == SourceTokenKind.Comment);
    }

    [Theory]
    [InlineData("\"\"\"")]
    [InlineData("'''")]
    public void A_python_triple_quoted_string_spans_lines(string quotes)
    {
        string[] lines = [$"doc = {quotes}", "   still a string", $"   {quotes}", "x = 1"];

        var scanned = SourceScanner.Scan(lines, SourceLanguage.Python);

        Assert.Equal(SourceTokenKind.String, scanned[1].Tokens[0].Kind);
        Assert.Contains(scanned[3].Tokens, t => t.Kind == SourceTokenKind.Identifier);
    }

    [Fact]
    public void One_kind_of_python_triple_quote_does_not_close_the_other()
    {
        string[] lines = ["doc = '''", "\"\"\" not the end", "'''", "x = 1"];

        var scanned = SourceScanner.Scan(lines, SourceLanguage.Python);

        Assert.Equal(SourceTokenKind.String, scanned[1].Tokens[0].Kind);
        Assert.Contains(scanned[3].Tokens, t => t.Kind == SourceTokenKind.Identifier);
    }

    [Fact]
    public void A_python_docstring_is_a_string_not_a_comment()
    {
        // It is a real value that a program can read, so "ignore comments" must leave it alone -
        // treating it as a comment would drop it out of the comparison entirely.
        string[] lines = ["def f():", "    \"\"\"What f does.\"\"\"", "    return 1"];

        Assert.Equal(
            "def f():\n    \"\"\"What f does.\"\"\"\n    return 1",
            Stripped(lines, SourceLanguage.Python));
    }

    [Theory]
    [InlineData("f\"hello {name}\"")]
    [InlineData("r\"C:\\temp\"")]
    [InlineData("b'bytes'")]
    [InlineData("rb\"\"\"raw bytes\"\"\"")]
    public void A_python_string_prefix_belongs_to_the_literal(string literal)
    {
        var line = $"x = {literal}";
        var tokens = Scan(line, SourceLanguage.Python);

        var text = Assert.Single(tokens, t => t.Kind == SourceTokenKind.String);
        Assert.Equal(literal, TextOf(text, line));
    }

    [Fact]
    public void An_identifier_that_begins_with_string_prefix_letters_is_still_an_identifier()
    {
        // "buffer" starts with b, u, f, f - three distinct Python string prefixes. A prefix run only
        // counts when a quote actually follows it.
        const string line = "buffer = rub + fur";
        var tokens = Scan(line, SourceLanguage.Python);

        Assert.DoesNotContain(tokens, t => t.Kind == SourceTokenKind.String);
        Assert.Contains(tokens, t => t.Kind == SourceTokenKind.Identifier && TextOf(t, line) == "buffer");
        Assert.Contains(tokens, t => t.Kind == SourceTokenKind.Identifier && TextOf(t, line) == "rub");
    }

    // ---- C and C++ ------------------------------------------------------------------------------

    [Theory]
    [InlineData(SourceLanguage.C)]
    [InlineData(SourceLanguage.Cpp)]
    public void C_family_comments_and_strings_scan(SourceLanguage language)
    {
        const string line = "printf(\"a // b\"); /* note */";
        var tokens = Scan(line, language);

        var comment = Assert.Single(tokens, t => t.Kind == SourceTokenKind.Comment);
        Assert.Equal("/* note */", TextOf(comment, line));
        Assert.Contains(tokens, t => t.Kind == SourceTokenKind.String && TextOf(t, line) == "\"a // b\"");
    }

    [Fact]
    public void A_c_block_comment_carries_across_lines()
    {
        string[] lines = ["int a; /* open", "still inside", "*/ int b;"];

        var scanned = SourceScanner.Scan(lines, SourceLanguage.C);

        Assert.Equal(SourceTokenKind.Comment, Assert.Single(scanned[1].Tokens).Kind);
        Assert.Contains(scanned[2].Tokens, t => t.Kind == SourceTokenKind.Identifier);
    }

    // ---- Detection ------------------------------------------------------------------------------

    [Theory]
    [InlineData("Main.java", SourceLanguage.Java)]
    [InlineData("main.go", SourceLanguage.Go)]
    [InlineData("script.py", SourceLanguage.Python)]
    [InlineData("types.pyi", SourceLanguage.Python)]
    [InlineData("main.c", SourceLanguage.C)]
    [InlineData("header.h", SourceLanguage.C)]
    [InlineData("main.cpp", SourceLanguage.Cpp)]
    [InlineData("widget.hpp", SourceLanguage.Cpp)]
    [InlineData("lib.rs", SourceLanguage.None)]
    public void The_new_extensions_are_detected(string path, SourceLanguage expected) =>
        Assert.Equal(expected, LanguageDetector.FromPath(path));
}
