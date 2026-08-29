using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Fubar.Diff.Core.Folders;

namespace Fubar.Diff.Infrastructure.Folders;

/// <summary>
/// <see cref="IFolderScanner"/> over the real filesystem.
///
/// Every listing swallows its exceptions and returns nothing. That is not laziness: a tree of any size
/// contains something the current user cannot open - a locked folder, a permissions boundary, a
/// junction pointing somewhere gone - and refusing to compare two checkouts because one folder in one
/// of them is unreadable would be a far worse answer than comparing the rest. The one place this
/// deliberately does NOT apply is content comparison, where an unreadable file is reported as a
/// DIFFERENCE rather than skipped, because claiming two files match when one could not be opened is
/// the single answer a comparison must never give.
/// </summary>
public sealed class FileSystemFolderScanner : IFolderScanner
{
    /// <summary>
    /// How much of each file to hold in memory while comparing. Large enough that the syscall overhead
    /// disappears, small enough that comparing two large files does not itself become the problem.
    /// </summary>
    private const int BufferSize = 64 * 1024;

    public IReadOnlyList<string> Directories(string path)
    {
        try
        {
            var names = new List<string>();

            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                names.Add(Path.GetFileName(directory));
            }

            return names;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    public IReadOnlyList<ScannedFile> Files(string path)
    {
        try
        {
            var files = new List<ScannedFile>();

            foreach (var file in Directory.EnumerateFiles(path))
            {
                try
                {
                    files.Add(new ScannedFile(Path.GetFileName(file), new FileInfo(file).Length));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A file that vanished between listing and measuring. It is not there now, so it
                    // is not part of the answer.
                }
            }

            return files;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>
    /// Byte-for-byte, after a length shortcut.
    ///
    /// Not a hash: hashing reads both files in full, always, where a comparison can stop at the first
    /// differing byte - and for two files that differ, which is the interesting case, that is usually
    /// immediately. Hashing only wins when comparing one file against many, which is not what this does.
    /// </summary>
    public bool ContentsEqual(string leftPath, string rightPath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var left = File.OpenRead(leftPath);
            using var right = File.OpenRead(rightPath);

            if (left.Length != right.Length)
            {
                return false;
            }

            var leftBuffer = new byte[BufferSize];
            var rightBuffer = new byte[BufferSize];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var leftRead = ReadBlock(left, leftBuffer);
                var rightRead = ReadBlock(right, rightBuffer);

                if (leftRead != rightRead)
                {
                    return false;
                }

                if (leftRead == 0)
                {
                    return true;
                }

                if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
                {
                    return false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable is a difference worth showing, not a match.
            return false;
        }
    }

    /// <summary>Fills the buffer as far as the stream allows; a short read is not the end of the file.</summary>
    private static int ReadBlock(Stream stream, byte[] buffer)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    public string Combine(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
