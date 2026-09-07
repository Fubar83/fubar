namespace Fubar.Studio.Core.Diagnostics;

/// <summary>How much the reader needs to care. Three levels, not more: the strip is glanced at, and a
/// scale finer than "fine / look at this / something failed" is one nobody reads.</summary>
public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Somewhere a log entry is kept after the window forgets it.
///
/// <para>A port rather than a direct file write because the entries are the only record of what the app
/// did, and "send us your log" is unanswerable without one - the strip held its entries in memory and
/// dropped them on exit. Kept in Core so the view model can write to it without the UI layer knowing
/// where the file is.</para>
/// </summary>
public interface ILogSink
{
    void Write(DateTimeOffset timestamp, LogLevel level, string message);

    /// <summary>Where the entries are being kept, for the About panel to show and for a bug report to
    /// name. Null when nothing is being written - a full disk or a read-only profile must not stop the
    /// app, so failing to log is survivable and has to be visible.</summary>
    string? Location { get; }
}
