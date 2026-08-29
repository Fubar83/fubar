using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The merge itself: who changed what, which regions answer themselves, and which need a person.
///
/// The alignments are produced by a plain LCS helper below rather than by the real engine, which is a
/// faithful stand-in precisely because the merger reads only UNCHANGED rows - how an aligner chooses to
/// pair a deletion with an insertion is invisible to it.
/// </summary>
public class ThreeWayMergerTests
{
    /// <summary>
    /// A minimal LCS alignment, enough to produce the unchanged rows the merger reads. Emits
    /// deletions before insertions at a tie, which only affects rows the merger ignores.
    /// </summary>
    private static IReadOnlyList<DiffLine> Align(string[] a, string[] b)
    {
        var lcs = new int[a.Length + 1, b.Length + 1];

        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var rows = new List<DiffLine>();
        int x = 0, y = 0;

        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                rows.Add(new DiffLine(x + 1, a[x], y + 1, b[y], ChangeKind.Unchanged));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                rows.Add(new DiffLine(x + 1, a[x], null, null, ChangeKind.Deleted));
                x++;
            }
            else
            {
                rows.Add(new DiffLine(null, null, y + 1, b[y], ChangeKind.Inserted));
                y++;
            }
        }

        while (x < a.Length)
        {
            rows.Add(new DiffLine(x + 1, a[x], null, null, ChangeKind.Deleted));
            x++;
        }

        while (y < b.Length)
        {
            rows.Add(new DiffLine(null, null, y + 1, b[y], ChangeKind.Inserted));
            y++;
        }

        return rows;
    }

    private static ThreeWayResult Merge(string[] ancestor, string[] left, string[] right) =>
        ThreeWayMerger.Merge(
            MergeDocument.Of(ancestor),
            MergeDocument.Of(left),
            MergeDocument.Of(right),
            Align(ancestor, left),
            Align(ancestor, right));

    private static string[] Merged(ThreeWayResult result, ThreeWayMergeState? state = null) =>
        [.. ThreeWayMergedDocument.Build(result, state ?? ThreeWayMergeState.Empty)];

    /// <summary>
    /// The alignment invariant, checked on every merge below: each column's line numbers step through
    /// its own document exactly once, in order, skipping nothing - and all three columns have the same
    /// number of rows, which is what lets three panes scroll as one.
    /// </summary>
    private static void AssertWellFormed(ThreeWayResult result, string[] ancestor, string[] left, string[] right)
    {
        var expected = new Dictionary<MergeSide, int>
        {
            [MergeSide.Base] = 1,
            [MergeSide.Left] = 1,
            [MergeSide.Right] = 1,
        };

        foreach (var row in result.Lines)
        {
            foreach (var side in new[] { MergeSide.Base, MergeSide.Left, MergeSide.Right })
            {
                if (row.NumberOn(side) is { } number)
                {
                    Assert.Equal(expected[side], number);
                    expected[side]++;
                }
            }

            Assert.True(
                row.BaseNumber is not null || row.LeftNumber is not null || row.RightNumber is not null,
                "a row with no line on any of the three sides is not a row");
        }

        Assert.Equal(ancestor.Length + 1, expected[MergeSide.Base]);
        Assert.Equal(left.Length + 1, expected[MergeSide.Left]);
        Assert.Equal(right.Length + 1, expected[MergeSide.Right]);
    }

    private static ThreeWayResult MergeChecked(string[] ancestor, string[] left, string[] right)
    {
        var result = Merge(ancestor, left, right);
        AssertWellFormed(result, ancestor, left, right);

        return result;
    }

    // ---- Classification -------------------------------------------------------------------------

    [Fact]
    public void Three_identical_documents_have_nothing_to_merge()
    {
        var result = MergeChecked(["a", "b", "c"], ["a", "b", "c"], ["a", "b", "c"]);

        Assert.True(result.AreIdentical);
        Assert.Empty(result.Regions);
    }

    [Fact]
    public void A_change_only_the_left_side_made_is_taken_automatically()
    {
        var result = MergeChecked(["a", "b", "c"], ["a", "B", "c"], ["a", "b", "c"]);

        var region = Assert.Single(result.Regions);
        Assert.Equal(MergeKind.LeftOnly, region.Kind);
        Assert.Equal(0, result.ConflictCount);
        Assert.Equal(["a", "B", "c"], Merged(result));
    }

    [Fact]
    public void A_change_only_the_right_side_made_is_taken_automatically()
    {
        var result = MergeChecked(["a", "b", "c"], ["a", "b", "c"], ["a", "B", "c"]);

        Assert.Equal(MergeKind.RightOnly, Assert.Single(result.Regions).Kind);
        Assert.Equal(["a", "B", "c"], Merged(result));
    }

    [Fact]
    public void The_same_edit_made_on_both_sides_is_not_a_conflict()
    {
        // Constant in practice: a cherry-pick, a shared reformatting, a rebase over a change someone
        // else already landed. Calling these conflicts is how the real ones get buried.
        var result = MergeChecked(["a", "b", "c"], ["a", "B", "c"], ["a", "B", "c"]);

        Assert.Equal(MergeKind.BothSame, Assert.Single(result.Regions).Kind);
        Assert.Equal(0, result.ConflictCount);
        Assert.Equal(["a", "B", "c"], Merged(result));
    }

    [Fact]
    public void Two_different_edits_to_the_same_region_conflict()
    {
        var result = MergeChecked(["a", "b", "c"], ["a", "LEFT", "c"], ["a", "RIGHT", "c"]);

        var region = Assert.Single(result.Regions);
        Assert.Equal(MergeKind.Conflict, region.Kind);
        Assert.Equal(1, result.ConflictCount);
    }

    // ---- Merging --------------------------------------------------------------------------------

    [Fact]
    public void Independent_changes_on_both_sides_are_merged_together()
    {
        // The thing a three-way merge is FOR, and the case a two-way diff cannot do at all: two people
        // edited different parts of the same file, and the answer needs both edits, from one pass, with
        // nobody asked anything.
        var result = MergeChecked(
            ["one", "two", "three", "four", "five"],
            ["ONE", "two", "three", "four", "five"],
            ["one", "two", "three", "four", "FIVE"]);

        Assert.Equal(2, result.Regions.Count);
        Assert.Equal(0, result.ConflictCount);
        Assert.Equal(["ONE", "two", "three", "four", "FIVE"], Merged(result));
    }

    [Fact]
    public void A_line_only_the_left_side_inserted_reaches_the_merge()
    {
        var result = MergeChecked(["a", "c"], ["a", "b", "c"], ["a", "c"]);

        Assert.Equal(["a", "b", "c"], Merged(result));
    }

    [Fact]
    public void A_line_only_the_left_side_deleted_stays_deleted()
    {
        // The filler case: taking the side that has nothing here has to produce NOTHING, not a blank
        // line, or "accept the deletion" would silently leave an empty line behind.
        var result = MergeChecked(["a", "b", "c"], ["a", "c"], ["a", "b", "c"]);

        Assert.Equal(["a", "c"], Merged(result));
    }

    [Fact]
    public void Both_sides_appending_different_things_conflicts_at_the_end()
    {
        var result = MergeChecked(["a"], ["a", "left"], ["a", "right"]);

        Assert.Equal(MergeKind.Conflict, Assert.Single(result.Regions).Kind);
    }

    [Fact]
    public void Both_sides_prepending_different_things_conflicts_at_the_start()
    {
        var result = MergeChecked(["a"], ["left", "a"], ["right", "a"]);

        Assert.Equal(MergeKind.Conflict, Assert.Single(result.Regions).Kind);
    }

    [Fact]
    public void An_empty_ancestor_makes_both_sides_additions()
    {
        var result = MergeChecked([], ["a"], ["b"]);

        Assert.Equal(MergeKind.Conflict, Assert.Single(result.Regions).Kind);
    }

    [Fact]
    public void Everything_deleted_on_both_sides_agrees()
    {
        var result = MergeChecked(["a", "b"], [], []);

        Assert.Equal(MergeKind.BothSame, Assert.Single(result.Regions).Kind);
        Assert.Empty(Merged(result));
    }

    // ---- Resolutions ----------------------------------------------------------------------------

    [Fact]
    public void An_unresolved_conflict_falls_back_to_the_ancestor()
    {
        // Conservative rather than useful, on purpose: the alternatives are inventing a merge nobody
        // approved, or writing conflict markers into a file someone asked to save.
        var result = MergeChecked(["a", "b", "c"], ["a", "LEFT", "c"], ["a", "RIGHT", "c"]);

        Assert.Equal(["a", "b", "c"], Merged(result));
    }

    [Theory]
    [InlineData(MergeChoice.TakeLeft, "LEFT")]
    [InlineData(MergeChoice.TakeRight, "RIGHT")]
    [InlineData(MergeChoice.TakeBase, "b")]
    public void Resolving_a_conflict_picks_that_side(MergeChoice choice, string expected)
    {
        var result = MergeChecked(["a", "b", "c"], ["a", "LEFT", "c"], ["a", "RIGHT", "c"]);

        var state = ThreeWayMergeState.Empty.With(0, choice);

        Assert.Equal(["a", expected, "c"], Merged(result, state));
    }

    [Fact]
    public void An_auto_merged_region_can_still_be_overridden()
    {
        // "Actually, keep what we started with" has to be reachable even where nothing was contested.
        var result = MergeChecked(["a", "b", "c"], ["a", "B", "c"], ["a", "b", "c"]);

        var state = ThreeWayMergeState.Empty.With(0, MergeChoice.TakeBase);

        Assert.Equal(["a", "b", "c"], Merged(result, state));
    }

    [Fact]
    public void Adjacent_edits_from_both_sides_become_one_conflict()
    {
        // Not a defect - the definition of a region is "everything between two points where all three
        // documents line up", and two edits with no surviving line between them have no such point.
        // git resolves this the same way, and the alternative would be presenting two decisions whose
        // answers have to agree with each other.
        var result = MergeChecked(
            ["one", "two", "three"],
            ["ONE", "LEFT", "three"],
            ["one", "RIGHT", "three"]);

        var region = Assert.Single(result.Regions);
        Assert.Equal(MergeKind.Conflict, region.Kind);
    }

    [Fact]
    public void Unresolved_conflicts_are_counted_but_auto_merged_regions_are_not()
    {
        // Separated by a line all three agree on, so they are two independent decisions: one the merge
        // settles itself, one it cannot.
        var result = MergeChecked(
            ["one", "two", "three", "four", "five"],
            ["ONE", "two", "three", "LEFT", "five"],
            ["one", "two", "three", "RIGHT", "five"]);

        var state = ThreeWayMergeState.Empty;

        Assert.Equal(1, result.ConflictCount);
        Assert.Equal(1, result.AutoMergedCount);
        Assert.Equal(1, state.UnresolvedConflicts(result));

        var resolved = state.With(IndexOfFirstConflict(result), MergeChoice.TakeLeft);
        Assert.Equal(0, resolved.UnresolvedConflicts(result));
    }

    private static int IndexOfFirstConflict(ThreeWayResult result)
    {
        for (var i = 0; i < result.Regions.Count; i++)
        {
            if (result.Regions[i].IsConflict)
            {
                return i;
            }
        }

        return -1;
    }

    // ---- Regions --------------------------------------------------------------------------------

    [Fact]
    public void Every_changed_row_belongs_to_exactly_one_region()
    {
        var result = MergeChecked(
            ["one", "two", "three", "four", "five"],
            ["ONE", "two", "three", "LEFT", "five"],
            ["one", "two", "three", "RIGHT", "five"]);

        foreach (var region in result.Regions)
        {
            for (var i = region.StartIndex; i <= region.EndIndex; i++)
            {
                Assert.Equal(region.Kind, result.Lines[i].Kind);
                Assert.True(result.Lines[i].IsChange);
            }
        }

        var inRegion = result.Lines.Count(l => l.RegionIndex >= 0);
        var covered = result.Regions.Sum(r => r.Length);
        Assert.Equal(inRegion, covered);
    }

    [Fact]
    public void A_stable_row_belongs_to_no_region()
    {
        var result = MergeChecked(["a", "b"], ["a", "B"], ["a", "b"]);

        Assert.Equal(-1, result.Lines[0].RegionIndex);
        Assert.False(result.Lines[0].IsChange);
    }
}
