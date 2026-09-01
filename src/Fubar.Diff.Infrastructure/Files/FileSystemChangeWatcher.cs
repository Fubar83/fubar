using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Fubar.Diff.Core.Files;

namespace Fubar.Diff.Infrastructure.Files;

/// <summary>
/// <see cref="IFileChangeWatcher"/> over <see cref="FileSystemWatcher"/>.
///
/// Two things make this more than a thin wrapper, and both come from how editors actually save. They
/// rarely write a file in place: the common pattern is to write a temporary file and rename it over the
/// target, which arrives as a delete, a create and a rename in quick succession, sometimes with a
/// change or two either side. So events are COALESCED behind a short timer, and the watcher is set up
/// on the containing DIRECTORY rather than the file - a watcher bound to a file that gets replaced
/// stops seeing anything at all, which is the failure mode that makes naive implementations of this
/// work in testing and not in use.
/// </summary>
public sealed class FileSystemChangeWatcher : IFileChangeWatcher
{
    /// <summary>
    /// How long the files must be quiet before the change is announced.
    ///
    /// Long enough to swallow a write-and-rename, short enough that saving in an editor and looking
    /// back at the diff feels immediate. A comparison also takes time, so anything much shorter would
    /// mean re-comparing the intermediate states of a single save.
    /// </summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Lock _gate = new();

    private Timer? _debounce;
    private bool _disposed;

    public event EventHandler? Changed;

    public void Watch(IReadOnlyList<string> paths)
    {
        Stop();

        if (_disposed)
        {
            return;
        }

        // Grouped by directory: one watcher per folder handles both files of a comparison when they
        // live together, which is the common case.
        var groups = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => (Directory: Path.GetDirectoryName(Path.GetFullPath(path)), Name: Path.GetFileName(path)))
            .Where(entry => !string.IsNullOrEmpty(entry.Directory) && Directory.Exists(entry.Directory))
            .GroupBy(entry => entry.Directory!, StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            foreach (var group in groups)
            {
                try
                {
                    var watcher = new FileSystemWatcher(group.Key)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    };

                    foreach (var name in group.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        watcher.Filters.Add(name);
                    }

                    watcher.Changed += OnFileSystemEvent;
                    watcher.Created += OnFileSystemEvent;
                    watcher.Deleted += OnFileSystemEvent;
                    watcher.Renamed += OnFileSystemEvent;

                    // A watcher that cannot keep up drops events rather than throwing; the buffer only
                    // has to survive one save, since everything is coalesced anyway.
                    watcher.InternalBufferSize = 16 * 1024;
                    watcher.EnableRaisingEvents = true;

                    _watchers.Add(watcher);
                }
                catch (Exception)
                {
                    // A directory that cannot be watched - a network share, a permissions problem, a
                    // path that vanished between the check and the call - costs the user automatic
                    // refresh and nothing else. Failing the comparison over it would be absurd.
                }
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnFileSystemEvent;
                watcher.Created -= OnFileSystemEvent;
                watcher.Deleted -= OnFileSystemEvent;
                watcher.Renamed -= OnFileSystemEvent;
                watcher.Dispose();
            }

            _watchers.Clear();

            _debounce?.Dispose();
            _debounce = null;
        }
    }

    /// <summary>
    /// Restarts the quiet timer. Every event pushes the announcement further out, so a burst of them
    /// produces exactly one - which is the contract.
    /// </summary>
    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _debounce ??= new Timer(_ => Announce(), null, Timeout.Infinite, Timeout.Infinite);
            _debounce.Change(Quiet, Timeout.InfiniteTimeSpan);
        }
    }

    private void Announce()
    {
        if (_disposed)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
