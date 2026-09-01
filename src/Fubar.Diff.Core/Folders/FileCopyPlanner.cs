using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fubar.Diff.Core.Folders;

/// <summary>Which way a copy goes.</summary>
public enum CopyDirection
{
    /// <summary>Take the left file and put it on the right.</summary>
    ToRight,

    /// <summary>Take the right file and put it on the left.</summary>
    ToLeft,
}

/// <summary>
/// A copy that is about to happen, resolved to two absolute paths.
/// </summary>
/// <param name="SourcePath">The file to read.</param>
/// <param name="DestinationPath">Where it will be written.</param>
/// <param name="Overwrites">
/// True when a file already exists at the destination. The single most important thing to say before
/// asking someone to confirm: creating a file and replacing one are very different acts, and the
/// second is the one that loses work.
/// </param>
public sealed record FileCopy(string SourcePath, string DestinationPath, bool Overwrites);

/// <summary>
/// PORT. Copies one file over another.
///
/// The narrowest possible interface, deliberately. This is the only thing in the app that can destroy
/// a file the user did not ask it to write, so it does exactly one thing, takes two fully-resolved
/// paths, and holds no policy - every decision about WHICH paths is made in
/// <see cref="FileCopyPlanner"/>, where it can be tested without a disk.
/// </summary>
public interface IFileCopier
{
    /// <summary>Copies <paramref name="source"/> to <paramref name="destination"/>, replacing it if present.</summary>
    /// <exception cref="FileCopyException">The copy failed.</exception>
    Task CopyAsync(string source, string destination, CancellationToken cancellationToken = default);
}

/// <summary>A copy could not be completed. Carries a message fit to show the user.</summary>
public sealed class FileCopyException : System.Exception
{
    public FileCopyException(string source, string destination, string reason, System.Exception? inner = null)
        : base($"Could not copy '{source}' to '{destination}': {reason}", inner)
    {
        Source = source;
        Destination = destination;
        Reason = reason;
    }

    public new string Source { get; }

    public string Destination { get; }

    /// <summary>Why, phrased for a person rather than a log.</summary>
    public string Reason { get; }
}

/// <summary>
/// Works out what a copy of one folder entry would actually do.
///
/// Separated from the copying itself because every mistake this feature could make is a mistake about
/// WHICH FILE, and that is a decision, not an I/O call. A folder comparison pairs entries by relative
/// path; turning "this row, that direction" back into two absolute paths has to get several things
/// right at once, and every one of them is testable here without touching a disk.
///
/// The deliberate limits: files only, never directories, and never a delete. Copying a whole tree and
/// "make this side match" - which means removing what the other side does not have - are the two
/// operations that turn a mistake into lost work, and neither is offered. Beyond Compare has them;
/// this earns them later, if at all.
/// </summary>
public static class FileCopyPlanner
{
    /// <summary>
    /// The copy this entry and direction describe, or null when there is nothing sensible to do.
    ///
    /// Null for a directory, for an identical pair (nothing to copy), and for a direction with no
    /// source - copying left-to-right needs a file on the LEFT, and a right-only entry has none.
    /// </summary>
    public static FileCopy? Plan(
        FolderEntry entry,
        string leftRoot,
        string rightRoot,
        CopyDirection direction)
    {
        if (entry.IsDirectory || entry.Status == FolderEntryStatus.Same)
        {
            return null;
        }

        var (sourceRoot, sourceRelative, destinationRoot, destinationRelative) = direction == CopyDirection.ToRight
            ? (leftRoot, entry.LeftRelativePath, rightRoot, entry.RightRelativePath)
            : (rightRoot, entry.RightRelativePath, leftRoot, entry.LeftRelativePath);

        if (sourceRelative is null)
        {
            return null;
        }

        // Where the destination side already HAS this entry, its own spelling wins - names pair
        // case-insensitively, so writing the source's spelling would leave `README.md` beside
        // `readme.md` on a case-sensitive filesystem instead of replacing it. Where it does not have
        // one, the source's spelling is the only one there is.
        //
        // In linked (one-folder) mode this is what makes "accept this snapshot" work at all: both roots
        // are the same folder and the two halves differ by NAME, so the destination path has to come
        // from the other side's own relative path rather than from the source's.
        var destination = destinationRelative ?? sourceRelative;

        return new FileCopy(
            Combine(sourceRoot, sourceRelative),
            Combine(destinationRoot, destination),
            Overwrites: destinationRelative is not null);
    }

    /// <summary>
    /// Every copy in one direction for a whole subtree, in the order they should run.
    ///
    /// Used for a directory row, where the user means "everything under here". Directories themselves
    /// are still never copied as such - what comes back is the list of FILES, so the confirmation can
    /// name how many and how many of them replace something.
    /// </summary>
    public static IReadOnlyList<FileCopy> PlanAll(
        FolderEntry entry,
        string leftRoot,
        string rightRoot,
        CopyDirection direction)
    {
        var copies = new List<FileCopy>();
        Collect(entry, leftRoot, rightRoot, direction, copies);

        return copies;
    }

    private static void Collect(
        FolderEntry entry,
        string leftRoot,
        string rightRoot,
        CopyDirection direction,
        List<FileCopy> into)
    {
        if (Plan(entry, leftRoot, rightRoot, direction) is { } copy)
        {
            into.Add(copy);
        }

        foreach (var child in entry.Children)
        {
            Collect(child, leftRoot, rightRoot, direction, into);
        }
    }

    /// <summary>
    /// Joins a root and a '/'-separated relative path into a path the filesystem will accept.
    ///
    /// Done here rather than left to the caller because it is part of naming the file correctly, which
    /// is the whole job of this class.
    /// </summary>
    private static string Combine(string root, string relativePath) =>
        System.IO.Path.Combine(root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
}
