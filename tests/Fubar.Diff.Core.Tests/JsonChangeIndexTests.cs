using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Line → change, which is what lets "ignore this field" work from the side-by-side view instead of
/// only from the tree. The reverse direction of <see cref="JsonChangeLines"/>.
/// </summary>
public class JsonChangeIndexTests
{
    private static JsonAstScalar Scalar(int line) =>
        new(JsonAstKind.String, "\"x\"", "x", new SourceSpan(line, 1, line, 4));

    private static JsonAstObject Container(int startLine, int endLine) =>
        new([], new SourceSpan(startLine, 1, endLine, 2));

    private static JsonChange Change(JsonPath path, JsonAstNode? left, JsonAstNode? right) =>
        new(path, ChangeKind.Modified, left, right);

    private static JsonPath Path(params object[] steps)
    {
        var path = JsonPath.Root;
        foreach (var step in steps)
        {
            path = step is int index ? path.Index(index) : path.Property((string)step);
        }

        return path;
    }

    [Fact]
    public void Finds_a_change_by_its_left_line()
    {
        var index = JsonChangeIndex.Build([Change(Path("status"), Scalar(8), Scalar(9))]);

        Assert.Equal("$.status", index.Find(8, null)?.Path.ToString());
    }

    /// <summary>An insertion has no left line at all, so the right side must resolve on its own.</summary>
    [Fact]
    public void Finds_a_change_by_its_right_line()
    {
        var index = JsonChangeIndex.Build([Change(Path("added"), null, Scalar(4))]);

        Assert.Equal("$.added", index.Find(null, 4)?.Path.ToString());
    }

    [Fact]
    public void A_line_with_no_change_resolves_to_nothing()
    {
        var index = JsonChangeIndex.Build([Change(Path("status"), Scalar(8), Scalar(8))]);

        Assert.Null(index.Find(3, 3));
    }

    [Fact]
    public void An_empty_index_resolves_to_nothing()
    {
        Assert.Null(JsonChangeIndex.Empty.Find(1, 1));
        Assert.Null(JsonChangeIndex.Build(null).Find(1, 1));
        Assert.Null(JsonChangeIndex.Build([]).Find(1, 1));
    }

    /// <summary>
    /// The important one. An object's span covers its properties' lines, so a line inside a replaced
    /// object matches both. Resolving to the object would ignore far more than the row suggests.
    /// </summary>
    [Fact]
    public void A_line_inside_a_container_resolves_to_the_narrower_change()
    {
        var index = JsonChangeIndex.Build(
        [
            Change(Path("meta"), Container(2, 6), Container(2, 6)),
            Change(Path("meta", "requestId"), Scalar(3), Scalar(3)),
        ]);

        Assert.Equal("$.meta.requestId", index.Find(3, 3)?.Path.ToString());
    }

    /// <summary>Lines the narrower change does not cover still resolve to the container.</summary>
    [Fact]
    public void A_container_still_claims_its_other_lines()
    {
        var index = JsonChangeIndex.Build(
        [
            Change(Path("meta"), Container(2, 6), Container(2, 6)),
            Change(Path("meta", "requestId"), Scalar(3), Scalar(3)),
        ]);

        Assert.Equal("$.meta", index.Find(5, 5)?.Path.ToString());
    }

    /// <summary>A property name span is on a different line from its value for a multi-line value.</summary>
    [Fact]
    public void A_name_span_resolves_too()
    {
        var change = new JsonChange(Path("body"), ChangeKind.Modified, Container(10, 14), Container(10, 14))
        {
            LeftNameSpan = new SourceSpan(10, 3, 10, 9),
        };

        Assert.Equal("$.body", JsonChangeIndex.Build([change]).Find(10, null)?.Path.ToString());
    }

    /// <summary>
    /// The path a row yields is generalized before becoming a rule, so ignoring a field from inside
    /// one array element covers every element - the same rule the tree would have produced.
    /// </summary>
    [Fact]
    public void A_row_inside_an_array_element_generalizes_to_every_element()
    {
        var index = JsonChangeIndex.Build([Change(Path("items", 0, "syncedAt"), Scalar(14), Scalar(14))]);

        var found = index.Find(14, 14);

        Assert.Equal("$.items[0].syncedAt", found?.Path.ToString());
        Assert.Equal("$.items[*].syncedAt", JsonPathPattern.Generalize(found!.Path.ToString()));
    }
}
