using Fubar.Diff.Core.Json;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// The parser's two jobs: build the right tree, and record where every node came from. The source
/// spans matter as much as the values - they are what lets a semantic difference be highlighted in a
/// text editor, and a span that is off by a line points at the wrong code.
/// </summary>
public class JsonAstParserTests
{
    private readonly JsonAstParser _parser = new();

    private JsonAstObject ParseObject(string json) => Assert.IsType<JsonAstObject>(_parser.Parse(json));

    private JsonAstArray ParseArray(string json) => Assert.IsType<JsonAstArray>(_parser.Parse(json));

    // ---- Values ---------------------------------------------------------------------------------

    [Fact]
    public void Parses_an_empty_object() =>
        Assert.Empty(ParseObject("{}").Properties);

    [Fact]
    public void Parses_an_empty_array() =>
        Assert.Empty(ParseArray("[]").Items);

    [Fact]
    public void Parses_properties_in_source_order()
    {
        var obj = ParseObject("""{"b": 1, "a": 2}""");

        Assert.Equal(["b", "a"], obj.Properties.Select(p => p.Name));
    }

    [Theory]
    [InlineData("\"hello\"", JsonAstKind.String)]
    [InlineData("42", JsonAstKind.Number)]
    [InlineData("-1.5e10", JsonAstKind.Number)]
    [InlineData("true", JsonAstKind.Boolean)]
    [InlineData("false", JsonAstKind.Boolean)]
    [InlineData("null", JsonAstKind.Null)]
    public void Parses_each_scalar_kind(string json, JsonAstKind expected) =>
        Assert.Equal(expected, _parser.Parse(json).Kind);

    [Fact]
    public void Keeps_a_numbers_raw_text()
    {
        // 1.0 and 1 are the same value but not the same text, and a diff of a text file should say so.
        var scalar = Assert.IsType<JsonAstScalar>(_parser.Parse("1.00"));

        Assert.Equal("1.00", scalar.RawText);
    }

    [Fact]
    public void Unescapes_string_values()
    {
        var scalar = Assert.IsType<JsonAstScalar>(_parser.Parse(@"""a\tb\nc\""d\\e"""));

        Assert.Equal("a\tb\nc\"d\\e", scalar.Value);
    }

    [Fact]
    public void Resolves_unicode_escapes()
    {
        var scalar = Assert.IsType<JsonAstScalar>(_parser.Parse(@"""åäö"""));

        Assert.Equal("åäö", scalar.Value);
    }

    [Fact]
    public void Parses_nested_structures()
    {
        var root = ParseObject("""{"a": {"b": [1, {"c": null}]}}""");

        var a = Assert.IsType<JsonAstObject>(root.Find("a")!.Value);
        var b = Assert.IsType<JsonAstArray>(a.Find("b")!.Value);

        Assert.Equal(2, b.Items.Count);
        Assert.Equal(JsonAstKind.Object, b.Items[1].Kind);
    }

    [Fact]
    public void Find_returns_null_for_an_absent_property() =>
        Assert.Null(ParseObject("""{"a": 1}""").Find("b"));

    // ---- Source spans ---------------------------------------------------------------------------

    [Fact]
    public void A_scalar_span_points_at_the_value()
    {
        //             1234567890123
        var obj = ParseObject("""{"a": 42}""");
        var span = obj.Find("a")!.Value.Span;

        Assert.Equal(1, span.StartLine);
        Assert.Equal(7, span.StartColumn);   // 1-based: '4' is the 7th character
        Assert.Equal(9, span.EndColumn);     // just past '2'
    }

    [Fact]
    public void A_property_name_has_its_own_span_separate_from_the_value()
    {
        var property = ParseObject("""{"key": "value"}""").Find("key")!;

        Assert.Equal(2, property.NameSpan.StartColumn);   // the opening quote of "key"
        Assert.Equal(9, property.Value.Span.StartColumn); // the opening quote of "value"
    }

    [Fact]
    public void Spans_track_line_numbers_across_a_multi_line_document()
    {
        var obj = ParseObject("{\n  \"a\": 1,\n  \"b\": 2\n}");

        Assert.Equal(2, obj.Find("a")!.Value.Span.StartLine);
        Assert.Equal(3, obj.Find("b")!.Value.Span.StartLine);
    }

    [Fact]
    public void A_container_span_covers_its_whole_extent()
    {
        var root = ParseObject("{\n  \"a\": {\n    \"b\": 1\n  }\n}");
        var inner = root.Find("a")!.Value;

        Assert.Equal(2, inner.Span.StartLine);
        Assert.Equal(4, inner.Span.EndLine);   // the closing brace
    }

    [Fact]
    public void A_multi_line_string_does_not_shift_later_line_numbers()
    {
        // The escape is two characters in the source, not a real newline - counting it as one would
        // put every following span a line out.
        var obj = ParseObject("{\n  \"a\": \"x\\ny\",\n  \"b\": 1\n}");

        Assert.Equal(3, obj.Find("b")!.Value.Span.StartLine);
    }

    [Fact]
    public void Windows_line_endings_do_not_double_count_lines()
    {
        var obj = ParseObject("{\r\n  \"a\": 1,\r\n  \"b\": 2\r\n}");

        Assert.Equal(3, obj.Find("b")!.Value.Span.StartLine);
    }

    // ---- Tolerance and errors -------------------------------------------------------------------

    [Fact]
    public void Tolerates_a_trailing_comma_in_an_object()
    {
        // Invalid JSON, but a common hand-editing slip - refusing to diff the file over it would be
        // unhelpful.
        Assert.Single(ParseObject("""{"a": 1,}""").Properties);
    }

    [Fact]
    public void Tolerates_a_trailing_comma_in_an_array() =>
        Assert.Equal(2, ParseArray("[1, 2,]").Items.Count);

    [Theory]
    [InlineData("", "expected a value")]
    [InlineData("{", "expected '}'")]
    [InlineData("[", "expected ']'")]
    [InlineData("{\"a\" 1}", "expected ':'")]
    [InlineData("{a: 1}", "property name")]
    [InlineData("\"unterminated", "unterminated string")]
    [InlineData("{} extra", "trailing content")]
    [InlineData("[1 2]", "expected ',' or ']'")]
    public void Reports_a_readable_reason_for_malformed_input(string json, string expectedFragment)
    {
        var ex = Assert.Throws<JsonParseException>(() => _parser.Parse(json));

        Assert.Contains(expectedFragment, ex.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_error_carries_a_position()
    {
        var ex = Assert.Throws<JsonParseException>(() => _parser.Parse("{\n  \"a\" 1\n}"));

        Assert.Equal(2, ex.Span.StartLine);
        Assert.True(ex.Span.IsKnown);
    }

    [Fact]
    public void TryParse_reports_failure_instead_of_throwing()
    {
        Assert.False(_parser.TryParse("not json", out var node, out var error));
        Assert.Null(node);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_succeeds_on_valid_input()
    {
        Assert.True(_parser.TryParse("""{"a": 1}""", out var node, out var error));
        Assert.NotNull(node);
        Assert.Null(error);
    }

    // ---- Robustness -----------------------------------------------------------------------------

    [Fact]
    public void Deep_nesting_fails_cleanly_rather_than_overflowing_the_stack()
    {
        // The reason the parser is iterative. A recursive one would die with a StackOverflowException,
        // which cannot be caught and takes the whole process down - and adversarial input is in scope
        // per SECURITY.md.
        var json = new string('[', 5000) + new string(']', 5000);

        var ex = Assert.Throws<JsonParseException>(() => _parser.Parse(json));

        Assert.Contains("nesting", ex.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nesting_just_inside_the_limit_still_parses()
    {
        var depth = JsonAstParser.MaxDepth - 1;
        var json = new string('[', depth) + new string(']', depth);

        Assert.Equal(JsonAstKind.Array, _parser.Parse(json).Kind);
    }

    [Fact]
    public void Handles_a_wide_document_without_difficulty()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 20_000)) + "]";

        Assert.Equal(20_000, ParseArray(json).Items.Count);
    }
}
