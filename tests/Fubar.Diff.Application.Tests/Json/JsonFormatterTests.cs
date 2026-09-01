using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Application.Tests.Json;

/// <summary>
/// Laying a parsed document back out for reading.
///
/// The property that matters most is that it does not EDIT anything on the way. A formatter that
/// re-derived values would turn 1.0 into 1 and 1e3 into 1000 while claiming only to have changed the
/// whitespace, which is a far worse thing for a diff tool to do than not being able to reformat.
/// </summary>
public class JsonFormatterTests
{
    private static string Format(string json, JsonFormatOptions? options = null)
    {
        Assert.True(new Fubar.Diff.Infrastructure.Json.JsonAstParser().TryParse(json, out var root, out var error), error?.Message);

        return JsonFormatter.Format(root!, options ?? JsonFormatOptions.Default);
    }

    [Fact]
    public void A_minified_document_gains_its_shape_back()
    {
        var formatted = Format("""{"a":{"b":1},"c":2}""");

        Assert.Equal("{\n  \"a\": { \"b\": 1 },\n  \"c\": 2\n}", formatted);
    }

    [Fact]
    public void Numbers_are_written_exactly_as_the_author_wrote_them()
    {
        // The one thing this must never do. 1.0 and 1 are the same value and not the same text, and a
        // diff tool that silently changes which one is in the file is not to be trusted with the rest.
        Assert.Contains("1.0", Format("""{"a":1.0}"""), StringComparison.Ordinal);
        Assert.Contains("1e3", Format("""{"a":1e3}"""), StringComparison.Ordinal);
        Assert.Contains("-0", Format("""{"a":-0}"""), StringComparison.Ordinal);
    }

    [Fact]
    public void Strings_keep_their_own_escaping()
    {
        Assert.Contains(@"é", Format("""{"a":"é"}"""), StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_of_only_scalars_stays_on_one_line()
    {
        // The setting that most changes how a real document reads: without it an array of ten small
        // objects becomes forty lines, most of them braces.
        Assert.Equal("""{ "id": 1, "name": "a" }""", Format("""{"id":1,"name":"a"}"""));
        Assert.Equal("[ 1, 2, 3 ]", Format("[1,2,3]"));
    }

    [Fact]
    public void Turning_that_off_expands_everything()
    {
        var options = JsonFormatOptions.Default with { InlineSimpleContainers = false };

        Assert.Equal("{\n  \"id\": 1\n}", Format("""{"id":1}""", options));
        Assert.Equal("[\n  1,\n  2\n]", Format("[1,2]", options));
    }

    [Fact]
    public void Anything_genuinely_nested_expands_regardless()
    {
        // Inlining is about removing boilerplate, not about hiding structure.
        Assert.Equal("{\n  \"a\": [ 1 ]\n}", Format("""{"a":[1]}"""));
    }

    [Fact]
    public void Empty_containers_have_a_form_of_their_own()
    {
        Assert.Equal("{}", Format("{}"));
        Assert.Equal("[]", Format("[]"));
        Assert.Equal("{\n  \"a\": {},\n  \"b\": []\n}", Format("""{"a":{},"b":{}}""".Replace("\"b\":{}", "\"b\":[]", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_indent_is_what_was_asked_for()
    {
        Assert.Equal(
            "{\n    \"a\": [ 1 ]\n}",
            Format("""{"a":[1]}""", JsonFormatOptions.Default with { IndentSize = 4 }));

        Assert.Equal(
            "{\n\t\"a\": [ 1 ]\n}",
            Format("""{"a":[1]}""", JsonFormatOptions.Default with { UseTabs = true }));
    }

    [Fact]
    public void Properties_can_be_sorted_for_reading()
    {
        // Off by default because it is a lie about what the file contains - but the right lie when the
        // two sides come from serializers that disagree about ordering.
        var options = JsonFormatOptions.Default with { SortProperties = true, InlineSimpleContainers = false };

        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2,\n  \"c\": 3\n}", Format("""{"c":3,"a":1,"b":2}""", options));
    }

    [Fact]
    public void Source_order_is_kept_by_default()
    {
        var options = JsonFormatOptions.Default with { InlineSimpleContainers = false };

        Assert.Equal("{\n  \"c\": 3,\n  \"a\": 1\n}", Format("""{"c":3,"a":1}""", options));
    }

    [Fact]
    public void The_space_after_the_colon_is_optional()
    {
        Assert.Equal("""{ "a":1 }""", Format("""{"a":1}""", JsonFormatOptions.Default with { SpaceAfterColon = false }));
    }

    [Fact]
    public void A_key_that_needs_escaping_gets_it_back()
    {
        // Names are held unescaped on the AST - every comparison wants them that way - so unlike a
        // scalar they cannot be written back verbatim.
        Assert.Contains("\\\"quoted\\\"", Format("""{"\"quoted\"":1}"""), StringComparison.Ordinal);
        Assert.Contains(@"a\nb", Format("""{"a\nb":1}"""), StringComparison.Ordinal);
    }

    [Fact]
    public void A_formatted_document_still_parses_to_the_same_thing()
    {
        // The round trip that makes the whole feature safe: reformatting for display must not change
        // what the document says.
        const string Original = """{"users":[{"id":1,"tags":["a","b"]},{"id":2,"tags":[]}],"n":1.50,"ok":true,"x":null}""";

        var formatted = Format(Original);

        Assert.Equal(Format(Original), Format(formatted));
    }

    [Fact]
    public void A_bare_scalar_document_is_itself()
    {
        Assert.Equal("42", Format("42"));
        Assert.Equal("\"hi\"", Format("\"hi\""));
        Assert.Equal("null", Format("null"));
    }
}
