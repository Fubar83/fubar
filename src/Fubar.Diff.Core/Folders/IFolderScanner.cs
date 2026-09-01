using System.Collections.Generic;
using System.Threading;

namespace Fubar.Diff.Core.Folders;

/// <summary>One entry as the filesystem reports it.</summary>
/// <param name="Name">The entry's own name, without any path.</param>
/// <param name="Length">Size in bytes.</param>
public readonly record struct ScannedFile(string Name, long Length);

/// <summary>
/// PORT. The filesystem, as a folder comparison needs to see it.
///
/// Narrow on purpose. Everything the walk decides - pairing, exclusion, status, ordering - is domain
/// policy and lives in <see cref="FolderComparer"/>, where it can be tested against a fake with no
/// directories on disk at all. What cannot be faked away is listing and reading, which is all that is
/// here.
/// </summary>
public interface IFolderScanner
{
    /// <summary>
    /// Subdirectory names directly under <paramref name="path"/>, or empty when it cannot be read.
    ///
    /// Empty rather than throwing: a tree of any size will contain something the current user cannot
    /// open, and refusing to compare two checkouts because one of them has a locked folder in it would
    /// be a worse answer than comparing the rest.
    /// </summary>
    IReadOnlyList<string> Directories(string path);

    /// <summary>Files directly under <paramref name="path"/>, with their sizes. Empty when unreadable.</summary>
    IReadOnlyList<ScannedFile> Files(string path);

    /// <summary>
    /// Whether two files have identical contents.
    ///
    /// Implementations should shortcut on length, and must treat an unreadable file as NOT equal - a
    /// file that cannot be read is a difference worth showing, and claiming two files match when one of
    /// them could not even be opened is the one answer a comparison must never give.
    /// </summary>
    bool ContentsEqual(string leftPath, string rightPath, CancellationToken cancellationToken = default);

    /// <summary>Joins a root and a relative path the way this filesystem spells it.</summary>
    string Combine(string root, string relativePath);
}
