using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Folders;

namespace Fubar.Diff.Application.Folders;

/// <summary>
/// Compares two directory trees, off the calling thread.
///
/// Thin over <see cref="FolderComparer"/>, and thin on purpose - what it adds is the thread and the
/// cancellation, which is precisely what a walk over thousands of files needs and precisely what a
/// pure function should not know about.
/// </summary>
public interface IFolderComparisonService
{
    /// <summary>
    /// Walks both trees.
    /// </summary>
    /// <param name="leftRoot">The left tree's root.</param>
    /// <param name="rightRoot">The right tree's root.</param>
    /// <param name="options">What to include and how carefully to compare.</param>
    /// <param name="progress">
    /// Names each pair as it is compared, for a status line. Reported from the background thread, so a
    /// UI listener must marshal - <c>Progress&lt;T&gt;</c> does that for you.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the walk. Not optional in spirit: a comparison of two large trees is the one operation
    /// here a user will genuinely want to abandon half way through.
    /// </param>
    Task<FolderComparison> CompareAsync(
        string leftRoot,
        string rightRoot,
        FolderComparisonOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks ONE tree, pairing files against each other by name - see <see cref="LinkedFolderComparer"/>.
    ///
    /// Returns the same shape as <see cref="CompareAsync"/>, with both roots set to the one folder, so
    /// every consumer of a folder comparison works on it unchanged.
    /// </summary>
    /// <param name="root">The folder to look in.</param>
    /// <param name="options">Recursion, exclusions, and whether to read contents.</param>
    /// <param name="rules">The name markers that pair two files.</param>
    /// <param name="progress">Names each pair as it is compared.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    Task<FolderComparison> CompareLinkedAsync(
        string root,
        FolderComparisonOptions options,
        IReadOnlyList<LinkRule> rules,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
