using Fubar.Studio.Core.Diagnostics;
using Fubar.Studio.Infrastructure.Diagnostics;

namespace Fubar.Studio.Infrastructure.Tests.Diagnostics;

public class RollingFileLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "fubar-log-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void An_entry_reaches_the_file()
    {
        var log = new RollingFileLog(_directory);

        log.Write(DateTimeOffset.UtcNow, LogLevel.Error, "token request failed");

        var written = File.ReadAllText(log.Location!);
        Assert.Contains("ERROR", written, StringComparison.Ordinal);
        Assert.Contains("token request failed", written, StringComparison.Ordinal);
    }

    /// <summary>The file is read one entry per line, so a message spanning lines must not break that
    /// contract - an exception message with a stack trace in it would otherwise shred the file.</summary>
    [Fact]
    public void A_multiline_message_stays_on_one_line()
    {
        var log = new RollingFileLog(_directory);

        log.Write(DateTimeOffset.UtcNow, LogLevel.Warning, "first\nsecond\r\nthird");

        var lines = File.ReadAllLines(log.Location!);
        Assert.Single(lines);
        Assert.Contains("first ⏎ second ⏎ third", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Files_older_than_the_retention_window_are_pruned()
    {
        Directory.CreateDirectory(_directory);
        var stale = Path.Combine(_directory, "studio-19990101.log");
        File.WriteAllText(stale, "ancient\n");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-(RollingFileLog.RetainedDays + 1)));

        new RollingFileLog(_directory).Write(DateTimeOffset.UtcNow, LogLevel.Info, "today");

        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void A_recent_file_is_kept()
    {
        Directory.CreateDirectory(_directory);
        var recent = Path.Combine(_directory, "studio-20000101.log");
        File.WriteAllText(recent, "yesterday\n");
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddDays(-1));

        new RollingFileLog(_directory).Write(DateTimeOffset.UtcNow, LogLevel.Info, "today");

        Assert.True(File.Exists(recent));
    }

    /// <summary>
    /// Logging must never take the app down - it is usually reporting a failure when it runs, and
    /// replacing a message with a crash is the worst possible trade. An undirectable path reports
    /// itself through a null Location rather than by throwing.
    /// </summary>
    [Fact]
    public void An_unusable_directory_disables_logging_rather_than_throwing()
    {
        // A path whose parent is a FILE cannot be created as a directory on any platform.
        var file = Path.Combine(_directory, "occupied");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(file, "");

        var log = new RollingFileLog(Path.Combine(file, "logs"));

        Assert.Null(log.Location);
        log.Write(DateTimeOffset.UtcNow, LogLevel.Error, "must not throw");
    }
}
