using System.Collections.Generic;

namespace Fubar.Diff.Core.Folders;

/// <summary>
/// A completed comparison of two directory trees: the entries as a tree, plus the counts a status line
/// needs. Built via <see cref="Create"/> so the counts can never disagree with the entries.
/// </summary>
public sealed class FolderComparison
{
    private FolderComparison(string leftRoot, string rightRoot, IReadOnlyList<FolderEntry> entries)
    {
        LeftRoot = leftRoot;
        RightRoot = rightRoot;
        Entries = entries;

        Count(entries, this);
    }

    /// <summary>The left tree's root.</summary>
    public string LeftRoot { get; }

    /// <summary>The right tree's root.</summary>
    public string RightRoot { get; }

    /// <summary>The top-level entries; directories carry their contents in <see cref="FolderEntry.Children"/>.</summary>
    public IReadOnlyList<FolderEntry> Entries { get; }

    /// <summary>How many FILES are identical on both sides.</summary>
    public int SameCount { get; private set; }

    /// <summary>How many files are present on both sides and differ.</summary>
    public int DifferentCount { get; private set; }

    /// <summary>How many files exist only on the left.</summary>
    public int LeftOnlyCount { get; private set; }

    /// <summary>How many files exist only on the right.</summary>
    public int RightOnlyCount { get; private set; }

    /// <summary>Every file worth attention: different, or present on one side only.</summary>
    public int DifferenceCount => DifferentCount + LeftOnlyCount + RightOnlyCount;

    /// <summary>True when the two trees hold the same files with the same contents.</summary>
    public bool AreIdentical => DifferenceCount == 0;

    /// <summary>Nothing compared yet.</summary>
    public static FolderComparison Empty { get; } = new(string.Empty, string.Empty, []);

    /// <summary>Builds a comparison and derives its counts from the entries.</summary>
    public static FolderComparison Create(string leftRoot, string rightRoot, IReadOnlyList<FolderEntry> entries) =>
        new(leftRoot, rightRoot, entries);

    /// <summary>
    /// Counts FILES only, walking into directories.
    ///
    /// A directory is not counted as a difference in its own right: it is different exactly when
    /// something inside it is, so counting both would report every change twice - once for the file and
    /// once for each folder above it - and a status line saying "12 differences" about 3 changed files
    /// is worse than no status line.
    /// </summary>
    private static void Count(IReadOnlyList<FolderEntry> entries, FolderComparison totals)
    {
        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                Count(entry.Children, totals);
                continue;
            }

            switch (entry.Status)
            {
                case FolderEntryStatus.Same:
                    totals.SameCount++;
                    break;

                case FolderEntryStatus.Different:
                    totals.DifferentCount++;
                    break;

                case FolderEntryStatus.LeftOnly:
                    totals.LeftOnlyCount++;
                    break;

                default:
                    totals.RightOnlyCount++;
                    break;
            }
        }
    }
}
