using Fubar.Diff.Core.Settings;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// The recent list's rules. De-duplication is the part worth pinning: re-opening a pair should MOVE it
/// to the top, not add a second copy — otherwise the list fills with one repeated entry and pushes out
/// everything else.
/// </summary>
public class RecentComparisonsTests
{
    private static IReadOnlyList<RecentComparison> Add(
        IReadOnlyList<RecentComparison> existing,
        string left,
        string right,
        int max = AppSettings.MaxRecent) =>
        RecentComparisons.Add(existing, left, right, max);

    [Fact]
    public void A_new_pair_goes_to_the_front()
    {
        var list = Add(Add([], "a", "b"), "c", "d");

        Assert.Equal("c", list[0].Left);
        Assert.Equal("a", list[1].Left);
    }

    [Fact]
    public void Re_adding_a_pair_moves_it_to_the_front_without_duplicating()
    {
        var list = Add(Add(Add([], "a", "b"), "c", "d"), "a", "b");

        Assert.Equal(2, list.Count);
        Assert.Equal("a", list[0].Left);
        Assert.Equal("c", list[1].Left);
    }

    [Fact]
    public void The_same_pair_in_the_other_order_is_a_different_entry()
    {
        // Left and right are not interchangeable - which side is "theirs" matters for merge.
        var list = Add(Add([], "a", "b"), "b", "a");

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void The_list_is_capped()
    {
        IReadOnlyList<RecentComparison> list = [];
        for (var i = 0; i < 10; i++)
        {
            list = Add(list, $"left{i}", $"right{i}", max: 3);
        }

        Assert.Equal(3, list.Count);
        Assert.Equal("left9", list[0].Left);
    }

    [Fact]
    public void An_incomplete_pair_is_not_recorded()
    {
        Assert.Empty(Add([], "a", string.Empty));
        Assert.Empty(Add([], "   ", "b"));
    }

    [Fact]
    public void Prune_drops_entries_whose_files_are_gone()
    {
        IReadOnlyList<RecentComparison> list = [new("keep-l", "keep-r"), new("gone-l", "keep-r")];

        var pruned = RecentComparisons.Prune(list, path => !path.StartsWith("gone", StringComparison.Ordinal));

        Assert.Single(pruned);
        Assert.Equal("keep-l", pruned[0].Left);
    }

    [Fact]
    public void Prune_requires_both_sides_to_exist()
    {
        IReadOnlyList<RecentComparison> list = [new("here", "gone")];

        Assert.Empty(RecentComparisons.Prune(list, path => path == "here"));
    }

    [Fact]
    public void DisplayName_shows_both_file_names()
    {
        var entry = new RecentComparison(
            System.IO.Path.Combine("dir", "old.json"),
            System.IO.Path.Combine("other", "new.json"));

        Assert.Equal("old.json ↔ new.json", entry.DisplayName);
    }
}
