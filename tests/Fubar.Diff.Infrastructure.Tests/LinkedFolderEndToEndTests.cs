using Fubar.Diff.Core.Folders;
using Fubar.Diff.Infrastructure.Folders;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// The snapshot-review workflow end to end, against a real folder laid out the way Verify lays one out.
///
/// The unit tests drive the pairing with a fake filesystem; this checks the whole thing works on files
/// that actually exist, including that the paths it reports can be opened - which is the step that
/// turns a listing into a diff.
/// </summary>
public class LinkedFolderEndToEndTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fubar-snapshots-" + Guid.NewGuid().ToString("N"));

    private readonly FileSystemFolderScanner _scanner = new();

    public LinkedFolderEndToEndTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Tests"));

        // A realistic run: one changed, one matching, one brand new, one left behind, and a source
        // file that is none of the above.
        Write("Tests/OrderTests.Creates.verified.json", """{ "total": 10 }""");
        Write("Tests/OrderTests.Creates.received.json", """{ "total": 12 }""");
        Write("Tests/OrderTests.Cancels.verified.json", """{ "state": "cancelled" }""");
        Write("Tests/OrderTests.Cancels.received.json", """{ "state": "cancelled" }""");
        Write("Tests/OrderTests.Refunds.received.json", """{ "brand": "new" }""");
        Write("Tests/OrderTests.Old.verified.json", """{ "stale": true }""");
        Write("Tests/OrderTests.cs", "// not a snapshot");
    }

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

    private void Write(string relativePath, string content) =>
        File.WriteAllText(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)), content);

    private FolderComparison Compare() =>
        LinkedFolderComparer.Compare(
            _root, _scanner, FolderComparisonOptions.Default, LinkRule.Defaults, null,
            TestContext.Current.CancellationToken);

    private static IEnumerable<FolderEntry> Files(IReadOnlyList<FolderEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                foreach (var child in Files(entry.Children))
                {
                    yield return child;
                }
            }
            else
            {
                yield return entry;
            }
        }
    }

    [Fact]
    public void A_snapshot_folder_is_reviewed_pair_by_pair()
    {
        var comparison = Compare();
        var files = Files(comparison.Entries).ToList();

        // Four pairs, and the source file is not one of them.
        Assert.Equal(4, files.Count);
        Assert.DoesNotContain(files, f => f.Name.EndsWith(".cs", StringComparison.Ordinal));

        Assert.Equal(1, comparison.DifferentCount);   // Creates
        Assert.Equal(1, comparison.SameCount);        // Cancels
        Assert.Equal(1, comparison.RightOnlyCount);   // Refunds - new
        Assert.Equal(1, comparison.LeftOnlyCount);    // Old - nothing produces it now
    }

    [Fact]
    public void The_paths_a_pair_reports_can_actually_be_opened()
    {
        // The step that makes it a diff tool rather than a listing.
        var changed = Files(Compare().Entries).Single(f => f.Status == FolderEntryStatus.Different);

        var left = _scanner.Combine(_root, changed.LeftRelativePath!);
        var right = _scanner.Combine(_root, changed.RightRelativePath!);

        Assert.True(File.Exists(left));
        Assert.True(File.Exists(right));
        Assert.NotEqual(File.ReadAllText(left), File.ReadAllText(right));
    }

    [Fact]
    public void The_verified_file_is_the_left_one()
    {
        var changed = Files(Compare().Entries).Single(f => f.Status == FolderEntryStatus.Different);

        Assert.Contains("verified", changed.LeftRelativePath!, StringComparison.Ordinal);
        Assert.Contains("received", changed.RightRelativePath!, StringComparison.Ordinal);
    }

    [Fact]
    public void Pairs_are_named_by_what_the_two_halves_share()
    {
        var names = Files(Compare().Entries).Select(f => f.Name).Order().ToList();

        Assert.Equal(
            ["OrderTests.Cancels.json", "OrderTests.Creates.json", "OrderTests.Old.json", "OrderTests.Refunds.json"],
            names);
    }
}
