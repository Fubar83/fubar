using Fubar.Diff.Core.Folders;

namespace Fubar.Diff.Core.Tests;

/// <summary>
/// Walking two trees together. Driven by a fake filesystem, which is the point of the scanner port -
/// every decision the walk makes is domain policy and needs no directories on disk to test.
/// </summary>
public class FolderComparerTests
{
    /// <summary>
    /// A tree described as a path-to-content map, e.g. <c>["src/a.cs"] = "one"</c>. Directories are
    /// implied by the paths, exactly as they are on a real filesystem.
    /// </summary>
    private sealed class FakeScanner(Dictionary<string, string> left, Dictionary<string, string> right) : IFolderScanner
    {
        public int ContentComparisons { get; private set; }

        private static (Dictionary<string, string> Tree, string Relative) Split(string path)
        {
            // Paths here are "L:" or "R:" followed by a relative path, so the fake can tell the two
            // trees apart without a real filesystem.
            var relative = path.Length > 2 ? path[2..].TrimStart('/') : string.Empty;

            return (null!, relative);
        }

        private Dictionary<string, string> TreeFor(string path) => path.StartsWith("L:", StringComparison.Ordinal) ? left : right;

        private static string RelativeOf(string path) => path.Length > 2 ? path[2..].TrimStart('/') : string.Empty;

        public IReadOnlyList<string> Directories(string path)
        {
            var prefix = RelativeOf(path);
            prefix = prefix.Length == 0 ? string.Empty : prefix + "/";

            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var key in TreeFor(path).Keys)
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

            foreach (var (key, content) in TreeFor(path))
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

        public bool ContentsEqual(string leftPath, string rightPath, CancellationToken cancellationToken = default)
        {
            ContentComparisons++;

            return left.TryGetValue(RelativeOf(leftPath), out var a)
                   && right.TryGetValue(RelativeOf(rightPath), out var b)
                   && a == b;
        }

        public string Combine(string root, string relativePath) => $"{root}/{relativePath}";
    }

    private static FolderComparison Compare(
        Dictionary<string, string> left,
        Dictionary<string, string> right,
        FolderComparisonOptions? options = null,
        FakeScanner? scanner = null)
    {
        scanner ??= new FakeScanner(left, right);

        return FolderComparer.Compare("L:", "R:", scanner, options ?? FolderComparisonOptions.Default);
    }

    /// <summary>Flattens the tree, for assertions that do not care about nesting.</summary>
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

    private static FolderEntry Find(FolderComparison comparison, string relativePath) =>
        Flatten(comparison.Entries).Single(e => e.RelativePath == relativePath);

    [Fact]
    public void Identical_trees_report_no_differences()
    {
        var comparison = Compare(
            new() { ["a.txt"] = "one", ["b.txt"] = "two" },
            new() { ["a.txt"] = "one", ["b.txt"] = "two" });

        Assert.True(comparison.AreIdentical);
        Assert.Equal(2, comparison.SameCount);
    }

    [Fact]
    public void A_file_whose_contents_differ_is_reported()
    {
        var comparison = Compare(
            new() { ["a.txt"] = "one" },
            new() { ["a.txt"] = "ONE" });

        Assert.Equal(FolderEntryStatus.Different, Find(comparison, "a.txt").Status);
        Assert.Equal(1, comparison.DifferentCount);
    }

    [Fact]
    public void Files_of_the_same_length_are_still_compared_by_content()
    {
        // The one mistake a folder comparison must not make. Two files of equal size are routinely
        // different, and reporting them identical is how a tool loses trust permanently.
        var comparison = Compare(
            new() { ["a.txt"] = "abc" },
            new() { ["a.txt"] = "xyz" });

        Assert.Equal(FolderEntryStatus.Different, Find(comparison, "a.txt").Status);
    }

    [Fact]
    public void Files_of_different_lengths_skip_the_content_read()
    {
        // Cheap shortcut: different sizes cannot be the same file, and reading them both to prove it
        // would be the slowest part of a large comparison.
        var scanner = new FakeScanner(new() { ["a.txt"] = "short" }, new() { ["a.txt"] = "much longer" });
        Compare(new() { ["a.txt"] = "short" }, new() { ["a.txt"] = "much longer" }, scanner: scanner);

        Assert.Equal(0, scanner.ContentComparisons);
    }

    [Fact]
    public void A_file_on_one_side_only_is_reported_as_such()
    {
        var comparison = Compare(
            new() { ["only-left.txt"] = "x" },
            new() { ["only-right.txt"] = "y" });

        Assert.Equal(FolderEntryStatus.LeftOnly, Find(comparison, "only-left.txt").Status);
        Assert.Equal(FolderEntryStatus.RightOnly, Find(comparison, "only-right.txt").Status);
        Assert.Equal(1, comparison.LeftOnlyCount);
        Assert.Equal(1, comparison.RightOnlyCount);
    }

    [Fact]
    public void Subdirectories_are_walked()
    {
        var comparison = Compare(
            new() { ["src/a.cs"] = "one", ["src/deep/b.cs"] = "two" },
            new() { ["src/a.cs"] = "one", ["src/deep/b.cs"] = "CHANGED" });

        Assert.Equal(FolderEntryStatus.Different, Find(comparison, "src/deep/b.cs").Status);
    }

    [Fact]
    public void A_directory_is_different_exactly_when_something_inside_it_is()
    {
        var comparison = Compare(
            new() { ["src/a.cs"] = "one", ["docs/b.md"] = "two" },
            new() { ["src/a.cs"] = "CHANGED", ["docs/b.md"] = "two" });

        Assert.Equal(FolderEntryStatus.Different, Find(comparison, "src").Status);
        Assert.Equal(FolderEntryStatus.Same, Find(comparison, "docs").Status);
    }

    [Fact]
    public void Directories_are_not_counted_as_differences_themselves()
    {
        // Counting them would report every change once for the file and again for each folder above
        // it - "12 differences" about three changed files is worse than no count at all.
        var comparison = Compare(
            new() { ["a/b/c/file.txt"] = "one" },
            new() { ["a/b/c/file.txt"] = "two" });

        Assert.Equal(1, comparison.DifferenceCount);
    }

    [Fact]
    public void A_directory_on_one_side_only_still_lists_its_contents()
    {
        // Someone comparing two checkouts wants to see WHAT is in the folder only one of them has, not
        // an opaque "only here".
        var comparison = Compare(
            new() { ["extra/one.txt"] = "a", ["extra/two.txt"] = "b" },
            []);

        Assert.Equal(FolderEntryStatus.LeftOnly, Find(comparison, "extra").Status);
        Assert.Equal(FolderEntryStatus.LeftOnly, Find(comparison, "extra/one.txt").Status);
        Assert.Equal(2, comparison.LeftOnlyCount);
    }

    [Fact]
    public void Excluded_names_are_not_compared_or_descended_into()
    {
        var comparison = Compare(
            new() { ["a.txt"] = "one", ["bin/junk.dll"] = "x", [".git/config"] = "y" },
            new() { ["a.txt"] = "one" });

        Assert.True(comparison.AreIdentical);
        Assert.DoesNotContain(Flatten(comparison.Entries), e => e.Name == "bin");
        Assert.DoesNotContain(Flatten(comparison.Entries), e => e.Name == ".git");
    }

    [Fact]
    public void Exclusions_accept_wildcards()
    {
        var comparison = Compare(
            new() { ["a.txt"] = "one", ["b.dll"] = "x" },
            new() { ["a.txt"] = "one" },
            FolderComparisonOptions.Default with { Exclude = ["*.dll"] });

        Assert.True(comparison.AreIdentical);
    }

    [Fact]
    public void Names_are_paired_case_insensitively_by_default()
    {
        // Pairing README.md with readme.md as two unrelated files is wrong on the platforms most people
        // run this on.
        var comparison = Compare(
            new() { ["README.md"] = "one" },
            new() { ["readme.md"] = "one" });

        Assert.True(comparison.AreIdentical);
    }

    [Fact]
    public void Case_sensitivity_can_be_turned_on()
    {
        var comparison = Compare(
            new() { ["README.md"] = "one" },
            new() { ["readme.md"] = "one" },
            FolderComparisonOptions.Default with { IgnoreNameCase = false });

        Assert.Equal(2, comparison.DifferenceCount);
    }

    [Fact]
    public void A_non_recursive_comparison_stops_at_the_top_level()
    {
        var comparison = Compare(
            new() { ["a.txt"] = "one", ["src/b.cs"] = "x" },
            new() { ["a.txt"] = "one", ["src/b.cs"] = "y" },
            FolderComparisonOptions.Default with { Recursive = false });

        Assert.True(comparison.AreIdentical);
        Assert.DoesNotContain(Flatten(comparison.Entries), e => e.IsDirectory);
    }

    [Fact]
    public void Comparing_by_size_alone_can_be_asked_for()
    {
        var scanner = new FakeScanner(new() { ["a.txt"] = "abc" }, new() { ["a.txt"] = "xyz" });

        var comparison = Compare(
            new() { ["a.txt"] = "abc" },
            new() { ["a.txt"] = "xyz" },
            FolderComparisonOptions.Default with { CompareContents = false },
            scanner);

        Assert.True(comparison.AreIdentical);
        Assert.Equal(0, scanner.ContentComparisons);
    }

    [Fact]
    public void Directories_are_listed_before_files_and_each_alphabetically()
    {
        // The ordering every file manager uses, so it is the one a reader scans without thinking.
        var comparison = Compare(
            new() { ["zeta.txt"] = "1", ["alpha.txt"] = "2", ["src/x.cs"] = "3", ["docs/y.md"] = "4" },
            new() { ["zeta.txt"] = "1", ["alpha.txt"] = "2", ["src/x.cs"] = "3", ["docs/y.md"] = "4" });

        Assert.Equal(
            ["docs", "src", "alpha.txt", "zeta.txt"],
            comparison.Entries.Select(e => e.Name));
    }

    [Fact]
    public void Sizes_are_reported_for_the_sides_that_have_the_file()
    {
        var comparison = Compare(
            new() { ["a.txt"] = "12345" },
            []);

        var entry = Find(comparison, "a.txt");

        Assert.Equal(5, entry.LeftSize);
        Assert.Equal(FolderEntry.NoSize, entry.RightSize);
    }

    [Fact]
    public void Two_empty_trees_compare_equal()
    {
        var comparison = Compare([], []);

        Assert.True(comparison.AreIdentical);
        Assert.Empty(comparison.Entries);
    }

    [Fact]
    public void A_cancelled_walk_stops()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var scanner = new FakeScanner(new() { ["a.txt"] = "one" }, new() { ["a.txt"] = "two" });

        // The analyzer wants TestContext's token here; this test is ABOUT the token, so it passes its
        // own already-cancelled one on purpose.
#pragma warning disable xUnit1051
        Assert.Throws<OperationCanceledException>(() =>
            FolderComparer.Compare("L:", "R:", scanner, FolderComparisonOptions.Default, null, cancellation.Token));
#pragma warning restore xUnit1051
    }

    /// <summary>
    /// Collects progress on the calling thread.
    ///
    /// Not <see cref="Progress{T}"/>: that posts to the captured synchronization context, so in a test
    /// without one the reports land on the thread pool some time later and any assertion about them is
    /// a race. A real UI caller wants that marshalling; a test wants to know what was reported.
    /// </summary>
    private sealed class Collected : IProgress<string>
    {
        public List<string> Reports { get; } = [];

        public void Report(string value) => Reports.Add(value);
    }

    [Fact]
    public void Progress_names_each_pair_as_it_is_compared()
    {
        var progress = new Collected();
        var scanner = new FakeScanner(
            new() { ["a.txt"] = "one", ["src/b.txt"] = "two" },
            new() { ["a.txt"] = "one", ["src/b.txt"] = "two" });

        FolderComparer.Compare(
            "L:", "R:", scanner, FolderComparisonOptions.Default, progress, TestContext.Current.CancellationToken);

        Assert.Equal(["a.txt", "src/b.txt"], progress.Reports.Order());
    }

    [Fact]
    public void One_sided_entries_are_not_reported_as_progress()
    {
        // Progress measures work done, and a file only one side has costs nothing to compare - there is
        // nothing to read it against.
        var progress = new Collected();
        var scanner = new FakeScanner(new() { ["only.txt"] = "x" }, []);

        FolderComparer.Compare(
            "L:", "R:", scanner, FolderComparisonOptions.Default, progress, TestContext.Current.CancellationToken);

        Assert.Empty(progress.Reports);
    }

    [Fact]
    public void Only_a_file_present_on_both_sides_can_be_opened_as_a_diff()
    {
        var comparison = Compare(
            new() { ["both.txt"] = "a", ["left.txt"] = "x", ["dir/f.txt"] = "y" },
            new() { ["both.txt"] = "b", ["dir/f.txt"] = "y" });

        Assert.True(Find(comparison, "both.txt").CanCompare);
        Assert.False(Find(comparison, "left.txt").CanCompare);
        Assert.False(Find(comparison, "dir").CanCompare);
    }
}
