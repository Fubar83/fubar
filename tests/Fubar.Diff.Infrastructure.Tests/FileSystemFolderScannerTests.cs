using Fubar.Diff.Core.Folders;
using Fubar.Diff.Infrastructure.Folders;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// The scanner against a real filesystem, including a whole-tree comparison through
/// <see cref="FolderComparer"/> - the walk is tested with a fake elsewhere, so what these add is that
/// the adapter under it reports what the disk actually contains.
/// </summary>
public class FileSystemFolderScannerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fubar-folders-" + Guid.NewGuid().ToString("N"));

    private readonly FileSystemFolderScanner _scanner = new();

    public FileSystemFolderScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return path;
    }

    /// <summary>Threads the test's cancellation token through, which the analyzer asks for.</summary>
    private bool Equal(string left, string right) =>
        _scanner.ContentsEqual(left, right, TestContext.Current.CancellationToken);

    private FolderComparison Compare(string left, string right) =>
        FolderComparer.Compare(
            left, right, _scanner, FolderComparisonOptions.Default, null, TestContext.Current.CancellationToken);

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
    public void Files_are_listed_with_their_sizes()
    {
        Write("a.txt", "12345");

        var files = _scanner.Files(_root);

        var file = Assert.Single(files);
        Assert.Equal("a.txt", file.Name);
        Assert.Equal(5, file.Length);
    }

    [Fact]
    public void Directories_are_listed_by_name()
    {
        Write("sub/a.txt", "x");

        Assert.Equal(["sub"], _scanner.Directories(_root));
    }

    [Fact]
    public void An_unreadable_directory_lists_as_empty_rather_than_throwing()
    {
        // A tree of any size contains something the current user cannot open, and refusing to compare
        // two checkouts over one locked folder would be a worse answer than comparing the rest.
        var missing = Path.Combine(_root, "does-not-exist");

        Assert.Empty(_scanner.Directories(missing));
        Assert.Empty(_scanner.Files(missing));
    }

    [Fact]
    public void Identical_files_compare_equal()
    {
        var a = Write("a.txt", "same contents");
        var b = Write("b.txt", "same contents");

        Assert.True(Equal(a, b));
    }

    [Fact]
    public void Files_of_equal_length_but_different_content_are_not_equal()
    {
        var a = Write("a.txt", "abc");
        var b = Write("b.txt", "xyz");

        Assert.False(Equal(a, b));
    }

    [Fact]
    public void Files_larger_than_one_buffer_are_compared_in_full()
    {
        // The comparison reads in blocks; a difference past the first block is exactly what a
        // buffer-handling mistake would miss.
        var content = new string('a', 200_000);
        var a = Write("a.bin", content);
        var b = Write("b.bin", content[..^1] + "b");

        Assert.False(Equal(a, b));
        Assert.True(Equal(a, Write("c.bin", content)));
    }

    [Fact]
    public void An_unreadable_file_is_a_difference_not_a_match()
    {
        // The one answer a comparison must never give: "these match" about a file it could not open.
        var a = Write("a.txt", "x");

        Assert.False(Equal(a, Path.Combine(_root, "not-there.txt")));
    }

    [Fact]
    public void Empty_files_compare_equal()
    {
        Assert.True(Equal(Write("a.txt", string.Empty), Write("b.txt", string.Empty)));
    }

    [Fact]
    public void A_whole_tree_compares_end_to_end()
    {
        var left = Path.Combine(_root, "left");
        var right = Path.Combine(_root, "right");

        Write("left/same.txt", "identical");
        Write("left/changed.txt", "before");
        Write("left/only-left.txt", "x");
        Write("left/src/deep.cs", "one");
        Write("left/bin/ignored.dll", "junk");

        Write("right/same.txt", "identical");
        Write("right/changed.txt", "after!");
        Write("right/only-right.txt", "y");
        Write("right/src/deep.cs", "one");

        var comparison = Compare(left, right);

        Assert.Equal(2, comparison.SameCount);          // same.txt and src/deep.cs
        Assert.Equal(1, comparison.DifferentCount);     // changed.txt
        Assert.Equal(1, comparison.LeftOnlyCount);      // only-left.txt
        Assert.Equal(1, comparison.RightOnlyCount);     // only-right.txt

        // bin is excluded by default, so its contents never reach the answer.
        Assert.DoesNotContain(Flatten(comparison.Entries), e => e.Name == "bin");
    }

    [Fact]
    public void A_compared_pair_carries_a_path_that_can_actually_be_opened()
    {
        // The point of tracking each side's own relative path: what the tree reports has to be
        // openable, not merely plausible.
        var left = Path.Combine(_root, "left");
        var right = Path.Combine(_root, "right");

        Write("left/src/file.txt", "one");
        Write("right/src/file.txt", "two");

        var comparison = Compare(left, right);
        var entry = Flatten(comparison.Entries).Single(e => e.CanCompare);

        Assert.True(File.Exists(_scanner.Combine(left, entry.LeftRelativePath!)));
        Assert.True(File.Exists(_scanner.Combine(right, entry.RightRelativePath!)));
    }
}
