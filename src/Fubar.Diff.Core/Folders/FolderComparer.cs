using System;
using System.Collections.Generic;
using System.Threading;

namespace Fubar.Diff.Core.Folders;

/// <summary>
/// Walks two directory trees together and reports what differs.
///
/// The algorithm is a merge of two sorted listings, level by level: at each directory, take the union
/// of the names on both sides, and for each one decide whether it is on the left only, the right only,
/// or both - recursing where both sides have a directory. That is all a folder comparison is, and
/// writing it as a pure walk over a scanner port means every decision it makes - pairing, exclusion,
/// how a directory's status follows from its contents - is testable with no directories on disk.
///
/// It reports a TREE rather than a flat list. A flat list is easier to build and worse to use: the
/// question a user has is "what changed in this project", and the answer is shaped like the project.
/// </summary>
public static class FolderComparer
{
    /// <summary>
    /// Compares two trees.
    /// </summary>
    /// <param name="leftRoot">The left tree's root path.</param>
    /// <param name="rightRoot">The right tree's root path.</param>
    /// <param name="scanner">How to read the filesystem.</param>
    /// <param name="options">What to include and how carefully to compare.</param>
    /// <param name="progress">Reports each file as it is compared, for a status line.</param>
    /// <param name="cancellationToken">Cancels a long walk - a deep tree can take a while.</param>
    public static FolderComparison Compare(
        string leftRoot,
        string rightRoot,
        IFolderScanner scanner,
        FolderComparisonOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var entries = CompareDirectory(
            leftRoot,
            rightRoot,
            leftRelative: string.Empty,
            rightRelative: string.Empty,
            scanner,
            options,
            progress,
            cancellationToken);

        return FolderComparison.Create(leftRoot, rightRoot, entries);
    }

    /// <summary>
    /// Compares one directory of each tree.
    ///
    /// Each side carries its OWN relative path rather than sharing one. They are usually identical and
    /// occasionally are not: names pair case-insensitively by default, so a directory spelled
    /// <c>Src</c> on one side and <c>src</c> on the other is one entry with two spellings, and reading
    /// either side through the other's spelling fails on a case-sensitive filesystem.
    /// </summary>
    private static IReadOnlyList<FolderEntry> CompareDirectory(
        string leftRoot,
        string rightRoot,
        string leftRelative,
        string rightRelative,
        IFolderScanner scanner,
        FolderComparisonOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var leftPath = Resolve(scanner, leftRoot, leftRelative);
        var rightPath = Resolve(scanner, rightRoot, rightRelative);

        var comparer = options.IgnoreNameCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var entries = new List<FolderEntry>();

        entries.AddRange(CompareFiles(
            leftRoot, rightRoot, leftRelative, rightRelative, leftPath, rightPath, scanner, options, comparer, progress, cancellationToken));

        entries.AddRange(CompareSubdirectories(
            leftRoot, rightRoot, leftRelative, rightRelative, leftPath, rightPath, scanner, options, comparer, progress, cancellationToken));

        // Directories first, then files, each alphabetically - the ordering every file manager uses, so
        // it is the one a reader can scan without thinking about it.
        entries.Sort(static (a, b) => a.IsDirectory != b.IsDirectory
            ? a.IsDirectory ? -1 : 1
            : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

        return entries;
    }

    private static IEnumerable<FolderEntry> CompareFiles(
        string leftRoot,
        string rightRoot,
        string leftRelative,
        string rightRelative,
        string leftPath,
        string rightPath,
        IFolderScanner scanner,
        FolderComparisonOptions options,
        StringComparer comparer,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var left = Index(scanner.Files(leftPath), options, comparer);
        var right = Index(scanner.Files(rightPath), options, comparer);

        foreach (var name in Union(left.Keys, right.Keys, comparer))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var onLeft = left.TryGetValue(name, out var leftFile);
            var onRight = right.TryGetValue(name, out var rightFile);

            // Each side's own spelling: leftFile.Name and rightFile.Name are what that tree actually
            // calls the file, which is not always what the other one calls it.
            var leftChild = onLeft ? Join(leftRelative, leftFile.Name) : null;
            var rightChild = onRight ? Join(rightRelative, rightFile.Name) : null;
            var displayPath = leftChild ?? rightChild!;

            if (onLeft && onRight)
            {
                progress?.Report(displayPath);

                var same = leftFile.Length == rightFile.Length
                           && (!options.CompareContents
                               || scanner.ContentsEqual(
                                   scanner.Combine(leftRoot, leftChild!),
                                   scanner.Combine(rightRoot, rightChild!),
                                   cancellationToken));

                yield return new FolderEntry(
                    displayPath,
                    leftFile.Name,
                    IsDirectory: false,
                    same ? FolderEntryStatus.Same : FolderEntryStatus.Different,
                    leftFile.Length,
                    rightFile.Length,
                    [])
                {
                    LeftRelativePath = leftChild,
                    RightRelativePath = rightChild,
                };
            }
            else if (onLeft)
            {
                yield return new FolderEntry(
                    displayPath, leftFile.Name, false, FolderEntryStatus.LeftOnly, leftFile.Length, FolderEntry.NoSize, [])
                {
                    LeftRelativePath = leftChild,
                };
            }
            else
            {
                yield return new FolderEntry(
                    displayPath, rightFile.Name, false, FolderEntryStatus.RightOnly, FolderEntry.NoSize, rightFile.Length, [])
                {
                    RightRelativePath = rightChild,
                };
            }
        }
    }

    private static IEnumerable<FolderEntry> CompareSubdirectories(
        string leftRoot,
        string rightRoot,
        string leftRelative,
        string rightRelative,
        string leftPath,
        string rightPath,
        IFolderScanner scanner,
        FolderComparisonOptions options,
        StringComparer comparer,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!options.Recursive)
        {
            yield break;
        }

        // Keyed by the paired name, valued by each side's own spelling of it.
        var left = NamesByKey(scanner.Directories(leftPath), options, comparer);
        var right = NamesByKey(scanner.Directories(rightPath), options, comparer);

        foreach (var name in Union(left.Keys, right.Keys, comparer))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var onLeft = left.TryGetValue(name, out var leftName);
            var onRight = right.TryGetValue(name, out var rightName);

            var leftChild = Join(leftRelative, leftName ?? name);
            var rightChild = Join(rightRelative, rightName ?? name);
            var displayPath = onLeft ? leftChild : rightChild;

            // Always both roots, even for a directory only one side has: the scanner reports an
            // unreadable or absent path as empty, so the missing side contributes nothing and every
            // child comes back one-sided by itself. That is the answer wanted - someone comparing two
            // checkouts wants to see WHAT is in the folder only one of them has, not an opaque
            // "only here".
            var children = CompareDirectory(
                leftRoot, rightRoot, leftChild, rightChild, scanner, options, progress, cancellationToken);

            var status = onLeft && onRight
                ? StatusOf(children)
                : onLeft ? FolderEntryStatus.LeftOnly : FolderEntryStatus.RightOnly;

            yield return new FolderEntry(
                displayPath,
                onLeft ? leftName! : rightName!,
                true,
                status,
                FolderEntry.NoSize,
                FolderEntry.NoSize,
                children)
            {
                LeftRelativePath = onLeft ? leftChild : null,
                RightRelativePath = onRight ? rightChild : null,
            };
        }
    }

    /// <summary>
    /// Names indexed for pairing, each mapped to the spelling its own tree uses. Under a
    /// case-insensitive pairing the key and the value differ whenever the two trees disagree about
    /// capitalisation, and it is the VALUE that can be opened.
    /// </summary>
    private static Dictionary<string, string> NamesByKey(
        IReadOnlyList<string> names,
        FolderComparisonOptions options,
        StringComparer comparer)
    {
        var index = new Dictionary<string, string>(names.Count, comparer);

        foreach (var name in names)
        {
            if (!NamePattern.MatchesAny(name, options.Exclude, options.IgnoreNameCase))
            {
                index[name] = name;
            }
        }

        return index;
    }

    /// <summary>
    /// A directory's own status follows from its contents: identical only when everything inside it is.
    /// An empty directory present on both sides is <see cref="FolderEntryStatus.Same"/>, which is true
    /// and is what lets a filtered view hide it.
    /// </summary>
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

    private static Dictionary<string, ScannedFile> Index(
        IReadOnlyList<ScannedFile> files,
        FolderComparisonOptions options,
        StringComparer comparer)
    {
        var index = new Dictionary<string, ScannedFile>(files.Count, comparer);

        foreach (var file in files)
        {
            if (!NamePattern.MatchesAny(file.Name, options.Exclude, options.IgnoreNameCase))
            {
                index[file.Name] = file;
            }
        }

        return index;
    }

    /// <summary>
    /// Every name on either side, once, in a stable order. Sorted rather than left in filesystem order
    /// because two directories do not enumerate in the same order on every platform, and a comparison
    /// whose row order depends on that is one nobody can screenshot.
    /// </summary>
    private static List<string> Union(
        IEnumerable<string> left,
        IEnumerable<string> right,
        StringComparer comparer)
    {
        var all = new HashSet<string>(left, comparer);
        all.UnionWith(right);

        var ordered = new List<string>(all);
        ordered.Sort(StringComparer.OrdinalIgnoreCase);

        return ordered;
    }

    private static string Join(string relativePath, string name) =>
        relativePath.Length == 0 ? name : relativePath + "/" + name;

    private static string Resolve(IFolderScanner scanner, string root, string relativePath) =>
        relativePath.Length == 0 ? root : scanner.Combine(root, relativePath);
}
