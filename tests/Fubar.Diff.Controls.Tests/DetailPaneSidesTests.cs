using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// The Json close-up splits its height between the two sides - and a side with nothing in it should not
/// get half the pane.
///
/// An inserted value exists only on the right and a deleted one only on the left, so for a large share
/// of the changes anyone navigates to, an even split spends half the pane on an empty box beside the
/// half holding the thing being read. On a minified document that is the difference between seeing the
/// change and not: there the excerpt is the whole line, and the side with content needs every pixel.
/// </summary>
public class DetailPaneSidesTests
{
    private static JsonAstScalar Str(string value, SourceSpan span) =>
        new(JsonAstKind.String, $"\"{value}\"", value, span);

    private static DiffPaneViewModel Pane(JsonChange change, string leftText, string rightText)
    {
        var pane = new DiffPaneViewModel { IsDetailVisible = true };

        pane.Show(
            DiffResult.Create([new DiffLine(1, leftText, 1, rightText, ChangeKind.Modified)]),
            semanticChanges: [change],
            originalSemanticChanges: [change],
            leftRawText: leftText,
            rightRawText: rightText);

        pane.CurrentSemanticChangeIndex = 0;

        return pane;
    }

    [Fact]
    public void An_insertion_has_no_left_side()
    {
        // Only the right document holds it, so the left excerpt is empty and must not take up room.
        var change = new JsonChange(
            JsonPath.Root.Property("added"),
            ChangeKind.Inserted,
            null,
            Str("Tjosan", new SourceSpan(1, 1, 1, 8)));

        var pane = Pane(change, leftText: "{}", rightText: """{"added":"Tjosan"}""");

        Assert.False(pane.HasDetailLeft);
        Assert.True(pane.HasDetailRight);
    }

    [Fact]
    public void A_deletion_has_no_right_side()
    {
        var change = new JsonChange(
            JsonPath.Root.Property("gone"),
            ChangeKind.Deleted,
            Str("markup", new SourceSpan(1, 1, 1, 8)),
            null);

        var pane = Pane(change, leftText: """{"gone":"markup"}""", rightText: "{}");

        Assert.True(pane.HasDetailLeft);
        Assert.False(pane.HasDetailRight);
    }

    [Fact]
    public void A_modification_has_both()
    {
        var change = new JsonChange(
            JsonPath.Root.Property("total"),
            ChangeKind.Modified,
            Str("one", new SourceSpan(1, 1, 1, 6)),
            Str("two", new SourceSpan(1, 1, 1, 6)));

        var pane = Pane(change, leftText: """{"total":"one"}""", rightText: """{"total":"two"}""");

        Assert.True(pane.HasDetailLeft);
        Assert.True(pane.HasDetailRight);
    }

    [Fact]
    public void Both_are_false_before_anything_is_selected()
    {
        // The pane between comparisons. The view treats this as "leave the split alone" rather than
        // collapsing to nothing, because a band that vanishes and returns while stepping through
        // differences is worse than one that stays put.
        var pane = new DiffPaneViewModel();

        Assert.False(pane.HasDetailLeft);
        Assert.False(pane.HasDetailRight);
    }
}
