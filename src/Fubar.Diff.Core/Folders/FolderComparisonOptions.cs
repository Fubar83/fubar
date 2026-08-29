using System.Collections.Generic;

namespace Fubar.Diff.Core.Folders;

/// <summary>How two directory trees should be compared.</summary>
public sealed record FolderComparisonOptions
{
    /// <summary>Sensible defaults: the whole tree, by content, skipping what nobody wants to diff.</summary>
    public static FolderComparisonOptions Default { get; } = new();

    /// <summary>Descend into subdirectories.</summary>
    public bool Recursive { get; init; } = true;

    /// <summary>
    /// Match names case-insensitively when pairing the two sides.
    ///
    /// On by default because the platforms people run this on mostly are: pairing <c>README.md</c> with
    /// <c>readme.md</c> as two unrelated files is wrong on Windows and macOS, and merely unusual on
    /// Linux. Turn it off for a tree that genuinely relies on case to tell files apart.
    /// </summary>
    public bool IgnoreNameCase { get; init; } = true;

    /// <summary>
    /// Names never descended into or compared - see <see cref="NamePattern"/> for the syntax.
    ///
    /// Defaulted rather than empty, because the first thing anyone comparing two checkouts wants is for
    /// <c>.git</c>, <c>bin</c>, <c>obj</c> and <c>node_modules</c> not to be in the answer. A tree full
    /// of build output buries the handful of real differences, which is the failure that makes a folder
    /// diff useless rather than merely noisy.
    /// </summary>
    public IReadOnlyList<string> Exclude { get; init; } =
        [".git", ".svn", ".hg", "node_modules", "bin", "obj", ".vs", ".idea"];

    /// <summary>
    /// Compare file CONTENTS rather than just their size.
    ///
    /// On by default, and the only setting that gives a trustworthy answer: two files of the same
    /// length are routinely different, and reporting them as identical is the one mistake a comparison
    /// must not make. Turning it off is for a first pass over a very large tree, where "same size" is
    /// a useful filter and everyone understands it is a guess.
    /// </summary>
    public bool CompareContents { get; init; } = true;
}
