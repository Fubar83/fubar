using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// Reading YAML into the same tree the JSON parser produces.
///
/// That sameness is the entire design: everything downstream - the differ, ignore rules, array
/// identity keys, the change tree, the spans that highlight a change in the text - works on
/// <see cref="JsonAstNode"/> and does not care where it came from. So what is worth testing here is
/// the mapping itself, and above all the two places where YAML has an opinion JSON does not: what
/// counts as a number, and where each node actually sits in the file.
/// </summary>
public class YamlAstParserTests
{
    private static JsonAstNode Parse(string yaml)
    {
        Assert.True(new YamlAstParser().TryParse(yaml, out var node, out var error), error?.Message);

        return node!;
    }

    private static JsonAstScalar Scalar(JsonAstNode node, string name) =>
        Assert.IsType<JsonAstScalar>(Assert.IsType<JsonAstObject>(node).Find(name)!.Value);

    [Fact]
    public void A_mapping_becomes_an_object()
    {
        var root = Assert.IsType<JsonAstObject>(Parse("name: widget\nversion: 2\n"));

        Assert.Equal(2, root.Properties.Count);
        Assert.Equal("widget", Scalar(root, "name").Value);
    }

    [Fact]
    public void A_sequence_becomes_an_array()
    {
        var root = Assert.IsType<JsonAstObject>(Parse("tags:\n  - a\n  - b\n"));
        var tags = Assert.IsType<JsonAstArray>(root.Find("tags")!.Value);

        Assert.Equal(2, tags.Items.Count);
    }

    [Fact]
    public void Nesting_survives()
    {
        var root = Parse("""
            metadata:
              labels:
                app: widget
            """);

        var metadata = Assert.IsType<JsonAstObject>(Assert.IsType<JsonAstObject>(root).Find("metadata")!.Value);
        var labels = Assert.IsType<JsonAstObject>(metadata.Find("labels")!.Value);

        Assert.Equal("widget", Scalar(labels, "app").Value);
    }

    [Theory]
    [InlineData("8080", JsonAstKind.Number)]
    [InlineData("1.5", JsonAstKind.Number)]
    [InlineData("true", JsonAstKind.Boolean)]
    [InlineData("false", JsonAstKind.Boolean)]
    [InlineData("null", JsonAstKind.Null)]
    [InlineData("~", JsonAstKind.Null)]
    [InlineData("widget", JsonAstKind.String)]
    public void A_plain_scalar_gets_the_type_the_core_schema_gives_it(string text, JsonAstKind expected) =>
        Assert.Equal(expected, Scalar(Parse($"value: {text}"), "value").Kind);

    [Fact]
    public void A_quoted_number_is_a_string()
    {
        // port: 8080 and port: "8080" are a number and a string. A diff calling them equal would hide
        // the change most likely to break something.
        Assert.Equal(JsonAstKind.String, Scalar(Parse("port: \"8080\""), "port").Kind);
        Assert.Equal(JsonAstKind.Number, Scalar(Parse("port: 8080"), "port").Kind);
    }

    [Fact]
    public void The_norway_problem_is_not_reproduced()
    {
        // YAML 1.1 reads NO as false, which is why country: NO became a boolean in a generation of
        // tools. A diff tool inventing that reading would report a change of type nobody wrote.
        Assert.Equal(JsonAstKind.String, Scalar(Parse("country: NO"), "country").Kind);
        Assert.Equal(JsonAstKind.String, Scalar(Parse("enabled: yes"), "enabled").Kind);
    }

    [Fact]
    public void Every_node_knows_where_it_came_from()
    {
        // Without this a structural difference could be found and not shown: the span is what puts a
        // highlight on the right line of the text the user is looking at.
        var root = Parse("name: widget\nversion: 2\n");
        var version = Scalar(root, "version");

        Assert.Equal(2, version.Span.StartLine);
        Assert.True(version.Span.IsKnown);
    }

    [Fact]
    public void A_property_knows_where_its_KEY_is_too()
    {
        // What lets an added or removed field highlight its name as well as its value, exactly as in
        // JSON.
        var root = Assert.IsType<JsonAstObject>(Parse("name: widget\nversion: 2\n"));
        var property = root.Find("version")!;

        Assert.Equal(2, property.NameSpan.StartLine);
        Assert.Equal(1, property.NameSpan.StartColumn);
    }

    [Fact]
    public void Several_documents_in_one_file_become_an_array_of_them()
    {
        // A Kubernetes manifest is routinely a Deployment, a Service and a ConfigMap separated by
        // ---, and comparing only the first would quietly ignore most of the file.
        var root = Assert.IsType<JsonAstArray>(Parse("""
            kind: Deployment
            ---
            kind: Service
            """));

        Assert.Equal(2, root.Items.Count);
    }

    [Fact]
    public void An_anchor_and_its_alias_read_as_the_same_value()
    {
        var root = Assert.IsType<JsonAstObject>(Parse("""
            base: &shared
              retries: 3
            derived: *shared
            """));

        var derived = Assert.IsType<JsonAstObject>(root.Find("derived")!.Value);
        Assert.Equal("3", Scalar(derived, "retries").RawText);
    }

    [Fact]
    public void Text_that_is_not_YAML_at_all_is_refused_with_a_position()
    {
        var parsed = new YamlAstParser().TryParse("key: [unclosed\n", out _, out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.True(error!.Span.IsKnown);
    }

    [Fact]
    public void An_empty_document_is_refused_rather_than_compared_as_nothing()
    {
        Assert.False(new YamlAstParser().TryParse("", out _, out _));
    }

    // ---- Choosing the format ---------------------------------------------------------------------

    [Theory]
    [InlineData("deploy.yaml")]
    [InlineData("deploy.yml")]
    [InlineData("DEPLOY.YAML")]
    public void Auto_reads_a_yaml_named_file_as_yaml(string path) =>
        Assert.Equal(StructuredFormat.Yaml, StructuredFormatDetector.For(path, ComparisonMode.Auto));

    [Theory]
    [InlineData("data.json")]
    [InlineData("notes.txt")]
    [InlineData("server.log")]
    [InlineData(null)]
    public void Auto_offers_everything_else_to_the_json_parser(string? path)
    {
        // Which will decline politely if it is not JSON. YAML is never guessed at, because a log file
        // is perfectly valid YAML and comparing two of them as one-scalar documents would report
        // nothing at all.
        Assert.Equal(StructuredFormat.Json, StructuredFormatDetector.For(path, ComparisonMode.Auto));
    }

    [Fact]
    public void An_explicit_mode_overrules_the_file_name()
    {
        // For the manifest that came out of a pipeline with no extension at all.
        Assert.Equal(StructuredFormat.Yaml, StructuredFormatDetector.For("output", ComparisonMode.Yaml));
        Assert.Equal(StructuredFormat.Json, StructuredFormatDetector.For("a.yaml", ComparisonMode.Json));
        Assert.Equal(StructuredFormat.None, StructuredFormatDetector.For("a.yaml", ComparisonMode.Text));
    }
}
