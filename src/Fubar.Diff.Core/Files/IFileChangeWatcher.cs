using System;
using System.Collections.Generic;

namespace Fubar.Diff.Core.Files;

/// <summary>
/// PORT. Tells a comparison when the files it is showing have changed underneath it.
///
/// The workflow this exists for is the ordinary one: the diff is open on a second monitor while the
/// edit happens in an IDE. Without it, every save is followed by a trip back to the diff window to
/// press Compare, and a stale diff that looks current is worse than no diff - it is the one state in
/// which a tool actively misleads.
/// </summary>
public interface IFileChangeWatcher : IDisposable
{
    /// <summary>
    /// Starts watching exactly these paths, replacing whatever was being watched before. Paths that do
    /// not exist are skipped rather than rejected - a comparison can be set up before both files are.
    /// </summary>
    void Watch(IReadOnlyList<string> paths);

    /// <summary>Stops watching everything, without disposing the watcher.</summary>
    void Stop();

    /// <summary>
    /// Raised after the watched files have settled.
    ///
    /// Implementations MUST coalesce: a single save produces several filesystem events - editors
    /// commonly write a temporary file and rename it over the target, which is a delete, a create and
    /// a rename - and re-running a comparison once per event would be both wasteful and visibly
    /// flickery. The event says "something changed, and has stopped changing", not "here is one
    /// change".
    ///
    /// There is no guarantee about which thread this arrives on; a UI listener must marshal.
    /// </summary>
    event EventHandler? Changed;
}
