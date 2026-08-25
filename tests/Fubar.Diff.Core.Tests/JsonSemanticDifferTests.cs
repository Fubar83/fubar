using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// What semantic diffing buys over text diffing. Each test here is a case a line-based diff gets
/// visibly wrong.
///
/// The AST is built by hand rather than parsed: the parser lives in Infrastructure and Core cannot
/// reference it, and building nodes directly also keeps these tests about the comparison rules rather
/// than about parsing.
/// </summary>
public class JsonSemanticDifferTests
{
    // ---- Builders -------------------------------------------------------------------------------

    private static JsonAstScalar Str(string value) =>
        new(JsonAstKind.String, $"\"{value}\"", value, SourceSpan.None);

    private static JsonAstScalar Num(string raw) =>
        new(JsonAstKind.Number, raw, null, SourceSpan.None);

    private static JsonAstScalar Null() =>
        new(JsonAstKind.Null, "null", null, SourceSpan.None);

    private static JsonAstObject Obj(params (string Name, JsonAstNode Value)[] properties) =>
        new([.. properties.Select(p => new JsonAstProperty(p.Name, p.Value, SourceSpan.None))], SourceSpan.None);

    private static JsonAstArray Arr(params JsonAstNode[] items) => new(items, SourceSpan.None);

    private static IReadOnlyList<JsonChange> Compare(
        JsonAstNode left,
        JsonAstNode right,
        JsonComparisonOptions? options = null) =>
        JsonSemanticDiffer.Compare(left, right, options ?? JsonComparisonOptions.Default);

    // ---- Objects --------------------------------------------------------------------------------

    [Fact]
    public void Identical_documents_produce_no_changes() =>
        Assert.Empty(Compare(Obj(("a", Num("1"))), Obj(("a", Num("1")))));

    [Fact]
    public void Reordering_properties_is_not_a_difference_by_default()
    {
        // The headline case: JSON objects are unordered, so a serializer emitting the same data in a
        // different order should report nothing. A text diff calls this two changed lines.
        var left = Obj(("a", Num("1")), ("b", Num("2")));
        var right = Obj(("b", Num("2")), ("a", Num("1")));

        Assert.Empty(Compare(left, right));
    }

    [Fact]
    public void Reordering_is_reported_when_asked_for()
    {
        var left = Obj(("a", Num("1")), ("b", Num("2")));
        var right = Obj(("b", Num("2")), ("a", Num("1")));

        var changes = Compare(left, right, new JsonComparisonOptions { ReportPropertyOrder = true });

        Assert.NotEmpty(changes);
        Assert.All(changes, c => Assert.True(c.IsReorder));
    }

    [Fact]
    public void Reordering_reports_only_the_properties_that_had_to_move()
    {
        // a,b,c,d -> b,c,d,a is one property moving, not four. Reporting all four would be as noisy as
        // the text diff this is meant to improve on.
        var left = Obj(("a", Num("1")), ("b", Num("2")), ("c", Num("3")), ("d", Num("4")));
        var right = Obj(("b", Num("2")), ("c", Num("3")), ("d", Num("4")), ("a", Num("1")));

        var changes = Compare(left, right, new JsonComparisonOptions { ReportPropertyOrder = true });

        var change = Assert.Single(changes);
        Assert.Equal("$.a", change.Path.ToString());
    }

    [Fact]
    public void A_changed_value_is_reported_at_its_path()
    {
        var changes = Compare(Obj(("a", Num("1"))), Obj(("a", Num("2"))));

        var change = Assert.Single(changes);
        Assert.Equal(ChangeKind.Modified, change.Kind);
        Assert.Equal("$.a", change.Path.ToString());
    }

    [Fact]
    public void An_added_property_is_reported_as_inserted()
    {
        var change = Assert.Single(Compare(Obj(), Obj(("a", Num("1")))));

        Assert.Equal(ChangeKind.Inserted, change.Kind);
        Assert.Null(change.Left);
        Assert.NotNull(change.Right);
    }

    [Fact]
    public void A_removed_property_is_reported_as_deleted()
    {
        var change = Assert.Single(Compare(Obj(("a", Num("1"))), Obj()));

        Assert.Equal(ChangeKind.Deleted, change.Kind);
        Assert.NotNull(change.Left);
        Assert.Null(change.Right);
    }

    [Fact]
    public void Nested_changes_carry_the_full_path()
    {
        var left = Obj(("outer", Obj(("inner", Str("before")))));
        var right = Obj(("outer", Obj(("inner", Str("after")))));

        Assert.Equal("$.outer.inner", Assert.Single(Compare(left, right)).Path.ToString());
    }

    [Fact]
    public void A_type_change_is_one_replacement_not_a_pile_of_property_differences()
    {
        // Descending into an object to compare it against an array would report every property as
        // removed - true, but useless.
        var change = Assert.Single(Compare(
            Obj(("a", Obj(("x", Num("1")), ("y", Num("2"))))),
            Obj(("a", Arr(Num("1"))))));

        Assert.Equal(ChangeKind.Modified, change.Kind);
        Assert.Equal("$.a", change.Path.ToString());
    }

    [Fact]
    public void Null_versus_missing_is_a_difference_by_default() =>
        Assert.Single(Compare(Obj(("a", Null())), Obj()));

    [Fact]
    public void Null_versus_missing_can_be_ignored() =>
        Assert.Empty(Compare(
            Obj(("a", Null())),
            Obj(),
            new JsonComparisonOptions { IgnoreNullVsMissing = true }));

    [Fact]
    public void Numbers_are_compared_as_written()
    {
        // 1.0 and 1 are the same number but not the same text. This is a diff of a file, so it says so.
        Assert.Single(Compare(Obj(("a", Num("1.0"))), Obj(("a", Num("1")))));
    }

    // ---- Arrays ---------------------------------------------------------------------------------

    [Fact]
    public void An_element_inserted_mid_array_does_not_cascade_when_keys_are_present()
    {
        // The other headline case. Positionally this is three changes; by identity it is one insertion.
        var left = Arr(
            Obj(("id", Num("1")), ("v", Str("a"))),
            Obj(("id", Num("2")), ("v", Str("b"))),
            Obj(("id", Num("3")), ("v", Str("c"))));

        var right = Arr(
            Obj(("id", Num("1")), ("v", Str("a"))),
            Obj(("id", Num("9")), ("v", Str("new"))),
            Obj(("id", Num("2")), ("v", Str("b"))),
            Obj(("id", Num("3")), ("v", Str("c"))));

        var change = Assert.Single(Compare(left, right));

        Assert.Equal(ChangeKind.Inserted, change.Kind);
    }

    [Fact]
    public void Reordered_keyed_elements_are_not_differences()
    {
        var left = Arr(Obj(("id", Num("1"))), Obj(("id", Num("2"))));
        var right = Arr(Obj(("id", Num("2"))), Obj(("id", Num("1"))));

        Assert.Empty(Compare(left, right));
    }

    [Fact]
    public void A_change_inside_a_keyed_element_is_reported_against_that_element()
    {
        var left = Arr(Obj(("id", Num("1")), ("v", Str("before"))));
        var right = Arr(Obj(("id", Num("1")), ("v", Str("after"))));

        var change = Assert.Single(Compare(left, right));

        Assert.Equal("$[0].v", change.Path.ToString());
        Assert.Equal(ChangeKind.Modified, change.Kind);
    }

    [Fact]
    public void A_removed_keyed_element_is_reported_once()
    {
        var left = Arr(Obj(("id", Num("1"))), Obj(("id", Num("2"))));
        var right = Arr(Obj(("id", Num("1"))));

        var change = Assert.Single(Compare(left, right));

        Assert.Equal(ChangeKind.Deleted, change.Kind);
    }

    [Fact]
    public void Arrays_without_a_usable_key_fall_back_to_position()
    {
        var changes = Compare(Arr(Num("1"), Num("2")), Arr(Num("1"), Num("3")));

        var change = Assert.Single(changes);
        Assert.Equal("$[1]", change.Path.ToString());
    }

    [Fact]
    public void Positional_matching_can_be_forced()
    {
        // For an array whose ORDER is the meaning, key matching hides that something moved.
        var left = Arr(Obj(("id", Num("1"))), Obj(("id", Num("2"))));
        var right = Arr(Obj(("id", Num("2"))), Obj(("id", Num("1"))));

        var changes = Compare(left, right, new JsonComparisonOptions { MatchArraysByPosition = true });

        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void A_longer_array_reports_the_extra_elements_as_inserted()
    {
        var changes = Compare(Arr(Num("1")), Arr(Num("1"), Num("2"), Num("3")));

        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal(ChangeKind.Inserted, c.Kind));
    }

    [Fact]
    public void An_empty_array_against_a_populated_one_reports_every_element()
    {
        var changes = Compare(Arr(), Arr(Obj(("id", Num("1"))), Obj(("id", Num("2")))));

        Assert.Equal(2, changes.Count);
    }

    // ---- Paths ----------------------------------------------------------------------------------

    [Fact]
    public void Root_path_is_a_dollar() => Assert.Equal("$", JsonPath.Root.ToString());

    [Fact]
    public void Paths_combine_properties_and_indices() =>
        Assert.Equal("$.users[2].name", JsonPath.Root.Property("users").Index(2).Property("name").ToString());

    [Fact]
    public void Awkward_property_names_are_bracket_quoted() =>
        Assert.Equal("$['my key']", JsonPath.Root.Property("my key").ToString());
}
