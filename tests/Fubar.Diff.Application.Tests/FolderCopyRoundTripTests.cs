using Fubar.Diff.Application.Folders;
using Fubar.Diff.Core.Folders;
using Fubar.Diff.Infrastructure.Files;
using Fubar.Diff.Infrastructure.Folders;

namespace Fubar.Diff.Application.Tests;

/// <summary>
/// Copying between two REAL folders, through the real walk, the real planner and the real copier.
///
/// The unit tests either plan without a disk or copy without a comparison; this is the only place all
/// three meet, which is where a mismatch between them would show. It is worth having because the cost
/// of getting this particular chain wrong is somebody's file.
/// </summary>
public class FolderCopyRoundTripTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fubar-foldercopy-").FullName;

    private string Left => Path.Combine(_root, "A");

    private string Right => Path.Combine(_root, "B");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public FolderCopyRoundTripTests()
    {
        Directory.CreateDirectory(Path.Combine(Left, "sub"));
        Directory.CreateDirectory(Path.Combine(Right, "sub"));

        Write(Left, "same.txt", "same");
        Write(Right, "same.txt", "same");

        Write(Left, "changed.txt", "left version");
        Write(Right, "changed.txt", "right version");

        Write(Left, "onlyleft.txt", "left only");

        Write(Left, Path.Combine("sub", "deep.txt"), "deep left");
        Write(Right, Path.Combine("sub", "deep.txt"), "deep right");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test is a nuisance, not a failure.
        }
    }

    private static void Write(string root, string relativePath, string content) =>
        File.WriteAllText(Path.Combine(root, relativePath), content);

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath));

    private Task<FolderComparison> Compare() =>
        new FolderComparisonService(new FileSystemFolderScanner())
            .CompareAsync(Left, Right, new FolderComparisonOptions(), null, Token);

    private FolderEntry Find(FolderComparison comparison, string name) =>
        Flatten(comparison.Entries).First(e => e.Name == name);

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

    private async Task CopyAll(FolderEntry entry, CopyDirection direction)
    {
        var copier = new FileCopier();

        foreach (var copy in FileCopyPlanner.PlanAll(entry, Left, Right, direction))
        {
            await copier.CopyAsync(copy.SourcePath, copy.DestinationPath, Token);
        }
    }

    [Fact]
    public async Task Copying_a_changed_file_makes_the_two_sides_agree()
    {
        var comparison = await Compare();

        await CopyAll(Find(comparison, "changed.txt"), CopyDirection.ToRight);

        Assert.Equal("left version", Read(Right, "changed.txt"));

        // And the comparison now says so, which is the whole point of re-walking after a copy.
        var after = await Compare();
        Assert.Equal(FolderEntryStatus.Same, Find(after, "changed.txt").Status);
    }

    [Fact]
    public async Task Copying_the_other_way_writes_the_other_version()
    {
        var comparison = await Compare();

        await CopyAll(Find(comparison, "changed.txt"), CopyDirection.ToLeft);

        Assert.Equal("right version", Read(Left, "changed.txt"));
    }

    [Fact]
    public async Task A_file_only_one_side_has_is_created_on_the_other()
    {
        var comparison = await Compare();

        await CopyAll(Find(comparison, "onlyleft.txt"), CopyDirection.ToRight);

        Assert.Equal("left only", Read(Right, "onlyleft.txt"));
    }

    [Fact]
    public async Task Copying_a_folder_reaches_the_files_inside_it()
    {
        var comparison = await Compare();

        await CopyAll(Find(comparison, "sub"), CopyDirection.ToRight);

        Assert.Equal("deep left", Read(Right, Path.Combine("sub", "deep.txt")));
    }

    [Fact]
    public async Task Copying_the_root_leaves_identical_files_untouched()
    {
        // Nothing to copy means nothing written - not even a rewrite with the same bytes, which would
        // change a timestamp and make the file look edited to everything downstream of it.
        var before = File.GetLastWriteTimeUtc(Path.Combine(Right, "same.txt"));

        var comparison = await Compare();

        foreach (var entry in comparison.Entries)
        {
            await CopyAll(entry, CopyDirection.ToRight);
        }

        Assert.Equal(before, File.GetLastWriteTimeUtc(Path.Combine(Right, "same.txt")));

        // Everything else did get copied, so the two trees now match.
        var after = await Compare();
        Assert.True(after.AreIdentical, "the trees should be identical after copying everything one way");
    }

    [Fact]
    public async Task Nothing_is_ever_deleted()
    {
        // The deliberate limit. Copying left-to-right does NOT remove the files only the right has -
        // "make this side match" is the operation that turns a mistake into lost work, and it is not
        // built.
        Write(Right, "onlyright.txt", "right only");

        var comparison = await Compare();

        foreach (var entry in comparison.Entries)
        {
            await CopyAll(entry, CopyDirection.ToRight);
        }

        Assert.True(File.Exists(Path.Combine(Right, "onlyright.txt")));
    }
}
