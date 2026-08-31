using Avalonia.Headless.XUnit;
using AvaloniaEdit.Document;
using Fubar.Diff.Controls.Rendering;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// Marking changes in the Json view's two raw documents.
///
/// The behaviour being protected: EVERY change is visible, not only the one being read. Showing just
/// the current one made the documents beside the tree unreadable as documents - a file with eleven
/// differences displayed one, and finding the others meant pressing Next eleven times.
/// </summary>
public class JsonChangeMarkingTests
{
    private static JsonChange Change(
        string name,
        ChangeKind kind = ChangeKind.Modified,
        bool ignored = false,
        int line = 2) => new(
        JsonPath.Root.Property(name),
        kind,
        new JsonAstScalar(JsonAstKind.Number, "1", null, new SourceSpan(line, 10, line, 11)),
        new JsonAstScalar(JsonAstKind.Number, "2", null, new SourceSpan(line, 10, line, 11)))
    {
        IsIgnored = ignored,
    };

    [AvaloniaFact]
    public void A_modified_value_is_a_removal_on_the_left_and_an_addition_on_the_right()
    {
        // One change, two documents, two readings - the same rule the aligned views follow for a
        // modified row.
        Assert.Equal(ChangeKind.Deleted, JsonChangeSpanColorizer.KindFor(ChangeKind.Modified, DiffSide.Left));
        Assert.Equal(ChangeKind.Inserted, JsonChangeSpanColorizer.KindFor(ChangeKind.Modified, DiffSide.Right));
    }

    [AvaloniaFact]
    public void An_addition_or_removal_keeps_its_own_kind_on_both_sides()
    {
        // A change that only exists on one side has no span on the other, so it paints nowhere there -
        // the kind never has to be reinterpreted.
        Assert.Equal(ChangeKind.Inserted, JsonChangeSpanColorizer.KindFor(ChangeKind.Inserted, DiffSide.Left));
        Assert.Equal(ChangeKind.Deleted, JsonChangeSpanColorizer.KindFor(ChangeKind.Deleted, DiffSide.Right));
    }

    [AvaloniaFact]
    public void The_change_being_read_is_marked_clearly_and_the_rest_quietly()
    {
        var current = new SourceSpan(2, 10, 2, 11);
        var other = new SourceSpan(5, 3, 5, 8);

        Assert.Equal(DiffEmphasis.Normal, JsonChangeSpanColorizer.EmphasisFor(current, current));
        Assert.Equal(DiffEmphasis.Faded, JsonChangeSpanColorizer.EmphasisFor(other, current));
    }

    [AvaloniaFact]
    public void With_nothing_selected_every_change_is_quiet()
    {
        Assert.Equal(DiffEmphasis.Faded, JsonChangeSpanColorizer.EmphasisFor(new SourceSpan(2, 1, 2, 5), null));
    }

    [AvaloniaFact]
    public void The_pane_is_given_the_changes_whose_spans_address_the_text_it_shows()
    {
        // The trap this guards: two lists exist, and the other one addresses the canonicalized copy the
        // aligner worked on. Marking with that one would be a line or two out the moment a user turned
        // on "Reformat for display".
        var pane = new DiffPaneViewModel();
        var aligned = Change("a");
        var original = Change("a", line: 7);

        pane.Show(
            DiffResult.Create([new DiffLine(1, "{", 1, "{", ChangeKind.Unchanged)]),
            isSemantic: true,
            semanticChanges: [aligned],
            leftRawText: "{\n}",
            rightRawText: "{\n}",
            originalSemanticChanges: [original]);

        Assert.Same(original, Assert.Single(pane.SemanticChanges));
    }

    // ---- Which characters a span covers on a line -------------------------------------------------

    private static DocumentLine Line(string text, int number) =>
        new TextDocument(text).GetLineByNumber(number);

    [AvaloniaFact]
    public void A_span_covers_its_own_columns_on_a_single_line()
    {
        var range = SpanRange.Within(Line("{\n  \"a\": 12,\n}", 2), new SourceSpan(2, 8, 2, 10));

        Assert.Equal((7, 9), range);
    }

    [AvaloniaFact]
    public void A_line_the_span_only_passes_through_is_covered_in_full()
    {
        // The middle of a multi-line object. Columns are meaningful only on the span's own first and
        // last lines; anything between them is entirely inside the change.
        var range = SpanRange.Within(Line("{\n  \"a\": {\n    \"b\": 1\n  }\n}", 3), new SourceSpan(2, 3, 4, 4));

        Assert.Equal((0, 10), range);
    }

    [AvaloniaFact]
    public void A_line_outside_the_span_is_not_covered()
    {
        Assert.Null(SpanRange.Within(Line("{\n  \"a\": 1\n}", 1), new SourceSpan(2, 3, 2, 6)));
    }

    [AvaloniaFact]
    public void An_unknown_span_covers_nothing()
    {
        // What a change with no counterpart looks like on the side it is missing from.
        Assert.Null(SpanRange.Within(Line("{\n}", 1), SourceSpan.None));
    }

    [AvaloniaFact]
    public void Columns_past_the_end_of_the_line_are_clamped()
    {
        // Metadata can arrive a frame before the document it describes. An out-of-range offset inside
        // a render pass takes the window down rather than merely looking wrong.
        var range = SpanRange.Within(Line("{\n  \"a\": 1\n}", 2), new SourceSpan(2, 3, 2, 900));

        Assert.Equal((2, 8), range);
    }
}
