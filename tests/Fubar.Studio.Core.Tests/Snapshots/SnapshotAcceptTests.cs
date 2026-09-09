using System.Text.Json.Nodes;
using Fubar.Studio.Core.Snapshots;

namespace Fubar.Studio.Core.Tests.Snapshots;

/// <summary>
/// Writing one difference from a response into the snapshot it was compared against.
///
/// <para>What makes a forty-difference wall workable: accept the three that were intended, and what
/// remains is the regression. So the assertion that matters in almost every test here is the second
/// one - that the OTHER differences are still standing afterwards.</para>
/// </summary>
public class SnapshotAcceptTests
{
    private static JsonNode Json(string text) => JsonNode.Parse(text)!;

    [Fact]
    public void One_field_is_taken_and_the_rest_are_left_alone()
    {
        var snapshot = Json("""{"total":10,"currency":"EUR","status":"paid"}""");
        var response = Json("""{"total":99,"currency":"USD","status":"paid"}""");

        Assert.True(SnapshotAccept.Field(snapshot, response, "$.total"));

        Assert.Equal(99, (int)snapshot["total"]!);

        // The regression is still there. This is the whole point.
        Assert.Equal("EUR", (string)snapshot["currency"]!);
    }

    [Fact]
    public void A_nested_field_is_addressed_by_its_path()
    {
        var snapshot = Json("""{"order":{"lines":[{"qty":1},{"qty":2}]}}""");
        var response = Json("""{"order":{"lines":[{"qty":1},{"qty":7}]}}""");

        Assert.True(SnapshotAccept.Field(snapshot, response, "$.order.lines[1].qty"));

        Assert.Equal(7, (int)snapshot["order"]!["lines"]![1]!["qty"]!);
        Assert.Equal(1, (int)snapshot["order"]!["lines"]![0]!["qty"]!);
    }

    /// <summary>A field the response no longer has went AWAY, and accepting that means taking it out
    /// of the snapshot - leaving it there would differ forever.</summary>
    [Fact]
    public void A_field_the_response_no_longer_has_is_removed()
    {
        var snapshot = Json("""{"total":10,"legacy":true}""");
        var response = Json("""{"total":10}""");

        Assert.True(SnapshotAccept.Field(snapshot, response, "$.legacy"));

        Assert.False(snapshot.AsObject().ContainsKey("legacy"));
    }

    [Fact]
    public void A_field_the_snapshot_does_not_have_yet_is_added()
    {
        var snapshot = Json("""{"total":10}""");
        var response = Json("""{"total":10,"currency":"EUR"}""");

        Assert.True(SnapshotAccept.Field(snapshot, response, "$.currency"));

        Assert.Equal("EUR", (string)snapshot["currency"]!);
    }

    [Fact]
    public void A_whole_object_can_be_accepted_as_one_value()
    {
        var snapshot = Json("""{"meta":{"a":1}}""");
        var response = Json("""{"meta":{"a":2,"b":3}}""");

        Assert.True(SnapshotAccept.Field(snapshot, response, "$.meta"));

        Assert.Equal(2, (int)snapshot["meta"]!["a"]!);
        Assert.Equal(3, (int)snapshot["meta"]!["b"]!);
    }

    /// <summary>The two documents must not end up sharing a node: a later accept into one would then
    /// silently change the other.</summary>
    [Fact]
    public void The_accepted_value_is_copied_rather_than_shared()
    {
        var snapshot = Json("""{"meta":{"a":1}}""");
        var response = Json("""{"meta":{"a":2}}""");

        SnapshotAccept.Field(snapshot, response, "$.meta");
        response["meta"]!["a"] = 99;

        Assert.Equal(2, (int)snapshot["meta"]!["a"]!);
    }

    [Fact]
    public void A_path_that_names_nothing_changes_nothing()
    {
        var snapshot = Json("""{"total":10}""");

        Assert.False(SnapshotAccept.Field(snapshot, Json("""{"total":10}"""), ""));
        Assert.Equal(10, (int)snapshot["total"]!);
    }

    /// <summary>An array that grew: the difference is reported at an index past the end of the
    /// snapshot's array, and accepting it appends rather than failing.</summary>
    [Fact]
    public void An_element_past_the_end_is_appended()
    {
        var snapshot = Json("""{"tags":["a"]}""");
        var response = Json("""{"tags":["a","b"]}""");

        Assert.True(SnapshotAccept.Field(snapshot, response, "$.tags[1]"));

        Assert.Equal(2, snapshot["tags"]!.AsArray().Count);
        Assert.Equal("b", (string)snapshot["tags"]![1]!);
    }
}
