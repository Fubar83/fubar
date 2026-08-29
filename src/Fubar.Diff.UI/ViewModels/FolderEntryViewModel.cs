using System.Collections.Generic;
using Fubar.Diff.Core.Folders;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// One row of the folder comparison tree.
///
/// A view model rather than binding the domain record directly, for two reasons that are not
/// cosmetic. The tree is FILTERED - hiding identical files is what makes a comparison of two real
/// checkouts readable - and a filtered tree is a different shape from the one the comparison produced,
/// so it has to be built rather than styled away. And Avalonia's <c>Classes</c> is not bindable, so a
/// status has to reach the view as a bool per class, which is the codebase's existing convention.
/// </summary>
public sealed class FolderEntryViewModel
{
    private FolderEntryViewModel(FolderEntry entry, IReadOnlyList<FolderEntryViewModel> children)
    {
        Entry = entry;
        Children = children;
    }

    /// <summary>The entry this row shows.</summary>
    public FolderEntry Entry { get; }

    /// <summary>Visible children, already filtered.</summary>
    public IReadOnlyList<FolderEntryViewModel> Children { get; }

    public string Name => Entry.Name;

    public bool IsDirectory => Entry.IsDirectory;

    /// <summary>Directories are weighted, so the tree's structure reads before its contents do.</summary>
    public Avalonia.Media.FontWeight NameWeight =>
        IsDirectory ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;

    /// <summary>
    /// Directories start expanded. A folder comparison is opened to find what differs, and a tree that
    /// must be unfolded before it says anything is a tree that has answered nothing - especially once
    /// identical files are hidden, when what is left is exactly the answer.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>Only a file present on both sides can be opened as a two-file diff.</summary>
    public bool CanCompare => Entry.CanCompare;

    // Style classes. Avalonia cannot bind Classes directly, so each state is its own bool.
    public bool IsSame => Entry.Status == FolderEntryStatus.Same;

    public bool IsDifferent => Entry.Status == FolderEntryStatus.Different;

    public bool IsLeftOnly => Entry.Status == FolderEntryStatus.LeftOnly;

    public bool IsRightOnly => Entry.Status == FolderEntryStatus.RightOnly;

    /// <summary>What happened, in words - the column a reader actually scans.</summary>
    public string StatusText => Entry.Status switch
    {
        FolderEntryStatus.Same => Entry.IsDirectory ? string.Empty : "same",
        FolderEntryStatus.Different => "differs",
        FolderEntryStatus.LeftOnly => "left only",
        _ => "right only",
    };

    /// <summary>Sizes, or nothing for a directory and for a side that has no such entry.</summary>
    public string LeftSizeText => Describe(Entry.LeftSize);

    public string RightSizeText => Describe(Entry.RightSize);

    private static string Describe(long size) => size < 0 ? string.Empty : Format(size);

    /// <summary>
    /// Human sizes rather than raw bytes. A folder comparison is skimmed, and "1.2 MB" is read at a
    /// glance where "1258291" has to be counted.
    /// </summary>
    private static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    /// <summary>
    /// Builds the visible tree.
    ///
    /// A directory survives the filter when anything inside it does, which is why this is a bottom-up
    /// rebuild rather than a per-row predicate: whether a folder is worth showing is a fact about its
    /// contents, and cannot be decided while looking only at the folder.
    /// </summary>
    /// <param name="entries">The comparison's entries.</param>
    /// <param name="showSame">Whether identical files, and folders holding only identical files, are shown.</param>
    public static IReadOnlyList<FolderEntryViewModel> Build(IReadOnlyList<FolderEntry> entries, bool showSame)
    {
        var rows = new List<FolderEntryViewModel>(entries.Count);

        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                var children = Build(entry.Children, showSame);

                // An empty folder is only worth a row when nothing is being hidden; otherwise it is a
                // folder whose entire contents were filtered out, and showing it says nothing.
                if (children.Count > 0 || (showSame && entry.Children.Count == 0))
                {
                    rows.Add(new FolderEntryViewModel(entry, children));
                }

                continue;
            }

            if (showSame || entry.IsDifference)
            {
                rows.Add(new FolderEntryViewModel(entry, []));
            }
        }

        return rows;
    }
}
