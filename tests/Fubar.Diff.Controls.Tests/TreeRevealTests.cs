using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// Stepping through differences has to MOVE the tree, not just change what it thinks is selected.
///
/// The two-way selection sync already existed - Prev/Next set the selected node and clicking a row set
/// the change index - but nothing opened the ancestors, so on a deep document the tree agreed it had
/// moved and showed nothing. A selection nobody can see is not a selection.
/// </summary>
public class TreeRevealTests
{
    private static JsonAstScalar Str(string value) =>
        new(JsonAstKind.String, $"\"{value}\"", value, SourceSpan.None);

    /// <summary>A change five levels down, like the glossary document that prompted this.</summary>
    private static JsonChange Deep(string leaf) =>
        new(
            JsonPath.Root
                .Property("glossary").Property("GlossDiv").Property("GlossList")
                .Property("GlossEntry").Property("GlossDef").Property(leaf),
            ChangeKind.Modified,
            Str("before"),
            Str("after"));

    private static (JsonChangeNodeViewModel Node, IReadOnlyList<JsonChangeNodeViewModel> Roots) Build(string leaf)
    {
        var change = Deep(leaf);
        var (roots, byPath) = JsonChangeNodeViewModel.Build([change]);

        return (byPath[change.Path.ToString()], roots);
    }

    [Fact]
    public void A_row_knows_its_parent()
    {
        var (node, _) = Build("GlossSee");

        Assert.NotNull(node.Parent);
        Assert.Equal("GlossDef", node.Parent!.Label);
    }

    [Fact]
    public void Revealing_opens_every_ancestor()
    {
        var (node, _) = Build("GlossSee");

        node.Reveal();

        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            Assert.True(ancestor.IsExpanded, $"{ancestor.Label} should be open");
        }
    }

    [Fact]
    public void Revealing_does_NOT_open_the_row_itself()
    {
        // A change row has nothing worth unfolding, and unfolding a group would bury the row that was
        // just selected under its own contents.
        var (node, _) = Build("GlossSee");

        node.Reveal();

        Assert.False(node.IsExpanded);
    }

    [Fact]
    public void Rows_start_closed_so_nothing_about_the_first_render_changed()
    {
        // IsExpanded is new; defaulting it to true would have silently unfolded every tree in the app.
        var (node, roots) = Build("GlossSee");

        Assert.False(node.IsExpanded);
        Assert.All(roots, r => Assert.False(r.IsExpanded));
    }

    [Fact]
    public void Expanding_a_row_by_hand_is_not_undone_by_revealing_another()
    {
        // Reveal only ever OPENS. A pass that closed everything else first would fight the user every
        // time they stepped to the next difference.
        var (node, roots) = Build("GlossSee");
        var unrelated = roots[0];
        unrelated.IsExpanded = true;

        node.Reveal();

        Assert.True(unrelated.IsExpanded);
    }

    [Fact]
    public void Navigating_reveals_the_row_it_selects()
    {
        // The end-to-end behaviour: set the change index the way Prev/Next does, and the tree should be
        // both selecting AND showing the row.
        var change = Deep("GlossSee");
        var (roots, _) = JsonChangeNodeViewModel.Build([change]);

        var pane = new DiffPaneViewModel();
        pane.Show(
            DiffResult.Create([]),
            semanticChanges: [change],
            originalSemanticChanges: [change],
            leftRawText: "{}",
            rightRawText: "{}");

        pane.CurrentSemanticChangeIndex = 0;

        Assert.NotNull(pane.CurrentTreeNode);

        for (var ancestor = pane.CurrentTreeNode!.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            Assert.True(ancestor.IsExpanded, $"{ancestor.Label} should be open");
        }

        _ = roots;
    }
}
