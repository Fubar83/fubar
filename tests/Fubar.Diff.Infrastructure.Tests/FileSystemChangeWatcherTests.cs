using Fubar.Diff.Infrastructure.Files;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// The watcher, against a real filesystem, because there is no other way to know it works - the whole
/// point of it is coping with what editors actually do to files, and a fake would only reproduce
/// whatever I assumed they did.
///
/// Timing-tolerant on purpose. These wait for an event with a generous timeout rather than asserting
/// how long it took, and the coalescing test asserts that many writes produce FAR fewer events rather
/// than exactly one: a loaded machine can stretch any burst past a quiet period, and a test that fails
/// when the build agent is busy gets deleted rather than fixed.
/// </summary>
public class FileSystemChangeWatcherTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "fubar-watch-" + Guid.NewGuid().ToString("N"));

    public FileSystemChangeWatcherTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A watcher on the way down can still hold a handle; a leftover temp directory is not
            // worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);

        return path;
    }

    /// <summary>Waits for at least one event, returning how many arrived before things went quiet.</summary>
    private static int WaitForEvents(ref int counter, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && Volatile.Read(ref counter) == 0)
        {
            Thread.Sleep(25);
        }

        // Let any stragglers land before counting.
        Thread.Sleep(500);

        return Volatile.Read(ref counter);
    }

    [Fact]
    public void A_write_is_announced()
    {
        var path = Write("a.txt", "one");

        using var watcher = new FileSystemChangeWatcher();
        var events = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref events);
        watcher.Watch([path]);

        File.WriteAllText(path, "two");

        Assert.True(WaitForEvents(ref events, TimeSpan.FromSeconds(10)) > 0, "the change was never announced");
    }

    [Fact]
    public void A_burst_of_writes_is_coalesced()
    {
        // One save from an editor is several filesystem events - commonly a write to a temporary file
        // and a rename over the target. Re-running a comparison per event would be wasteful and visibly
        // flickery, so the contract is "something changed and has stopped changing".
        var path = Write("b.txt", "one");

        using var watcher = new FileSystemChangeWatcher();
        var events = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref events);
        watcher.Watch([path]);

        for (var i = 0; i < 20; i++)
        {
            File.WriteAllText(path, $"write {i}");
        }

        var count = WaitForEvents(ref events, TimeSpan.FromSeconds(10));

        Assert.True(count > 0, "the burst was never announced");
        Assert.True(count < 5, $"20 rapid writes produced {count} announcements; they should coalesce");
    }

    [Fact]
    public void A_file_replaced_by_rename_is_still_seen()
    {
        // How most editors save: write a temporary file, then rename it over the target. A watcher
        // bound to the FILE rather than its directory stops seeing anything at this point, which is the
        // failure that makes a naive implementation pass in testing and fail in use.
        var path = Write("c.txt", "one");
        var temp = Path.Combine(_directory, "c.tmp");

        using var watcher = new FileSystemChangeWatcher();
        var events = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref events);
        watcher.Watch([path]);

        File.WriteAllText(temp, "two");
        File.Move(temp, path, overwrite: true);

        Assert.True(WaitForEvents(ref events, TimeSpan.FromSeconds(10)) > 0, "the rename was never announced");
    }

    [Fact]
    public void Watching_replaces_whatever_was_watched_before()
    {
        var first = Write("d.txt", "one");
        var second = Write("e.txt", "one");

        using var watcher = new FileSystemChangeWatcher();
        var events = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref events);

        watcher.Watch([first]);
        watcher.Watch([second]);

        File.WriteAllText(first, "changed");
        Thread.Sleep(1000);

        Assert.Equal(0, Volatile.Read(ref events));
    }

    [Fact]
    public void Stopping_silences_it()
    {
        var path = Write("f.txt", "one");

        using var watcher = new FileSystemChangeWatcher();
        var events = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref events);

        watcher.Watch([path]);
        watcher.Stop();

        File.WriteAllText(path, "two");
        Thread.Sleep(1000);

        Assert.Equal(0, Volatile.Read(ref events));
    }

    [Fact]
    public void A_path_that_does_not_exist_is_skipped_rather_than_rejected()
    {
        // A comparison can be set up before both files are.
        using var watcher = new FileSystemChangeWatcher();

        watcher.Watch([Path.Combine(_directory, "nothing-here.txt"), Path.Combine("Z:", "no", "such", "drive.txt")]);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var watcher = new FileSystemChangeWatcher();
        watcher.Watch([Write("g.txt", "one")]);

        watcher.Dispose();
        watcher.Dispose();
    }
}
