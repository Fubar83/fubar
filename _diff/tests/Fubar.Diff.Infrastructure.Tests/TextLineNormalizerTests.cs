using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Infrastructure.Comparison;

namespace Fubar.Diff.Infrastructure.Tests;

public class TextLineNormalizerTests
{
    private readonly TextLineNormalizer _normalizer = new();

    [Fact]
    public void Default_options_leave_a_line_untouched() =>
        Assert.Equal("  Hello  ", _normalizer.ToComparisonKey("  Hello  ", ComparisonOptions.Default));

    [Fact]
    public void IgnoreWhitespace_trims_the_ends() =>
        Assert.Equal("Hello", _normalizer.ToComparisonKey("  Hello  ", new ComparisonOptions { IgnoreWhitespace = true }));

    [Fact]
    public void IgnoreCase_folds_case() =>
        Assert.Equal("HELLO", _normalizer.ToComparisonKey("Hello", new ComparisonOptions { IgnoreCase = true }));

    [Fact]
    public void Both_options_compose()
    {
        var options = new ComparisonOptions { IgnoreWhitespace = true, IgnoreCase = true };

        Assert.Equal(
            _normalizer.ToComparisonKey("  hello ", options),
            _normalizer.ToComparisonKey("HELLO", options));
    }

    [Fact]
    public void Canonicalize_is_a_no_op_unless_asked_for()
    {
        string[] lines = ["{\"b\":1,\"a\":2}"];

        Assert.Same(lines, _normalizer.Canonicalize(lines, ComparisonOptions.Default));
    }

    [Fact]
    public void Json_is_reindented_so_formatting_alone_is_not_a_difference()
    {
        var options = new ComparisonOptions { NormalizeStructure = true };

        var compact = _normalizer.Canonicalize(["{\"a\":1,\"b\":[2,3]}"], options);
        var sprawling = _normalizer.Canonicalize(
            ["{", "  \"a\"  :  1,", "  \"b\" : [ 2,", "  3 ]", "}"],
            options);

        Assert.Equal(compact, sprawling);
    }

    [Fact]
    public void Json_property_order_is_preserved_so_reordering_still_shows_up()
    {
        var options = new ComparisonOptions { NormalizeStructure = true };

        var ab = _normalizer.Canonicalize(["{\"a\":1,\"b\":2}"], options);
        var ba = _normalizer.Canonicalize(["{\"b\":2,\"a\":1}"], options);

        Assert.NotEqual(ab, ba);
    }

    [Fact]
    public void Xml_is_reindented()
    {
        var options = new ComparisonOptions { NormalizeStructure = true };

        var compact = _normalizer.Canonicalize(["<r><a>1</a></r>"], options);
        var spaced = _normalizer.Canonicalize(["<r>", "    <a>1</a>", "</r>"], options);

        Assert.Equal(compact, spaced);
    }

    [Fact]
    public void Malformed_content_falls_back_to_plain_text()
    {
        // A broken file is exactly when a diff is most wanted, so a parse failure must never
        // fail the comparison.
        var options = new ComparisonOptions { NormalizeStructure = true };
        string[] broken = ["{\"a\": ", "  oops"];

        Assert.Equal(broken, _normalizer.Canonicalize(broken, options));
    }

    [Fact]
    public void Plain_text_is_left_alone_even_with_structure_normalisation_on()
    {
        var options = new ComparisonOptions { NormalizeStructure = true };
        string[] prose = ["Just some text.", "Nothing structured here."];

        Assert.Equal(prose, _normalizer.Canonicalize(prose, options));
    }
}
