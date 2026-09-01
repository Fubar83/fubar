using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests.Json;

/// <summary>
/// What a semantic change COVERS in the text - which is what the Json view highlights.
///
/// The distinction these pin is between a property appearing or disappearing, where the key is part
/// of what changed, and a value being edited, where it is not. Highlighting the value alone for an
/// added field left the key beside it looking untouched, which is the opposite of what happened.
/// </summary>
public class JsonChangeSpanTests
{
    private static readonly SourceSpan Name = new(1, 3, 1, 8);

    private static readonly SourceSpan Value = new(1, 10, 1, 14);

    private static JsonAstNode Node(SourceSpan span) => new JsonAstScalar(JsonAstKind.Number, "42", null, span);

    private static JsonChange Change(ChangeKind kind, bool left, bool right) =>
        new(JsonPath.Root.Property("a"), kind, left ? Node(Value) : null, right ? Node(Value) : null)
        {
            LeftNameSpan = left ? Name : SourceSpan.None,
            RightNameSpan = right ? Name : SourceSpan.None,
        };

    [Fact]
    public void An_added_property_covers_its_key_AND_its_value()
    {
        // The whole "name": value pair is what appeared, so the whole pair is highlighted.
        var change = Change(ChangeKind.Inserted, left: false, right: true);

        Assert.Equal(new SourceSpan(1, 3, 1, 14), change.RightSpan);
    }

    [Fact]
    public void A_removed_property_covers_its_key_AND_its_value()
    {
        var change = Change(ChangeKind.Deleted, left: true, right: false);

        Assert.Equal(new SourceSpan(1, 3, 1, 14), change.LeftSpan);
    }

    [Fact]
    public void A_changed_VALUE_covers_only_the_value()
    {
        // The key is still there and still spelled the same. Colouring it would claim an edit nobody
        // made, and on a long object that reads as far more having changed than did.
        var change = Change(ChangeKind.Modified, left: true, right: true);

        Assert.Equal(Value, change.LeftSpan);
        Assert.Equal(Value, change.RightSpan);
    }

    [Fact]
    public void A_property_that_only_MOVED_covers_the_whole_pair()
    {
        // It went somewhere else as a unit, so highlighting half of it would be arbitrary.
        var change = Change(ChangeKind.Modified, left: true, right: true) with { IsReorder = true };

        Assert.Equal(new SourceSpan(1, 3, 1, 14), change.LeftSpan);
        Assert.Equal(new SourceSpan(1, 3, 1, 14), change.RightSpan);
    }

    [Fact]
    public void An_array_element_has_no_key_to_cover()
    {
        // Elements are identified by position or by an identity key inside them, never by a name
        // beside them - so there is no name span, and the value is the whole story.
        var change = new JsonChange(
            JsonPath.Root.Property("items").Index(0), ChangeKind.Inserted, null, Node(Value));

        Assert.Equal(Value, change.RightSpan);
    }

    [Fact]
    public void The_missing_side_of_a_one_sided_change_covers_nothing()
    {
        var change = Change(ChangeKind.Inserted, left: false, right: true);

        Assert.False(change.LeftSpan.IsKnown);
    }

    [Fact]
    public void A_key_and_value_on_different_lines_are_covered_together()
    {
        // Pretty-printed JSON can put a large added value on the lines below its key.
        var change = new JsonChange(
            JsonPath.Root.Property("a"),
            ChangeKind.Inserted,
            null,
            Node(new SourceSpan(4, 10, 9, 2)))
        {
            RightNameSpan = new SourceSpan(4, 3, 4, 8),
        };

        Assert.Equal(new SourceSpan(4, 3, 9, 2), change.RightSpan);
    }
}
