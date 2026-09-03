using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Walking every difference, including the ones a rule is hiding - Shift+Alt+Up/Down.
///
/// Ordinary Prev/Next steps past anything ignored, which is the point of having rules. This is the
/// other question: what exactly am I not being told? Before this there was no answer but scrolling and
/// looking for a faint mark, which on a long file is no answer.
/// </summary>
public class DifferenceStopsTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);

    private static DiffLine Modified(int n) => new(n, "a", n, "b", ChangeKind.Modified);

    private static DiffLine Ignored(int n) => new(n, "a ", n, "a", ChangeKind.Unchanged) { IsIgnored = true };

    /// <summary>Rows 3-4 an ignored run, row 8 a real change, rows 10-11 another ignored run.</summary>
    private static List<DiffLine> Document()
    {
        var lines = Enumerable.Range(1, 14).Select(Unchanged).ToList();

        lines[3] = Ignored(4);
        lines[4] = Ignored(5);
        lines[8] = Modified(9);
        lines[10] = Ignored(11);
        lines[11] = Ignored(12);

        return lines;
    }

    private static IReadOnlyList<DifferenceStop> Stops()
    {
        var lines = Document();

        return DifferenceStops.All(lines, [new DiffHunk(8, 8)]);
    }

    [Fact]
    public void Hunks_and_ignored_runs_are_listed_together_in_document_order()
    {
        Assert.Equal([3, 8, 10], Stops().Select(s => s.StartRow));
    }

    [Fact]
    public void A_run_of_ignored_rows_is_one_stop()
    {
        // Two adjacent ignored rows are one thing that happened - a block whose indentation changed -
        // and stopping once per row is not navigation. Same rule the location map draws by.
        var first = Stops()[0];

        Assert.Equal((3, 4), (first.StartRow, first.EndRow));
        Assert.True(first.IsIgnored);
    }

    [Fact]
    public void An_ignored_stop_belongs_to_no_hunk()
    {
        // It forms none by design, so anything reading HunkIndex must be told that rather than being
        // handed an index that addresses some unrelated hunk.
        Assert.All(Stops().Where(s => s.IsIgnored), s => Assert.Equal(-1, s.HunkIndex));
        Assert.Equal(0, Stops().Single(s => !s.IsIgnored).HunkIndex);
    }

    [Fact]
    public void Next_walks_forward_and_wraps()
    {
        var stops = Stops();

        Assert.Equal(3, DifferenceStops.Next(stops, -1)!.Value.StartRow);
        Assert.Equal(8, DifferenceStops.Next(stops, 3)!.Value.StartRow);
        Assert.Equal(10, DifferenceStops.Next(stops, 8)!.Value.StartRow);
        Assert.Equal(3, DifferenceStops.Next(stops, 10)!.Value.StartRow);
    }

    [Fact]
    public void Previous_walks_back_and_wraps_to_the_last()
    {
        // From "nowhere yet" this lands on the LAST stop, which is what pressing previous first is
        // asking for - and what HunkNavigator.Previous already does.
        var stops = Stops();

        Assert.Equal(10, DifferenceStops.Previous(stops, -1)!.Value.StartRow);
        Assert.Equal(8, DifferenceStops.Previous(stops, 10)!.Value.StartRow);
        Assert.Equal(3, DifferenceStops.Previous(stops, 8)!.Value.StartRow);
        Assert.Equal(10, DifferenceStops.Previous(stops, 3)!.Value.StartRow);
    }

    [Fact]
    public void A_position_inside_a_stop_still_moves_off_it()
    {
        // Position is a row, not an index into the list, so it can land mid-run - and pressing next
        // there must not return the run it is already sitting in.
        var stops = Stops();

        Assert.Equal(8, DifferenceStops.Next(stops, 4)!.Value.StartRow);
        Assert.Equal(3, DifferenceStops.Previous(stops, 4)!.Value.StartRow);
    }

    [Fact]
    public void Nothing_to_walk_returns_null_rather_than_a_stop_that_does_not_exist()
    {
        var clean = Enumerable.Range(1, 5).Select(Unchanged).ToList();
        var stops = DifferenceStops.All(clean, []);

        Assert.Empty(stops);
        Assert.Null(DifferenceStops.Next(stops, -1));
        Assert.Null(DifferenceStops.Previous(stops, -1));
    }
}
