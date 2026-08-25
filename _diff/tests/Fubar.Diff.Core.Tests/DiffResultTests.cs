using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Hunk grouping and counting. <see cref="DiffResult.Create"/> derives both from the rows, so these
/// pin down that adjacent changes collapse into one hunk while context breaks the run.
/// </summary>
public class DiffResultTests
{
    private static DiffLine Unchanged(int n) => new(n, "same", n, "same", ChangeKind.Unchanged);
    private static DiffLine Modified(int n) => new(n, "a", n, "b", ChangeKind.Modified);
    private static DiffLine Inserted(int n) => new(null, null, n, "added", ChangeKind.Inserted);
    private static DiffLine Deleted(int n) => new(n, "gone", null, null, ChangeKind.Deleted);

    [Fact]
    public void Identical_documents_produce_no_hunks()
    {
        var result = DiffResult.Create([Unchanged(1), Unchanged(2)]);

        Assert.True(result.AreIdentical);
        Assert.Empty(result.Hunks);
    }

    [Fact]
    public void Adjacent_changes_collapse_into_one_hunk()
    {
        var result = DiffResult.Create([Unchanged(1), Modified(2), Deleted(3), Inserted(4), Unchanged(5)]);

        var hunk = Assert.Single(result.Hunks);
        Assert.Equal(1, hunk.StartIndex);
        Assert.Equal(3, hunk.EndIndex);
        Assert.Equal(3, hunk.Length);
    }

    [Fact]
    public void Context_between_changes_splits_the_hunks()
    {
        var result = DiffResult.Create([Modified(1), Unchanged(2), Modified(3)]);

        Assert.Equal(2, result.Hunks.Count);
        Assert.Equal(0, result.Hunks[0].StartIndex);
        Assert.Equal(2, result.Hunks[1].StartIndex);
    }

    [Fact]
    public void A_change_at_the_very_end_still_closes_its_hunk()
    {
        // Regression guard: a run that never meets a following context line must still be emitted.
        var result = DiffResult.Create([Unchanged(1), Inserted(2)]);

        var hunk = Assert.Single(result.Hunks);
        Assert.Equal(1, hunk.StartIndex);
        Assert.Equal(1, hunk.EndIndex);
    }

    [Fact]
    public void Counts_are_tallied_per_change_kind()
    {
        var result = DiffResult.Create([Inserted(1), Inserted(2), Deleted(3), Modified(4), Unchanged(5)]);

        Assert.Equal(2, result.Inserted);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.Modified);
    }

    [Fact]
    public void Filler_rows_are_not_changes()
    {
        var result = DiffResult.Create([new DiffLine(null, null, null, null, ChangeKind.Filler)]);

        Assert.True(result.AreIdentical);
    }
}
