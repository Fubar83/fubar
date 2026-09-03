using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Arrays whose order does not matter.
///
/// Identity keys answer "which element is this?" only for objects carrying an id. An array of STRINGS -
/// a set of tags, roles, feature flags - has no field to key on, so it always fell through to positional
/// comparison and <c>["A","B"]</c> against <c>["B","A"]</c> reported two modifications for a document
/// that had not changed. This is the opt-in that fixes it, by matching elements on their whole value.
/// </summary>
public class UnorderedArrayTests
{
    // Built by hand, like JsonSemanticDifferTests: the parser lives in Infrastructure and Core cannot
    // reference it, and building nodes directly keeps these about the matching rules rather than parsing.
    private static JsonAstScalar Str(string value) =>
        new(JsonAstKind.String, $"\"{value}\"", value, SourceSpan.None);

    private static JsonAstScalar Num(string raw) =>
        new(JsonAstKind.Number, raw, null, SourceSpan.None);

    private static JsonAstScalar Bool(bool value) =>
        new(JsonAstKind.Boolean, value ? "true" : "false", null, SourceSpan.None);

    private static JsonAstObject Obj(params (string Name, JsonAstNode Value)[] properties) =>
        new([.. properties.Select(p => new JsonAstProperty(p.Name, p.Value, SourceSpan.None))], SourceSpan.None);

    private static JsonAstArray Arr(params JsonAstNode[] items) => new(items, SourceSpan.None);

    private static JsonAstArray Strs(params string[] values) => Arr([.. values.Select(Str)]);

    private static IReadOnlyList<JsonChange> Compare(
        JsonAstNode left, JsonAstNode right, JsonComparisonOptions options) =>
        JsonSemanticDiffer.Compare(left, right, options);

    private static JsonComparisonOptions Unordered(params string[] paths) =>
        JsonComparisonOptions.Default with { UnorderedArrays = paths };

    // ---- The case this exists for --------------------------------------------------------------

    [Fact]
    public void A_reordered_string_array_is_no_longer_a_difference()
    {
        var changes = Compare(
            Obj(("tags", Strs("A", "B"))),
            Obj(("tags", Strs("B", "A"))),
            Unordered("$.tags"));

        Assert.Empty(changes);
    }

    [Fact]
    public void Without_the_opt_in_it_still_reports_the_reorder()
    {
        // Off by default, because for plenty of arrays the order IS the content.
        var changes = Compare(
            Obj(("tags", Strs("A", "B"))),
            Obj(("tags", Strs("B", "A"))),
            JsonComparisonOptions.Default);

        Assert.NotEmpty(changes);
    }

    [Fact]
    public void A_longer_shuffle_is_still_equal()
    {
        var changes = Compare(
            Obj(("roles", Strs("admin", "editor", "viewer", "owner"))),
            Obj(("roles", Strs("viewer", "owner", "admin", "editor"))),
            Unordered("$.roles"));

        Assert.Empty(changes);
    }

    [Fact]
    public void Numbers_and_booleans_work_the_same_way()
    {
        Assert.Empty(Compare(
            Obj(("a", Arr(Num("1"), Num("2"), Num("3")))),
            Obj(("a", Arr(Num("3"), Num("1"), Num("2")))),
            Unordered("$.a")));

        Assert.Empty(Compare(
            Obj(("a", Arr(Bool(true), Bool(false)))),
            Obj(("a", Arr(Bool(false), Bool(true)))),
            Unordered("$.a")));
    }

    [Fact]
    public void A_type_change_is_not_hidden()
    {
        // "1" and 1 are different values, and an unordered array must not blur that.
        Assert.NotEmpty(Compare(
            Obj(("a", Arr(Num("1")))),
            Obj(("a", Arr(Str("1")))),
            Unordered("$.a")));
    }

    // ---- Real differences still surface --------------------------------------------------------

    [Fact]
    public void A_removed_element_is_reported()
    {
        var changes = Compare(
            Obj(("tags", Strs("A", "B", "C"))),
            Obj(("tags", Strs("C", "A"))),
            Unordered("$.tags"));

        var change = Assert.Single(changes);
        Assert.Equal(ChangeKind.Deleted, change.Kind);
    }

    [Fact]
    public void An_added_element_is_reported()
    {
        var changes = Compare(
            Obj(("tags", Strs("A"))),
            Obj(("tags", Strs("B", "A"))),
            Unordered("$.tags"));

        var change = Assert.Single(changes);
        Assert.Equal(ChangeKind.Inserted, change.Kind);
    }

    [Fact]
    public void A_swapped_element_reads_as_a_modification_rather_than_a_delete_and_an_insert()
    {
        // Leftovers are paired up, so one changed element is one change - not two.
        var changes = Compare(
            Obj(("tags", Strs("A", "B"))),
            Obj(("tags", Strs("B", "C"))),
            Unordered("$.tags"));

        var change = Assert.Single(changes);
        Assert.Equal(ChangeKind.Modified, change.Kind);
    }

    [Fact]
    public void Duplicates_are_counted_rather_than_collapsed()
    {
        // A MULTISET, not a set. ["A","A","B"] against ["A","B"] has genuinely lost an element, and set
        // semantics would call the two equal - the one answer a comparison must never give.
        var changes = Compare(
            Obj(("tags", Strs("A", "A", "B"))),
            Obj(("tags", Strs("B", "A"))),
            Unordered("$.tags"));

        var change = Assert.Single(changes);
        Assert.Equal(ChangeKind.Deleted, change.Kind);
    }

    [Fact]
    public void An_extra_duplicate_on_the_right_is_reported_too()
    {
        var changes = Compare(
            Obj(("tags", Strs("A"))),
            Obj(("tags", Strs("A", "A"))),
            Unordered("$.tags"));

        Assert.Equal(ChangeKind.Inserted, Assert.Single(changes).Kind);
    }

    // ---- Objects and nesting -------------------------------------------------------------------

    [Fact]
    public void It_works_for_objects_with_no_id_to_key_on()
    {
        // The other half of "not only based on object fields": an array of objects that carry nothing
        // the key heuristic recognises still gets matched, by whole value.
        var changes = Compare(
            Obj(("points", Arr(
                Obj(("x", Num("1")), ("y", Num("2"))),
                Obj(("x", Num("3")), ("y", Num("4")))))),
            Obj(("points", Arr(
                Obj(("x", Num("3")), ("y", Num("4"))),
                Obj(("x", Num("1")), ("y", Num("2")))))),
            Unordered("$.points"));

        Assert.Empty(changes);
    }

    [Fact]
    public void Property_order_inside_an_element_does_not_make_it_a_different_element()
    {
        // Consistent with the rest of the comparison: JSON objects are unordered by definition.
        var changes = Compare(
            Obj(("points", Arr(Obj(("x", Num("1")), ("y", Num("2")))))),
            Obj(("points", Arr(Obj(("y", Num("2")), ("x", Num("1")))))),
            Unordered("$.points"));

        Assert.Empty(changes);
    }

    [Fact]
    public void A_nested_arrays_order_still_counts()
    {
        // Opting one array out of ordering says nothing about the arrays inside it. Quietly making those
        // unordered too would hide differences nobody asked to hide.
        var changes = Compare(
            Obj(("rows", Arr(Arr(Num("1"), Num("2"))))),
            Obj(("rows", Arr(Arr(Num("2"), Num("1"))))),
            Unordered("$.rows"));

        Assert.NotEmpty(changes);
    }

    [Fact]
    public void A_nested_array_can_be_opted_in_on_its_own()
    {
        var changes = Compare(
            Obj(("rows", Arr(Arr(Num("1"), Num("2"))))),
            Obj(("rows", Arr(Arr(Num("2"), Num("1"))))),
            Unordered("$.rows", "$.rows[0]"));

        Assert.Empty(changes);
    }

    [Fact]
    public void An_element_that_changed_in_one_field_still_gets_a_field_level_diff()
    {
        // What pairing the leftovers buys. Reporting the whole element as replaced would lose which
        // field moved, which is the thing the reader opened the diff for.
        var changes = Compare(
            Obj(("points", Arr(Obj(("x", Num("1")), ("y", Num("2")))))),
            Obj(("points", Arr(Obj(("x", Num("1")), ("y", Num("9")))))),
            Unordered("$.points"));

        var change = Assert.Single(changes);
        Assert.Contains("y", change.Path.ToString());
    }

    [Fact]
    public void Ignore_rules_still_reach_inside_an_unordered_element()
    {
        // The other reason leftovers are paired rather than piled up: matching purely by value would
        // report a whole element as replaced because a timestamp inside it moved, and the rule covering
        // that timestamp would never get to speak.
        var changes = Compare(
            Obj(("items", Arr(Obj(("name", Str("a")), ("ts", Str("2024-01-01")))))),
            Obj(("items", Arr(Obj(("name", Str("a")), ("ts", Str("2025-06-06")))))),
            JsonComparisonOptions.Default with
            {
                UnorderedArrays = ["$.items"],
                IgnoredPaths = ["$.items[*].ts"],
            });

        Assert.DoesNotContain(changes, c => !c.IsIgnored);
    }

    // ---- Precedence ----------------------------------------------------------------------------

    [Fact]
    public void An_explicit_positional_entry_beats_an_explicit_unordered_one()
    {
        // A contradiction only the user can have written, and positional is its conservative half:
        // reporting a reorder nobody minds is a smaller failure than hiding one that matters.
        var changes = Compare(
            Obj(("tags", Strs("A", "B"))),
            Obj(("tags", Strs("B", "A"))),
            JsonComparisonOptions.Default with
            {
                UnorderedArrays = ["$.tags"],
                PositionalArrays = ["$.tags"],
            });

        Assert.NotEmpty(changes);
    }

    [Fact]
    public void A_named_identity_key_beats_unordered()
    {
        // The key names a field, which is the more specific instruction - and it reports which field of
        // which element changed, where whole-value matching could only say one went and another arrived.
        var changes = Compare(
            Obj(("users", Arr(
                Obj(("ref", Num("1")), ("name", Str("a"))),
                Obj(("ref", Num("2")), ("name", Str("b")))))),
            Obj(("users", Arr(
                Obj(("ref", Num("2")), ("name", Str("b"))),
                Obj(("ref", Num("1")), ("name", Str("CHANGED")))))),
            JsonComparisonOptions.Default with
            {
                UnorderedArrays = ["$.users"],
                ArrayKeyOverrides = new Dictionary<string, string> { ["$.users"] = "ref" },
            });

        var change = Assert.Single(changes);
        Assert.Contains("name", change.Path.ToString());
    }

    [Fact]
    public void Only_the_named_path_is_affected()
    {
        var changes = Compare(
            Obj(("tags", Strs("A", "B")), ("steps", Strs("one", "two"))),
            Obj(("tags", Strs("B", "A")), ("steps", Strs("two", "one"))),
            Unordered("$.tags"));

        Assert.NotEmpty(changes);
        Assert.All(changes, c => Assert.Contains("steps", c.Path.ToString()));
    }

    // ---- The global switch ---------------------------------------------------------------------

    [Fact]
    public void The_global_switch_applies_everywhere()
    {
        var changes = Compare(
            Obj(("tags", Strs("A", "B")), ("steps", Strs("one", "two"))),
            Obj(("tags", Strs("B", "A")), ("steps", Strs("two", "one"))),
            JsonComparisonOptions.Default with { IgnoreArrayOrder = true });

        Assert.Empty(changes);
    }

    [Fact]
    public void The_global_switch_leaves_identity_key_matching_alone()
    {
        // Ranked below automatic key matching on purpose: where a key exists it ignores order already
        // AND says which field of which element changed.
        var changes = Compare(
            Obj(("users", Arr(
                Obj(("id", Num("1")), ("name", Str("a"))),
                Obj(("id", Num("2")), ("name", Str("b")))))),
            Obj(("users", Arr(
                Obj(("id", Num("2")), ("name", Str("b"))),
                Obj(("id", Num("1")), ("name", Str("CHANGED")))))),
            JsonComparisonOptions.Default with { IgnoreArrayOrder = true });

        var change = Assert.Single(changes);
        Assert.Contains("name", change.Path.ToString());
    }

    [Fact]
    public void A_positional_path_still_wins_over_the_global_switch()
    {
        var changes = Compare(
            Obj(("steps", Strs("one", "two"))),
            Obj(("steps", Strs("two", "one"))),
            JsonComparisonOptions.Default with { IgnoreArrayOrder = true, PositionalArrays = ["$.steps"] });

        Assert.NotEmpty(changes);
    }

    // ---- The mode the menu shows is the mode the comparison uses -------------------------------

    // ModeFor is public precisely so the check mark and the comparison cannot drift apart. These pin
    // that they answer the same question the same way.

    [Fact]
    public void ModeFor_reports_unordered_for_a_path_that_opted_in()
    {
        Assert.Equal(
            ArrayMatchMode.Unordered,
            JsonSemanticDiffer.ModeFor("$.tags", resolvedKey: null, Unordered("$.tags")));
    }

    [Fact]
    public void ModeFor_prefers_a_named_key_over_everything()
    {
        var options = JsonComparisonOptions.Default with
        {
            UnorderedArrays = ["$.users"],
            PositionalArrays = ["$.users"],
            MatchArraysByPosition = true,
            ArrayKeyOverrides = new Dictionary<string, string> { ["$.users"] = "ref" },
        };

        Assert.Equal(ArrayMatchMode.Key, JsonSemanticDiffer.ModeFor("$.users", "ref", options));
    }

    [Fact]
    public void ModeFor_prefers_an_explicit_positional_path_over_an_explicit_unordered_one()
    {
        var options = JsonComparisonOptions.Default with
        {
            UnorderedArrays = ["$.tags"],
            PositionalArrays = ["$.tags"],
        };

        Assert.Equal(ArrayMatchMode.Position, JsonSemanticDiffer.ModeFor("$.tags", null, options));
    }

    [Fact]
    public void ModeFor_puts_an_auto_detected_key_above_the_global_unordered_switch()
    {
        var options = JsonComparisonOptions.Default with { IgnoreArrayOrder = true };

        Assert.Equal(ArrayMatchMode.Key, JsonSemanticDiffer.ModeFor("$.users", "id", options));
    }

    [Fact]
    public void ModeFor_falls_back_to_position()
    {
        Assert.Equal(
            ArrayMatchMode.Position,
            JsonSemanticDiffer.ModeFor("$.steps", null, JsonComparisonOptions.Default));
    }

    // ---- Degenerate --------------------------------------------------------------------------

    [Fact]
    public void Empty_arrays_compare_equal()
    {
        Assert.Empty(Compare(Obj(("a", Arr())), Obj(("a", Arr())), Unordered("$.a")));
    }

    [Fact]
    public void An_emptied_array_reports_every_element_as_gone()
    {
        var changes = Compare(Obj(("a", Strs("x", "y"))), Obj(("a", Arr())), Unordered("$.a"));

        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal(ChangeKind.Deleted, c.Kind));
    }
}
