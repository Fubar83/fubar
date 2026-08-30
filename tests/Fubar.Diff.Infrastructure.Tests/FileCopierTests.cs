using Fubar.Diff.Core.Folders;
using Fubar.Diff.Infrastructure.Files;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// Actually writing a file over another one.
///
/// The copier holds no policy - which file goes where is <c>FileCopyPlanner</c>'s job - so what is
/// tested here is the small number of things the filesystem itself demands, and the one refusal that
/// exists to stop a file destroying itself.
/// </summary>
public class FileCopierTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("fubar-copy-").FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test is a nuisance, not a failure.
        }
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return path;
    }

    private string Path_(string relativePath) => Path.Combine(_folder, relativePath);

    [Fact]
    public async Task A_file_is_copied_byte_for_byte()
    {
        var source = Write("a.txt", "hello");
        var destination = Path_("b.txt");

        await new FileCopier().CopyAsync(source, destination, Token);

        Assert.Equal("hello", File.ReadAllText(destination));
    }

    [Fact]
    public async Task An_existing_file_is_replaced()
    {
        var source = Write("a.txt", "new");
        var destination = Write("b.txt", "old");

        await new FileCopier().CopyAsync(source, destination, Token);

        Assert.Equal("new", File.ReadAllText(destination));
    }

    [Fact]
    public async Task A_missing_destination_folder_is_created()
    {
        // Copying a left-only file into a tree that never had that subdirectory is an ordinary case,
        // not an error.
        var source = Write("a.txt", "hello");
        var destination = Path_(Path.Combine("nested", "deeper", "b.txt"));

        await new FileCopier().CopyAsync(source, destination, Token);

        Assert.True(File.Exists(destination));
    }

    [Fact]
    public async Task Copying_a_file_over_itself_is_refused()
    {
        // Possible in one-folder mode, where both roots are the same directory. File.Copy's answer to
        // this on some platforms is to truncate the file to nothing.
        var path = Write("a.txt", "precious");

        var ex = await Assert.ThrowsAsync<FileCopyException>(
            () => new FileCopier().CopyAsync(path, path, Token));

        Assert.Contains("same file", ex.Reason, StringComparison.Ordinal);
        Assert.Equal("precious", File.ReadAllText(path));
    }

    [Fact]
    public async Task The_same_file_reached_by_a_different_spelling_is_still_refused()
    {
        var path = Write("a.txt", "precious");
        var roundabout = Path.Combine(_folder, ".", "a.txt");

        await Assert.ThrowsAsync<FileCopyException>(
            () => new FileCopier().CopyAsync(path, roundabout, Token));

        Assert.Equal("precious", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_source_that_has_vanished_is_reported_rather_than_thrown_raw()
    {
        var ex = await Assert.ThrowsAsync<FileCopyException>(
            () => new FileCopier().CopyAsync(Path_("gone.txt"), Path_("b.txt"), Token));

        Assert.Contains("no longer exists", ex.Reason, StringComparison.Ordinal);
        Assert.False(File.Exists(Path_("b.txt")));
    }

    [Fact]
    public async Task The_destination_is_left_alone_when_the_source_is_missing()
    {
        var destination = Write("b.txt", "still here");

        await Assert.ThrowsAsync<FileCopyException>(
            () => new FileCopier().CopyAsync(Path_("gone.txt"), destination, Token));

        Assert.Equal("still here", File.ReadAllText(destination));
    }
}
