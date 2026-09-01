using Avalonia.Headless.XUnit;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// One pair of Prev/Next buttons for every view.
///
/// A hunk and a semantic change are genuinely different things to step through - one is a run of rows
/// the aligner paired up, the other is a single value that differs - and the Json view used to bring
/// its own Prev/Next strip because of it, with the host hiding its own to avoid two "next" buttons
/// that disagreed. NextDifference decides from the view that is actually on screen, which is what let
/// that second strip go.
/// </summary>
public class DifferenceNavigationTests
{
    private static DiffResult Rows() => DiffResult.Create(
    [
        new DiffLine(1, "{", 1, "{", ChangeKind.Unchanged),
        new DiffLine(2, "  \"a\": 1", 2, "  \"a\": 2", ChangeKind.Modified),
        new DiffLine(3, "  \"b\": 1", 3, "  \"b\": 2", ChangeKind.Modified),
        new DiffLine(4, "}", 4, "}", ChangeKind.Unchanged),
    ]);

    private static JsonChange Change(string name, bool ignored = false) => new(
        JsonPath.Root.Property(name),
        ChangeKind.Modified,
        new JsonAstScalar(JsonAstKind.Number, "1", null, new SourceSpan(2, 10, 2, 11)),
        new JsonAstScalar(JsonAstKind.Number, "2", null, new SourceSpan(2, 10, 2, 11)))
    {
        IsIgnored = ignored,
    };

    private static DiffPaneViewModel Semantic(params JsonChange[] changes)
    {
        var pane = new DiffPaneViewModel();

        pane.Show(
            Rows(),
            isSemantic: true,
            semanticChanges: changes,
            leftRawText: "{\n  \"a\": 1\n  \"b\": 1\n}",
            rightRawText: "{\n  \"a\": 2\n  \"b\": 2\n}",
            originalSemanticChanges: changes);

        return pane;
    }

    private static DiffPaneViewModel Text()
    {
        var pane = new DiffPaneViewModel();
        pane.Show(Rows());

        return pane;
    }

    [AvaloniaFact]
    public void In_a_text_comparison_it_walks_hunks()
    {
        var pane = Text();

        pane.NextDifferenceCommand.Execute(null);

        Assert.Equal(0, pane.CurrentHunk);
        Assert.Null(pane.CurrentSemanticChange);
    }

    [AvaloniaFact]
    public void In_the_Json_view_it_walks_semantic_changes()
    {
        // The hunk selection is deliberately left alone: in this view the hunks are not what is on
        // screen, and moving the text view's cursor behind the scenes would show up as the wrong
        // change the moment someone switched to Text.
        var pane = Semantic(Change("a"), Change("b"));

        pane.NextDifferenceCommand.Execute(null);

        Assert.NotNull(pane.CurrentSemanticChange);
        Assert.Equal("$.a", pane.CurrentSemanticChange!.Path.ToString());
        Assert.Equal(-1, pane.CurrentHunk);
    }

    [AvaloniaFact]
    public void Next_and_previous_walk_the_same_list()
    {
        var pane = Semantic(Change("a"), Change("b"));

        pane.NextDifferenceCommand.Execute(null);
        pane.NextDifferenceCommand.Execute(null);
        Assert.Equal("$.b", pane.CurrentSemanticChange!.Path.ToString());

        pane.PreviousDifferenceCommand.Execute(null);
        Assert.Equal("$.a", pane.CurrentSemanticChange!.Path.ToString());
    }

    [AvaloniaFact]
    public void There_is_something_to_walk_in_either_view()
    {
        Assert.True(Text().HasDifferences);
        Assert.True(Semantic(Change("a")).HasDifferences);
    }

    [AvaloniaFact]
    public void An_empty_pane_has_nothing_to_walk()
    {
        // Which is what hides the buttons rather than greying them out.
        Assert.False(new DiffPaneViewModel().HasDifferences);
    }

    [AvaloniaFact]
    public void Changes_that_are_all_ignored_count_as_nothing_to_walk()
    {
        // Navigation skips ignored changes, so buttons that only had those to move between would do
        // nothing when pressed. The tree still lists them.
        var pane = Semantic(Change("a", ignored: true));

        Assert.False(pane.HasDifferences);
    }
}
