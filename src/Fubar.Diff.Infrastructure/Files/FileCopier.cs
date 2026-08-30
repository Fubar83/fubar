using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Folders;

namespace Fubar.Diff.Infrastructure.Files;

/// <summary>
/// <see cref="IFileCopier"/> over the local file system.
///
/// Holds no policy - which file goes where is decided by <see cref="FileCopyPlanner"/> - so what is
/// left here is the two things the filesystem needs and one it does not: refusing to copy a file over
/// itself. That happens in linked (one-folder) mode, where both roots are the same directory, and
/// <c>File.Copy</c>'s answer to it on some platforms is to truncate the file to nothing.
/// </summary>
public sealed class FileCopier : IFileCopier
{
    public Task CopyAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!File.Exists(source))
            {
                throw new FileCopyException(source, destination, "the source file no longer exists.");
            }

            if (SamePath(source, destination))
            {
                throw new FileCopyException(source, destination, "the source and destination are the same file.");
            }

            // The destination's folder may not exist at all - copying a left-only file into a tree that
            // never had that subdirectory is an ordinary case, not an error.
            if (Path.GetDirectoryName(destination) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            File.Copy(source, destination, overwrite: true);

            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new FileCopyException(source, destination, ex.Message, ex);
        }
    }

    /// <summary>
    /// Whether two paths name the same file, as far as can be told without opening them.
    ///
    /// Full paths compared case-insensitively, which is right on Windows and over-cautious elsewhere -
    /// the failure mode of being over-cautious is refusing a copy the user could redo with different
    /// names, and of being under-cautious is destroying a file.
    /// </summary>
    private static bool SamePath(string source, string destination) =>
        string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);
}
