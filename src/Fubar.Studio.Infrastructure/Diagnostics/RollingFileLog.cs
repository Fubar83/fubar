using Fubar.Studio.Core.Diagnostics;

namespace Fubar.Studio.Infrastructure.Diagnostics;

/// <summary>
/// <see cref="ILogSink"/> writing one file per day to <c>%AppData%/Fubar/logs/studio-yyyyMMdd.log</c>
/// (or the platform equivalent), keeping <see cref="RetainedDays"/> of them.
///
/// <para>Deliberately dull: line-per-entry, UTC, no structure. Its job is to survive until someone asks
/// "what happened", which the in-memory strip could not do - it dropped everything on exit, so a
/// failure reported the next morning had no evidence behind it at all.</para>
///
/// <para><b>Never throws.</b> A full disk or a locked profile must not take the application down, and
/// certainly must not take down whatever was being logged at the time - which is usually an error.
/// <see cref="Location"/> goes null instead, so the About panel can say logging is not working rather
/// than pointing at a file that is not being written.</para>
/// </summary>
public sealed class RollingFileLog : ILogSink
{
    /// <summary>Days of history kept. Long enough to cover "it did it on Friday", short enough that
    /// nobody has to think about the size of it.</summary>
    public const int RetainedDays = 7;

    private readonly object _gate = new();
    private readonly string? _directory;
    private DateOnly _lastPruned;

    public RollingFileLog()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fubar", "logs"))
    {
    }

    public RollingFileLog(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _directory = directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _directory = null;
        }
    }

    public string? Location => _directory is null ? null : PathFor(DateTimeOffset.UtcNow);

    public void Write(DateTimeOffset timestamp, LogLevel level, string message)
    {
        if (_directory is null)
        {
            return;
        }

        // One writer at a time: several view models share the sink, and interleaved appends produce
        // torn lines - which is exactly the log nobody can read afterwards.
        lock (_gate)
        {
            try
            {
                Prune(timestamp);

                var line = $"{timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff}Z  {level.ToString().ToUpperInvariant(),-7}  {Flatten(message)}";
                File.AppendAllText(PathFor(timestamp), line + System.Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Losing a log line is a nuisance; throwing from inside a catch block that was
                // reporting a failure would replace a message with a crash.
            }
        }
    }

    /// <summary>A message spanning lines would break the one-entry-per-line contract the file is read
    /// with, so newlines become a visible marker rather than a real break.</summary>
    private static string Flatten(string message) =>
        message.ReplaceLineEndings(" ⏎ ");

    private string PathFor(DateTimeOffset timestamp) =>
        Path.Combine(_directory!, $"studio-{timestamp.UtcDateTime:yyyyMMdd}.log");

    private void Prune(DateTimeOffset now)
    {
        // Once a day is plenty; this runs on the logging path and must stay close to free.
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (_lastPruned == today)
        {
            return;
        }

        _lastPruned = today;
        var cutoff = now.UtcDateTime.AddDays(-RetainedDays);

        foreach (var file in Directory.EnumerateFiles(_directory!, "studio-*.log"))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Another instance may hold it open. It will be pruned next time.
                }
            }
        }
    }
}
