using Fubar.Diff.Core.Json;
using Fubar.Diff.Infrastructure.Json;

namespace Fubar.Diff.Application.Tests.Json;

/// <summary>
/// What the change tree can offer for an array on a right-click, and the per-array options behind it.
///
/// The bar these hold is that every field OFFERED actually works as a key. A menu entry that then
/// silently fails to match elements produces a diff that looks like data loss, which is far worse than
/// a shorter menu.
/// </summary>
public class ArrayKeyChoiceTests
{
    private static JsonAstNode Parse(string json)
    {
        Assert.True(new JsonAstParser().TryParse(json, out var root, out var error), error?.Message);

        return root!;
    }

    private static ArrayKeyChoices Choices(string left, string right, string path = "$.items", JsonComparisonOptions? options = null) =>
        ArrayKeyScanner.Scan(Parse(left), Parse(right), options ?? JsonComparisonOptions.Default)[path];

    [Fact]
    public void An_array_of_objects_offers_the_fields_that_could_identify_them()
    {
        var choices = Choices(
            """{"items":[{"id":1,"sku":"a"},{"id":2,"sku":"b"}]}""",
            """{"items":[{"id":1,"sku":"a"},{"id":2,"sku":"b"}]}""");

        Assert.True(choices.ElementsAreObjects);
        Assert.Equal("id", choices.Suggested);
        Assert.Contains("id", choices.Candidates);
        Assert.Contains("sku", choices.Candidates);
    }

    [Fact]
    public void A_field_that_repeats_cannot_identify_anything()
    {
        // Two elements sharing a value would match each other arbitrarily. Offering it would produce a
        // diff that looks like data loss.
        var choices = Choices(
            """{"items":[{"id":1,"kind":"x"},{"id":2,"kind":"x"}]}""",
            """{"items":[{"id":1,"kind":"x"},{"id":2,"kind":"x"}]}""");

        Assert.Contains("id", choices.Candidates);
        Assert.DoesNotContain("kind", choices.Candidates);
    }

    [Fact]
    public void A_field_missing_from_one_element_is_not_offered()
    {
        var choices = Choices(
            """{"items":[{"id":1,"sku":"a"},{"id":2}]}""",
            """{"items":[{"id":1,"sku":"a"},{"id":2}]}""");

        Assert.Contains("id", choices.Candidates);
        Assert.DoesNotContain("sku", choices.Candidates);
    }

    [Fact]
    public void A_field_missing_from_the_OTHER_side_is_not_offered()
    {
        // Both documents have to satisfy it - a key that works on one side only matches nothing.
        var choices = Choices(
            """{"items":[{"id":1,"sku":"a"}]}""",
            """{"items":[{"id":1}]}""");

        Assert.DoesNotContain("sku", choices.Candidates);
    }

    [Fact]
    public void A_nested_field_can_be_a_key()
    {
        // Identity is not always at the top level, and an array keyed on meta.id is exactly the case
        // auto-detection cannot help with.
        var choices = Choices(
            """{"items":[{"meta":{"id":1}},{"meta":{"id":2}}]}""",
            """{"items":[{"meta":{"id":1}},{"meta":{"id":2}}]}""");

        Assert.Contains("meta.id", choices.Candidates);
    }

    [Fact]
    public void An_array_of_scalars_has_no_field_to_choose()
    {
        // The only meaningful question left is whether order matters.
        var choices = Choices("""{"items":[1,2,3]}""", """{"items":[1,2,3]}""");

        Assert.False(choices.ElementsAreObjects);
        Assert.Empty(choices.Candidates);
    }

    [Fact]
    public void No_suggestion_is_made_when_nothing_qualifies()
    {
        var choices = Choices(
            """{"items":[{"a":1},{"a":1}]}""",
            """{"items":[{"a":1},{"a":1}]}""");

        Assert.Null(choices.Suggested);
    }

    [Fact]
    public void Nested_arrays_are_found_too()
    {
        var all = ArrayKeyScanner.Scan(
            Parse("""{"groups":[{"id":1,"items":[{"sku":"a"}]}]}"""),
            Parse("""{"groups":[{"id":1,"items":[{"sku":"a"}]}]}"""),
            JsonComparisonOptions.Default);

        Assert.Contains("$.groups", all.Keys);
        Assert.Contains("$.groups[0].items", all.Keys);
    }

    // ---- Per-array positional matching ------------------------------------------------------------

    [Fact]
    public void One_array_can_be_compared_by_position_while_others_are_not()
    {
        // The reason the per-array form exists: a file can hold a list of users, where order means
        // nothing, alongside a list of steps, where order is the entire content.
        var options = JsonComparisonOptions.Default with { PositionalArrays = ["$.steps"] };

        var left = Parse("""{"users":[{"id":1}],"steps":[{"id":9}]}""");
        var right = Parse("""{"users":[{"id":1}],"steps":[{"id":9}]}""");

        var all = ArrayKeyScanner.Scan(left, right, options);

        Assert.Equal("id", all["$.users"].Suggested);
        Assert.Null(all["$.steps"].Suggested);
    }

    [Fact]
    public void An_explicit_key_wins_even_over_positional()
    {
        // Naming a key for THIS array says what you want about it, and the global switch should not
        // then override the more specific answer.
        var options = JsonComparisonOptions.Default with
        {
            MatchArraysByPosition = true,
            ArrayKeyOverrides = new Dictionary<string, string> { ["$.items"] = "sku" },
        };

        Assert.Equal("sku", Choices(
            """{"items":[{"sku":"a"}]}""",
            """{"items":[{"sku":"a"}]}""",
            options: options).Suggested);
    }

    [Fact]
    public void A_nested_key_actually_matches_elements()
    {
        // The end-to-end point of dotted keys: an element that only MOVED must not read as one removed
        // and another added.
        var options = JsonComparisonOptions.Default with
        {
            ArrayKeyOverrides = new Dictionary<string, string> { ["$.items"] = "meta.id" },
        };

        var changes = JsonSemanticDiffer.Compare(
            Parse("""{"items":[{"meta":{"id":1},"v":"a"},{"meta":{"id":2},"v":"b"}]}"""),
            Parse("""{"items":[{"meta":{"id":2},"v":"b"},{"meta":{"id":1},"v":"a"}]}"""),
            options);

        Assert.Empty(changes);
    }

    [Fact]
    public void Without_the_key_that_same_pair_reads_as_two_changes()
    {
        // The contrast that makes the option worth offering at all.
        var changes = JsonSemanticDiffer.Compare(
            Parse("""{"items":[{"meta":{"id":1},"v":"a"},{"meta":{"id":2},"v":"b"}]}"""),
            Parse("""{"items":[{"meta":{"id":2},"v":"b"},{"meta":{"id":1},"v":"a"}]}"""),
            JsonComparisonOptions.Default);

        Assert.NotEmpty(changes);
    }
}
