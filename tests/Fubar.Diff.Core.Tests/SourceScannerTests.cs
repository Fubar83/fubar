using Fubar.Diff.Core.Languages;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The lexer's contract. Two things matter to everything downstream: the tokens TILE the line (their
/// lengths sum to it, in order, with no gaps), and a construct that spans lines is still recognised on
/// the lines after the one that opened it.
/// </summary>
public class SourceScannerTests
{
    /// <summary>
    /// The invariant every consumer relies on. Comment stripping rebuilds the line from its tokens and
    /// the inline differ computes character offsets from their lengths - either would silently corrupt
    /// its output if a single character went missing.
    /// </summary>
    private static void AssertTilesLine(string line, IReadOnlyList<SourceToken> tokens)
    {
        var expected = 0;

        foreach (var token in tokens)
        {
            Assert.Equal(expected, token.Start);
            Assert.True(token.Length > 0, "a zero-length token would make offsets ambiguous");
            expected = token.End;
        }

        Assert.Equal(line.Length, expected);
    }

    private static IReadOnlyList<SourceToken> Scan(string line, SourceLanguage language = SourceLanguage.CSharp)
    {
        var tokens = SourceScanner.ScanLine(line, language);
        AssertTilesLine(line, tokens);

        return tokens;
    }

    private static string TextOf(SourceToken token, string line) => token.TextIn(line);

    [Fact]
    public void A_line_comment_runs_to_the_end_of_the_line()
    {
        const string line = "foo(); // and the rest // of it";
        var tokens = Scan(line);

        var comment = Assert.Single(tokens, t => t.Kind == SourceTokenKind.Comment);
        Assert.Equal("// and the rest // of it", TextOf(comment, line));
    }

    [Fact]
    public void A_block_comment_closing_on_the_same_line_leaves_the_rest_as_code()
    {
        const string line = "f(a /* why */, b)";
        var tokens = Scan(line);

        var comment = Assert.Single(tokens, t => t.Kind == SourceTokenKind.Comment);
        Assert.Equal("/* why */", TextOf(comment, line));
        Assert.Contains(tokens, t => t.Kind == SourceTokenKind.Identifier && TextOf(t, line) == "b");
    }

    [Fact]
    public void A_block_comment_carries_across_lines_until_it_closes()
    {
        string[] lines = ["int a = 1; /* open", "still inside", "still */ int b = 2;"];

        var scanned = SourceScanner.Scan(lines, SourceLanguage.CSharp);

        // The middle line is entirely comment - which is the whole point: read on its own it looks
        // like two identifiers.
        var middle = Assert.Single(scanned[1].Tokens);
        Assert.Equal(SourceTokenKind.Comment, middle.Kind);

        // ...and the last line goes back to being code after the close.
        Assert.Contains(scanned[2].Tokens, t => t.Kind == SourceTokenKind.Identifier && TextOf(t, lines[2]) == "int");
    }

    [Fact]
    public void A_blank_line_inside_a_block_comment_does_not_close_it()
    {
        string[] lines = ["/* open", "", "still */"];

        var scanned = SourceScanner.Scan(lines, SourceLanguage.CSharp);

        Assert.Empty(scanned[1].Tokens);
        Assert.Equal(SourceTokenKind.Comment, scanned[2].Tokens[0].Kind);
    }

    [Fact]
    public void A_comment_marker_inside_a_string_is_not_a_comment()
    {
        const string line = "var url = \"https://example.com\";";
        var tokens = Scan(line);

        Assert.DoesNotContain(tokens, t => t.Kind == SourceTokenKind.Comment);
        Assert.Contains(tokens, t => t.Kind == SourceTokenKind.String && TextOf(t, line) == "\"https://example.com\"");
    }

    [Fact]
    public void An_escaped_quote_does_not_end_a_string()
    {
        const string line = "\"a\\\"b\" + c";
        var tokens = Scan(line);

        var text = Assert.Single(tokens, t => t.Kind == SourceTokenKind.String);
        Assert.Equal("\"a\\\"b\"", TextOf(text, line));
    }

    [Fact]
    public void An_empty_verbatim_string_closes_immediately()
    {
        // The doubled quote that escapes a quote inside a verbatim string is exactly the pair that
        // makes an EMPTY one ambiguous to a naive scanner - it reads @"" as an escape and swallows
        // the rest of the file.
        const string line = "var s = @\"\"; var t = 1;";
        var tokens = Scan(line);

        var text = Assert.Single(tokens, t => t.Kind == SourceTokenKind.String);
        Assert.Equal("@\"\"", TextOf(text, line));
    }

    [Fact]
    public void A_doubled_quote_inside_a_verbatim_string_is_an_escape()
    {
        const string line = "var s = @\"say \"\"hi\"\" now\";";
        var tokens = Scan(line);

        var text = Assert.Single(tokens, t => t.Kind == SourceTokenKind.String);
        Assert.Equal("@\"say \"\"hi\"\" now\"", TextOf(text, line));
    }

    [Fact]
    public void A_verbatim_string_carries_across_lines()
    {
        string[] lines = ["var sql = @\"SELECT *", "FROM t\";", "var x = 1;"];

        var scanned = SourceScanner.Scan(lines, SourceLanguage.CSharp);

        Assert.Equal(SourceTokenKind.String, scanned[1].Tokens[0].Kind);
        Assert.Contains(scanned[2].Tokens, t => t.Kind == SourceTokenKind.Identifier);
    }

    [Fact]
    public void A_raw_string_is_closed_only_by_a_long_enough_quote_run()
    {
        const string line = "var s = \"\"\"he said \"quoted\" there\"\"\";";
        var tokens = Scan(line);

        var text = Assert.Single(tokens, t => t.Kind == SourceTokenKind.String);
        Assert.Equal("\"\"\"he said \"quoted\" there\"\"\"", TextOf(text, line));
    }

    [Fact]
    public void An_at_prefixed_identifier_is_not_a_string()
    {
        const string line = "var @class = 1;";
        var tokens = Scan(line);

        Assert.DoesNotContain(tokens, t => t.Kind == SourceTokenKind.String);
        Assert.Contains(tokens, t => t.Kind == SourceTokenKind.Identifier && TextOf(t, line) == "@class");
    }

    [Fact]
    public void A_template_literal_carries_across_lines_in_javascript()
    {
        string[] lines = ["const q = `line one", "line two`;", "const x = 1;"];

        var scanned = SourceScanner.Scan(lines, SourceLanguage.JavaScript);

        Assert.Equal(SourceTokenKind.String, scanned[1].Tokens[0].Kind);
        Assert.Contains(scanned[2].Tokens, t => t.Kind == SourceTokenKind.Identifier);
    }

    [Fact]
    public void A_backtick_is_not_a_string_in_csharp()
    {
        const string line = "var a = 1; // `not a string`";
        var tokens = Scan(line);

        Assert.DoesNotContain(tokens, t => t.Kind == SourceTokenKind.String);
    }

    [Theory]
    [InlineData("=>")]
    [InlineData("===")]
    [InlineData("!==")]
    [InlineData("??=")]
    [InlineData("<=")]
    [InlineData("&&")]
    [InlineData("...")]
    public void A_multi_character_operator_is_one_token(string op)
    {
        var line = $"a {op} b";
        var tokens = Scan(line, SourceLanguage.TypeScript);

        Assert.Contains(tokens, t => t.Kind == SourceTokenKind.Operator && TextOf(t, line) == op);
    }

    [Fact]
    public void The_longest_operator_wins()
    {
        // === must not be scanned as == followed by =, which is what makes changing == to === show up
        // as a changed operator rather than as one stray character.
        const string line = "a === b";
        var tokens = Scan(line, SourceLanguage.JavaScript);

        Assert.Single(tokens, t => t.Kind == SourceTokenKind.Operator);
    }

    [Theory]
    [InlineData("0xFF")]
    [InlineData("1_000")]
    [InlineData("1.5e-3")]
    [InlineData("42UL")]
    public void A_numeric_literal_is_one_token(string number)
    {
        var line = $"x = {number};";
        var tokens = Scan(line);

        Assert.Contains(tokens, t => t.Kind == SourceTokenKind.Number && TextOf(t, line) == number);
    }

    [Fact]
    public void A_member_call_on_a_number_is_not_swallowed_into_it()
    {
        const string line = "1.ToString()";
        var tokens = Scan(line);

        Assert.Equal("1", TextOf(tokens[0], line));
        Assert.Equal(SourceTokenKind.Number, tokens[0].Kind);
        Assert.Contains(tokens, t => t.Kind == SourceTokenKind.Identifier && TextOf(t, line) == "ToString");
    }

    [Fact]
    public void An_unterminated_quote_does_not_run_into_the_next_line()
    {
        // The opposite failure to the verbatim one: an ordinary literal cannot span lines, so a stray
        // quote must not turn the remainder of the file into one string.
        string[] lines = ["var s = \"oops;", "var t = 1;"];

        var scanned = SourceScanner.Scan(lines, SourceLanguage.CSharp);

        Assert.Contains(scanned[1].Tokens, t => t.Kind == SourceTokenKind.Identifier);
    }

    [Fact]
    public void An_unknown_language_yields_one_token_for_the_line()
    {
        const string line = "anything at all";
        var tokens = Scan(line, SourceLanguage.None);

        var only = Assert.Single(tokens);
        Assert.Equal(line.Length, only.Length);
    }
}
