using System.Collections.Generic;

namespace Fubar.Diff.Core.Folders;

/// <summary>What a comparison found about one entry present in one or both trees.</summary>
public enum FolderEntryStatus
{
    /// <summary>Present on both sides and identical.</summary>
    Same,

    /// <summary>Present on both sides, and the contents differ.</summary>
    Different,

    /// <summary>Only in the left tree.</summary>
    LeftOnly,

    /// <summary>Only in the right tree.</summary>
    RightOnly,
}

/// <summary>
/// One file or directory, as seen by a comparison of two trees.
///
/// Keyed by its path RELATIVE to the two roots, because that is the only thing the two sides have in
/// common - the absolute paths differ by construction, and pairing on anything else would mean deciding
/// that <c>a\b\c.txt</c> under one root corresponds to something differently named under the other,
/// which is a guess no comparison should make.
/// </summary>
/// <param name="RelativePath">Path from either root, e.g. <c>src/app.cs</c>. Empty for a root itself.</param>
/// <param name="Name">The entry's own name, for display.</param>
/// <param name="IsDirectory">Whether this is a directory rather than a file.</param>
/// <param name="Status">What the comparison found.</param>
/// <param name="LeftSize">Size in bytes on the left, or -1 where it is absent or a directory.</param>
/// <param name="RightSize">Size in bytes on the right, or -1.</param>
/// <param name="Children">Contents, for a directory. Empty for a file.</param>
public sealed record FolderEntry(
    string RelativePath,
    string Name,
    bool IsDirectory,
    FolderEntryStatus Status,
    long LeftSize,
    long RightSize,
    IReadOnlyList<FolderEntry> Children)
{
    /// <summary>
    /// This entry's path under the LEFT root, spelled the way that tree spells it, or null when the
    /// left tree does not have it.
    ///
    /// Separate from <see cref="RelativePath"/> because the two sides can spell the same entry
    /// differently: names are paired case-insensitively by default, so <c>README.md</c> on one side
    /// pairs with <c>readme.md</c> on the other. Building both paths from one spelling works on a
    /// case-insensitive filesystem and fails to open the file on a case-sensitive one - which is
    /// exactly the sort of bug that only appears on someone else's machine.
    /// </summary>
    public string? LeftRelativePath { get; init; }

    /// <summary>This entry's path under the right root, as that tree spells it, or null.</summary>
    public string? RightRelativePath { get; init; }

    /// <summary>Size is meaningless for a directory and for a side that has no such entry.</summary>
    public const long NoSize = -1;

    /// <summary>True when this entry is worth a user's attention - anything but an identical pair.</summary>
    public bool IsDifference => Status != FolderEntryStatus.Same;

    /// <summary>True when both trees have this entry, so a file comparison can be opened on it.</summary>
    public bool IsOnBothSides => Status is FolderEntryStatus.Same or FolderEntryStatus.Different;

    /// <summary>A file present on both sides - the only kind of entry a two-file diff can be opened for.</summary>
    public bool CanCompare => !IsDirectory && IsOnBothSides;
}
