using System;
using System.Collections.Generic;
using System.Threading;

namespace Fubar.Diff.Core.Folders;

/// <summary>
/// Compares files against each other WITHIN one folder tree, pairing them by name.
///
/// The two-tree comparison answers "how do these two copies differ". This answers a different question
/// that has no second copy at all: a snapshot-testing run leaves <c>Thing.received.json</c> beside
/// <c>Thing.verified.json</c> in the same directory, and reviewing what changed means diffing the two
/// halves of every such pair. Before this, that meant picking two files by hand, one pair at a time.
///
/// It deliberately produces the SAME <see cref="FolderComparison"/> shape as the two-tree walk, with
/// both roots set to the one folder. That is not a convenience - it is what lets the entire window,
/// the filtering, and opening a pair work unchanged. The two halves of a pair have different FILE
/// NAMES rather than different roots, which is exactly what
/// <see cref="FolderEntry.LeftRelativePath"/> and its right-hand twin already carry.
/// </summary>
public static class LinkedFolderComparer
{
    /// <summary>
    /// Walks one tree and pairs up the files the rules link.
    /// </summary>
    /// <param name="root">The folder to look in.</param>
    /// <param name="scanner">How to read the filesystem.</param>
    /// <param name="options">Recursion, exclusions, and whether to read contents.</param>
    /// <param name="rules">The name markers that pair two files - see <see cref="LinkRule"/>.</param>
    /// <param name="progress">Names each pair as it is compared.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    public static FolderComparison Compare(
        string root,
        IFolderScanner scanner,
        FolderComparisonOptions options,
        IReadOnlyList<LinkRule> rules,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var entries = CompareDirectory(root, string.Empty, scanner, options, rules, progress, cancellationToken);

        return FolderComparison.Create(root, root, entries);
    }

    private static IReadOnlyList<FolderEntry> CompareDirectory(
        string root,
        string relativePath,
        IFolderScanner scanner,
        FolderComparisonOptions options,
        IReadOnlyList<LinkRule> rules,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = relativePath.Length == 0 ? root : scanner.Combine(root, relativePath);
        var entries = new List<FolderEntry>();

        entries.AddRange(PairFiles(root, relativePath, path, scanner, options, rules, progress, cancellationToken));

        if (options.Recursive)
        {
            foreach (var name in scanner.Directories(path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (NamePattern.MatchesAny(name, options.Exclude, options.IgnoreNameCase))
                {
                    continue;
                }

                var childPath = relativePath.Length == 0 ? name : relativePath + "/" + name;
                var children = CompareDirectory(root, childPath, scanner, options, rules, progress, cancellationToken);

                // A folder with no pairs in it anywhere has nothing to say. Unlike the two-tree walk,
                // where an empty folder is a real (if dull) answer, here it means "no snapshots live
                // here" - and showing it would bury the folders that do.
                if (children.Count > 0)
                {
                    entries.Add(new FolderEntry(
                        childPath, name, true, StatusOf(children), FolderEntry.NoSize, FolderEntry.NoSize, children)
                    {
                        LeftRelativePath = childPath,
                        RightRelativePath = childPath,
                    });
                }
            }
        }

        entries.Sort(static (a, b) => a.IsDirectory != b.IsDirectory
            ? a.IsDirectory ? -1 : 1
            : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

        return entries;
    }

    private static IEnumerable<FolderEntry> PairFiles(
        string root,
        string relativePath,
        string path,
        IFolderScanner scanner,
        FolderComparisonOptions options,
        IReadOnlyList<LinkRule> rules,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var comparer = options.IgnoreNameCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        // Grouped by the key the two halves share - the file name with the marker removed.
        var pairs = new Dictionary<string, (ScannedFile? Left, ScannedFile? Right)>(comparer);
        var order = new List<string>();

        foreach (var file in scanner.Files(path))
        {
            if (NamePattern.MatchesAny(file.Name, options.Exclude, options.IgnoreNameCase))
            {
                continue;
            }

            // A file no rule matches is simply not part of a pair. Unlike the two-tree walk there is
            // no "only on one side" to report: an ordinary source file sitting next to some snapshots
            // is not a difference, it is just a file.
            if (FileLinker.Match(file.Name, rules, options.IgnoreNameCase) is not { } link)
            {
                continue;
            }

            if (!pairs.TryGetValue(link.Key, out var pair))
            {
                order.Add(link.Key);
            }

            pairs[link.Key] = link.Side == LinkSide.Left
                ? (file, pair.Right)
                : (pair.Left, file);
        }

        order.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var key in order)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (left, right) = pairs[key];

            var leftPath = left is { } l ? Join(relativePath, l.Name) : null;
            var rightPath = right is { } r ? Join(relativePath, r.Name) : null;

            if (left is { } leftFile && right is { } rightFile)
            {
                progress?.Report(key);

                var same = leftFile.Length == rightFile.Length
                           && (!options.CompareContents
                               || scanner.ContentsEqual(
                                   scanner.Combine(root, leftPath!),
                                   scanner.Combine(root, rightPath!),
                                   cancellationToken));

                yield return new FolderEntry(
                    Join(relativePath, key),
                    key,
                    IsDirectory: false,
                    same ? FolderEntryStatus.Same : FolderEntryStatus.Different,
                    leftFile.Length,
                    rightFile.Length,
                    [])
                {
                    LeftRelativePath = leftPath,
                    RightRelativePath = rightPath,
                };
            }
            else if (left is { } onlyLeft)
            {
                // A baseline with nothing new beside it: the test did not run, or its output was
                // already accepted. Worth showing - a snapshot nobody produced any more is how a dead
                // test goes unnoticed.
                yield return new FolderEntry(
                    Join(relativePath, key), key, false, FolderEntryStatus.LeftOnly, onlyLeft.Length, FolderEntry.NoSize, [])
                {
                    LeftRelativePath = leftPath,
                };
            }
            else if (right is { } onlyRight)
            {
                // Output with no baseline: a brand new snapshot, waiting to be accepted for the first
                // time. This is the one a reviewer most wants to see.
                yield return new FolderEntry(
                    Join(relativePath, key), key, false, FolderEntryStatus.RightOnly, FolderEntry.NoSize, onlyRight.Length, [])
                {
                    RightRelativePath = rightPath,
                };
            }
        }
    }

    private static FolderEntryStatus StatusOf(IReadOnlyList<FolderEntry> children)
    {
        foreach (var child in children)
        {
            if (child.Status != FolderEntryStatus.Same)
            {
                return FolderEntryStatus.Different;
            }
        }

        return FolderEntryStatus.Same;
    }

    private static string Join(string relativePath, string name) =>
        relativePath.Length == 0 ? name : relativePath + "/" + name;
}
