using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The identity-key heuristic. It is a guess, so what matters most is when it declines to guess: a key
/// that is missing or duplicated anywhere would silently mismatch elements, which is worse than
/// falling back to positional matching.
/// </summary>
public class ArrayKeyResolverTests
{
    private static JsonAstScalar Str(string value) =>
        new(JsonAstKind.String, $"\"{value}\"", value, SourceSpan.None);

    private static JsonAstScalar Num(string raw) =>
        new(JsonAstKind.Number, raw, null, SourceSpan.None);

    private static JsonAstScalar Null() =>
        new(JsonAstKind.Null, "null", null, SourceSpan.None);

    private static JsonAstObject Obj(params (string Name, JsonAstNode Value)[] properties) =>
        new([.. properties.Select(p => new JsonAstProperty(p.Name, p.Value, SourceSpan.None))], SourceSpan.None);

    private static JsonAstArray Arr(params JsonAstNode[] items) => new(items, SourceSpan.None);

    private static string? Resolve(
        JsonAstArray left,
        JsonAstArray right,
        JsonComparisonOptions? options = null,
        JsonPath? path = null) =>
        ArrayKeyResolver.Resolve(left, right, path ?? JsonPath.Root, options ?? JsonComparisonOptions.Default);

    [Fact]
    public void Finds_a_unique_id()
    {
        var array = Arr(Obj(("id", Num("1"))), Obj(("id", Num("2"))));

        Assert.Equal("id", Resolve(array, array));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("_id")]
    [InlineData("uuid")]
    [InlineData("guid")]
    [InlineData("key")]
    [InlineData("name")]
    public void Recognises_each_candidate_name(string candidate)
    {
        var array = Arr(Obj((candidate, Str("a"))), Obj((candidate, Str("b"))));

        Assert.Equal(candidate, Resolve(array, array));
    }

    [Fact]
    public void Prefers_id_over_name_when_both_are_present()
    {
        // A name is often a label that can legitimately change; an id is meant to be stable, so it
        // produces better matches.
        var array = Arr(
            Obj(("name", Str("a")), ("id", Num("1"))),
            Obj(("name", Str("b")), ("id", Num("2"))));

        Assert.Equal("id", Resolve(array, array));
    }

    [Fact]
    public void Declines_when_the_key_is_duplicated()
    {
        // Two elements sharing an id cannot be told apart, so matching by it would pair the wrong ones.
        var array = Arr(Obj(("id", Num("1"))), Obj(("id", Num("1"))));

        Assert.Null(Resolve(array, array));
    }

    [Fact]
    public void Declines_when_the_key_is_missing_from_some_element()
    {
        var array = Arr(Obj(("id", Num("1"))), Obj(("other", Num("2"))));

        Assert.Null(Resolve(array, array));
    }

    [Fact]
    public void Declines_when_the_key_is_null_on_some_element()
    {
        // Null is a placeholder, not an identity - several elements can carry it.
        var array = Arr(Obj(("id", Num("1"))), Obj(("id", Null())));

        Assert.Null(Resolve(array, array));
    }

    [Fact]
    public void Declines_when_the_key_holds_a_container()
    {
        var array = Arr(Obj(("id", Obj(("nested", Num("1"))))), Obj(("id", Obj(("nested", Num("2"))))));

        Assert.Null(Resolve(array, array));
    }

    [Fact]
    public void Declines_for_arrays_of_scalars() =>
        Assert.Null(Resolve(Arr(Num("1"), Num("2")), Arr(Num("1"), Num("2"))));

    [Fact]
    public void Declines_when_the_key_only_qualifies_on_one_side()
    {
        var left = Arr(Obj(("id", Num("1"))), Obj(("id", Num("2"))));
        var right = Arr(Obj(("id", Num("1"))), Obj(("id", Num("1"))));

        Assert.Null(Resolve(left, right));
    }

    [Fact]
    public void Declines_when_either_side_is_empty()
    {
        // Nothing to match against, so the key would make no difference.
        Assert.Null(Resolve(Arr(), Arr(Obj(("id", Num("1"))))));
    }

    [Fact]
    public void An_override_wins_over_the_heuristic()
    {
        var array = Arr(
            Obj(("id", Num("1")), ("sku", Str("x"))),
            Obj(("id", Num("2")), ("sku", Str("y"))));

        var options = new JsonComparisonOptions
        {
            ArrayKeyOverrides = new Dictionary<string, string> { ["$.items"] = "sku" },
        };

        Assert.Equal("sku", Resolve(array, array, options, JsonPath.Root.Property("items")));
    }

    [Fact]
    public void An_override_applies_only_to_its_own_path()
    {
        var array = Arr(Obj(("id", Num("1"))), Obj(("id", Num("2"))));

        var options = new JsonComparisonOptions
        {
            ArrayKeyOverrides = new Dictionary<string, string> { ["$.other"] = "sku" },
        };

        Assert.Equal("id", Resolve(array, array, options, JsonPath.Root.Property("items")));
    }

    [Fact]
    public void Positional_matching_disables_key_detection()
    {
        var array = Arr(Obj(("id", Num("1"))), Obj(("id", Num("2"))));

        Assert.Null(Resolve(array, array, new JsonComparisonOptions { MatchArraysByPosition = true }));
    }

    [Fact]
    public void A_string_key_and_a_number_key_are_different_identities()
    {
        // Otherwise {"id": 1} and {"id": "1"} would be matched as the same element.
        Assert.NotEqual(
            ArrayKeyResolver.KeyOf(Num("1")),
            ArrayKeyResolver.KeyOf(Str("1")));
    }
}
