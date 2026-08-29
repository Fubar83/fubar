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

    /// <summary>
    /// NFC folds the two spellings of an accented letter together. The case this exists for: macOS
    /// decomposes where Windows and Linux compose, so the same word can differ in bytes and be
    /// pixel-identical on screen.
    /// </summary>
    [Fact]
    public void NormalizeUnicode_folds_composed_and_decomposed_forms()
    {
        var options = new ComparisonOptions { NormalizeUnicode = true };

        // "café" precomposed (U+00E9) vs decomposed (e + U+0301). Built from char codes rather than
        // written as escapes, so no tool between here and the compiler can silently normalise the
        // literal and make the test pass for the wrong reason.
        var composed = "caf" + (char)0x00E9;
        var decomposed = "cafe" + (char)0x0301;

        Assert.Equal(
            _normalizer.ToComparisonKey(composed, options),
            _normalizer.ToComparisonKey(decomposed, options));
    }

    [Fact]
    public void The_two_forms_stay_different_without_the_option()
    {
        Assert.NotEqual(
            _normalizer.ToComparisonKey("caf" + (char)0x00E9, ComparisonOptions.Default),
            _normalizer.ToComparisonKey("cafe" + (char)0x0301, ComparisonOptions.Default));
    }

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

    // ---- The JSON pretty-printer, via Canonicalize with NormalizeStructure on --------------------
    //
    // These pin the hand-written printer's diff-friendly layout rules. There used to be a SEPARATE,
    // unconditional CanonicalizeJson entry point that ran this automatically before every semantic
    // comparison's alignment step; that was removed (Text mode shows the file as given, full stop -
    // the Json view is what handles differently-formatted JSON, and needs no reformatting to do it).
    // The printer itself stays, since the explicit "Reformat" toggle still reaches it for JSON.

    private static readonly ComparisonOptions Normalized = new() { NormalizeStructure = true };

    /// <summary>
    /// The rule that keeps line-based diffing usable for arrays of objects: an object or array
    /// holding only scalars stays on ONE line rather than expanding into boilerplate braces that
    /// would be identical across every element and confuse a line-based text differ into matching
    /// them to each other regardless of which element they belong to.
    /// </summary>
    [Fact]
    public void A_container_of_only_scalars_stays_on_one_line()
    {
        var result = _normalizer.Canonicalize(["{\"items\":[{\"id\":1},{\"id\":2}]}"], Normalized);

        Assert.Equal(
            ["{", "  \"items\": [", "    {\"id\":1},", "    {\"id\":2}", "  ]", "}"],
            result);
    }

    /// <summary>A container holding a NESTED container still expands, so its structure is visible.</summary>
    [Fact]
    public void A_container_with_a_nested_container_still_expands()
    {
        var result = _normalizer.Canonicalize(["{\"a\":{\"b\":{\"c\":1}}}"], Normalized);

        Assert.Equal(["{", "  \"a\": {", "    \"b\": {\"c\":1}", "  }", "}"], result);
    }

    [Fact]
    public void An_empty_object_and_array_render_compact()
    {
        Assert.Equal(["{}"], _normalizer.Canonicalize(["{}"], Normalized));
        Assert.Equal(["{", "  \"items\": []", "}"], _normalizer.Canonicalize(["{\"items\":[]}"], Normalized));
    }

    /// <summary>
    /// String escaping comes from the framework's own writer, not hand-rolled - so a value with an
    /// embedded newline and a quote round-trips exactly, on the one line a simple object gets.
    /// </summary>
    [Fact]
    public void Canonicalize_preserves_string_escaping_in_json()
    {
        const string input = "{\"a\":\"line1\\nline2 \\\"quoted\\\"\"}";

        Assert.Equal([input], _normalizer.Canonicalize([input], Normalized));
    }

    /// <summary>Re-canonicalizing an already-canonical document must be a byte-for-byte no-op.</summary>
    [Fact]
    public void Canonicalize_of_json_is_idempotent()
    {
        var once = _normalizer.Canonicalize(["{\"items\":[{\"id\":1},{\"id\":2}]}"], Normalized);
        var twice = _normalizer.Canonicalize(once, Normalized);

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// System.Text.Json's own indented writer uses Environment.NewLine, which is CRLF on Windows - a
    /// naive split on '\n' alone would leave a stray '\r' glued to every line.
    /// </summary>
    [Fact]
    public void Canonicalize_of_json_produces_no_carriage_returns()
    {
        var result = _normalizer.Canonicalize(["{\"a\":{\"b\":1}}"], Normalized);

        Assert.All(result, line => Assert.DoesNotContain('\r', line));
    }
}
