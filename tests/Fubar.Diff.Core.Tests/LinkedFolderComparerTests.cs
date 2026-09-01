using Fubar.Diff.Core.Folders;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Pairing files inside ONE folder by name - the snapshot-testing workflow, where a run leaves
/// <c>Thing.received.json</c> beside <c>Thing.verified.json</c> and reviewing means diffing the two
/// halves of every such pair.
/// </summary>
public class LinkedFolderComparerTests
{
    /// <summary>A single tree described as a path-to-content map.</summary>
    private sealed class FakeScanner(Dictionary<string, string> tree) : IFolderScanner
    {
        private static string RelativeOf(string path) => path.Length > 2 ? path[2..].TrimStart('/') : string.Empty;

        public IReadOnlyList<string> Directories(string path)
        {
            var prefix = RelativeOf(path);
            prefix = prefix.Length == 0 ? string.Empty : prefix + "/";

            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var key in tree.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var rest = key[prefix.Length..];
                var slash = rest.IndexOf('/');

                if (slash > 0)
                {
                    names.Add(rest[..slash]);
                }
            }

            return [.. names];
        }

        public IReadOnlyList<ScannedFile> Files(string path)
        {
            var prefix = RelativeOf(path);
            prefix = prefix.Length == 0 ? string.Empty : prefix + "/";

            var files = new List<ScannedFile>();

            foreach (var (key, content) in tree)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var rest = key[prefix.Length..];
                if (rest.Length > 0 && !rest.Contains('/'))
                {
                    files.Add(new ScannedFile(rest, content.Length));
                }
            }

            return files;
        }

        public bool ContentsEqual(string leftPath, string rightPath, CancellationToken cancellationToken = default) =>
            tree.TryGetValue(RelativeOf(leftPath), out var a)
            && tree.TryGetValue(RelativeOf(rightPath), out var b)
            && a == b;

        public string Combine(string root, string relativePath) => $"{root}/{relativePath}";
    }

    private static FolderComparison Compare(
        Dictionary<string, string> tree,
        IReadOnlyList<LinkRule>? rules = null,
        FolderComparisonOptions? options = null) =>
        LinkedFolderComparer.Compare(
            "T:",
            new FakeScanner(tree),
            options ?? FolderComparisonOptions.Default,
            rules ?? LinkRule.Defaults);

    private static IEnumerable<FolderEntry> Flatten(IReadOnlyList<FolderEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;

            foreach (var child in Flatten(entry.Children))
            {
                yield return child;
            }
        }
    }

    [Fact]
    public void A_verified_and_received_pair_is_compared_against_itself()
    {
        var comparison = Compare(new()
        {
            ["Thing.verified.json"] = "old",
            ["Thing.received.json"] = "new",
        });

        var entry = Assert.Single(comparison.Entries);

        Assert.Equal("Thing.json", entry.Name);
        Assert.Equal(FolderEntryStatus.Different, entry.Status);
        Assert.Equal("Thing.verified.json", entry.LeftRelativePath);
        Assert.Equal("Thing.received.json", entry.RightRelativePath);
    }

    [Fact]
    public void The_verified_file_is_the_left_side()
    {
        // The committed baseline is the left, so a review reads as "what changed since it was
        // approved" - the same direction every other diff in the app runs.
        var comparison = Compare(new()
        {
            ["a.received.json"] = "new",
            ["a.verified.json"] = "old",
        });

        var entry = Assert.Single(comparison.Entries);

        Assert.Contains("verified", entry.LeftRelativePath!, StringComparison.Ordinal);
        Assert.Contains("received", entry.RightRelativePath!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_matching_pair_is_reported_as_the_same()
    {
        var comparison = Compare(new()
        {
            ["a.verified.txt"] = "identical",
            ["a.received.txt"] = "identical",
        });

        Assert.True(comparison.AreIdentical);
        Assert.Equal(1, comparison.SameCount);
    }

    [Fact]
    public void Output_with_no_baseline_is_a_new_snapshot()
    {
        // The one a reviewer most wants to see: a test that produced something for the first time.
        var comparison = Compare(new() { ["fresh.received.json"] = "brand new" });

        var entry = Assert.Single(comparison.Entries);

        Assert.Equal(FolderEntryStatus.RightOnly, entry.Status);
        Assert.Null(entry.LeftRelativePath);
    }

    [Fact]
    public void A_baseline_with_no_output_is_still_reported()
    {
        // A snapshot nothing produces any more - which is how a dead test goes unnoticed.
        var comparison = Compare(new() { ["stale.verified.json"] = "left behind" });

        var entry = Assert.Single(comparison.Entries);

        Assert.Equal(FolderEntryStatus.LeftOnly, entry.Status);
        Assert.Null(entry.RightRelativePath);
    }

    [Fact]
    public void Files_no_rule_matches_are_not_in_the_answer()
    {
        // An ordinary source file sitting next to some snapshots is not a difference; it is just a
        // file. Reporting it as "only on one side" would be nonsense - there is one folder.
        var comparison = Compare(new()
        {
            ["Program.cs"] = "code",
            ["readme.md"] = "words",
            ["a.verified.json"] = "x",
            ["a.received.json"] = "y",
        });

        Assert.Equal(["a.json"], comparison.Entries.Select(e => e.Name));
    }

    [Fact]
    public void Pairs_are_found_in_subdirectories()
    {
        var comparison = Compare(new()
        {
            ["tests/Snapshots/One.verified.txt"] = "a",
            ["tests/Snapshots/One.received.txt"] = "b",
        });

        var entry = Flatten(comparison.Entries).Single(e => !e.IsDirectory);

        Assert.Equal("tests/Snapshots/One.verified.txt", entry.LeftRelativePath);
        Assert.Equal("tests/Snapshots/One.received.txt", entry.RightRelativePath);
    }

    [Fact]
    public void A_folder_with_no_pairs_anywhere_is_not_shown()
    {
        // Unlike the two-tree walk, an empty folder here means "no snapshots live here", and showing
        // it would bury the folders that do.
        var comparison = Compare(new()
        {
            ["src/Program.cs"] = "code",
            ["snapshots/a.verified.json"] = "x",
            ["snapshots/a.received.json"] = "y",
        });

        Assert.Equal(["snapshots"], comparison.Entries.Select(e => e.Name));
    }

    [Fact]
    public void The_approvaltests_convention_works_too()
    {
        var comparison = Compare(new()
        {
            ["Case.approved.txt"] = "old",
            ["Case.received.txt"] = "new",
        });

        Assert.Equal("Case.txt", Assert.Single(comparison.Entries).Name);
    }

    [Fact]
    public void Custom_rules_can_be_supplied()
    {
        var comparison = Compare(
            new() { ["report.baseline.csv"] = "a", ["report.current.csv"] = "b" },
            [new LinkRule(".baseline", ".current")]);

        Assert.Equal("report.csv", Assert.Single(comparison.Entries).Name);
    }

    [Fact]
    public void Two_pairs_in_one_folder_stay_separate()
    {
        var comparison = Compare(new()
        {
            ["a.verified.json"] = "1",
            ["a.received.json"] = "2",
            ["b.verified.json"] = "3",
            ["b.received.json"] = "3",
        });

        Assert.Equal(["a.json", "b.json"], comparison.Entries.Select(e => e.Name));
        Assert.Equal(1, comparison.DifferentCount);
        Assert.Equal(1, comparison.SameCount);
    }

    [Fact]
    public void Both_roots_are_the_one_folder()
    {
        // Which is what lets the whole window, the filtering and opening a pair work unchanged: the
        // two halves differ by FILE NAME, not by root.
        var comparison = Compare(new() { ["a.verified.json"] = "x", ["a.received.json"] = "y" });

        Assert.Equal("T:", comparison.LeftRoot);
        Assert.Equal(comparison.LeftRoot, comparison.RightRoot);
    }

    [Fact]
    public void Exclusions_still_apply()
    {
        var comparison = Compare(new()
        {
            ["bin/a.verified.json"] = "x",
            ["bin/a.received.json"] = "y",
        });

        Assert.Empty(comparison.Entries);
    }

    [Fact]
    public void An_empty_folder_produces_nothing()
    {
        Assert.Empty(Compare([]).Entries);
    }

    [Fact]
    public void A_cancelled_walk_stops()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var scanner = new FakeScanner(new() { ["a.verified.json"] = "x" });

#pragma warning disable xUnit1051
        Assert.Throws<OperationCanceledException>(() =>
            LinkedFolderComparer.Compare(
                "T:", scanner, FolderComparisonOptions.Default, LinkRule.Defaults, null, cancellation.Token));
#pragma warning restore xUnit1051
    }
}

/// <summary>The name-matching half, tested on its own because it decides what pairs with what.</summary>
public class FileLinkerTests
{
    [Theory]
    [InlineData("Thing.verified.json", "Thing.json", LinkSide.Left)]
    [InlineData("Thing.received.json", "Thing.json", LinkSide.Right)]
    [InlineData("Case.approved.txt", "Case.txt", LinkSide.Left)]
    [InlineData("report.expected.csv", "report.csv", LinkSide.Left)]
    [InlineData("report.actual.csv", "report.csv", LinkSide.Right)]
    public void A_marker_is_removed_to_give_the_shared_key(string name, string key, LinkSide side)
    {
        var link = FileLinker.Match(name, LinkRule.Defaults, ignoreCase: true);

        Assert.NotNull(link);
        Assert.Equal(key, link!.Value.Key);
        Assert.Equal(side, link.Value.Side);
    }

    [Fact]
    public void A_name_no_rule_matches_is_not_a_link()
    {
        Assert.Null(FileLinker.Match("Program.cs", LinkRule.Defaults, ignoreCase: true));
    }

    [Fact]
    public void A_marker_anywhere_in_the_name_counts()
    {
        // Verify puts the framework and parameters in between, so the marker is not always last.
        var link = FileLinker.Match("MyTest.MyMethod.verified.DotNet8.txt", LinkRule.Defaults, ignoreCase: true);

        Assert.Equal("MyTest.MyMethod.DotNet8.txt", link!.Value.Key);
    }

    [Fact]
    public void A_rule_with_a_blank_half_is_ignored()
    {
        // It would otherwise match every file in the folder.
        Assert.Null(FileLinker.Match("anything.txt", [new LinkRule(string.Empty, ".received")], ignoreCase: true));
    }

    [Fact]
    public void Rules_are_parsed_from_what_a_user_types()
    {
        var rules = FileLinker.Parse(".verified = .received\n.approved:.received\n\nrubbish\n");

        Assert.Equal(2, rules.Count);
        Assert.Equal(".verified", rules[0].Left);
        Assert.Equal(".received", rules[0].Right);
        Assert.Equal(".approved", rules[1].Left);
    }

    [Fact]
    public void Unparseable_lines_are_dropped_rather_than_rejected()
    {
        // These live in a settings file someone can hand-edit; one bad line must not disable the rest.
        var rules = FileLinker.Parse("nonsense\n.verified = .received");

        Assert.Single(rules);
    }

    [Fact]
    public void A_rule_round_trips_through_its_text_form()
    {
        var rules = FileLinker.Parse(string.Join(Environment.NewLine, LinkRule.Defaults));

        Assert.Equal(LinkRule.Defaults, rules);
    }
}
