using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Application.Merge;

/// <summary>
/// Turns per-hunk decisions into a saved file. Thin by design - the merge rules themselves are pure
/// and live in <see cref="MergedDocument"/>; what this adds is the write, and the choice of which
/// document's format to preserve.
/// </summary>
public interface IMergeService
{
    /// <summary>
    /// Writes the merged result. Saves to <paramref name="targetPath"/> when given (Save As), and to
    /// the base document's own path otherwise.
    /// </summary>
    Task<string> SaveAsync(
        FileComparison comparison,
        MergeState state,
        DiffSide baseSide,
        string? targetPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The merged content as it would be saved, without writing anything - for a preview, or to test
    /// the merge rules without touching a disk.
    /// </summary>
    string Preview(FileComparison comparison, MergeState state, DiffSide baseSide);

    /// <summary>
    /// Writes the result of a three-way merge.
    /// </summary>
    /// <param name="destination">
    /// Whose path and file format the result takes. NOT where the content comes from - that is decided
    /// entirely by <paramref name="state"/> and the merge itself. Right by default in the UI ("mine",
    /// the file in front of you), but the ancestor is a legitimate choice when the merge is being
    /// produced for somewhere else entirely.
    /// </param>
    /// <param name="targetPath">Somewhere other than the destination document's own path (Save As).</param>
    Task<string> SaveThreeWayAsync(
        ThreeWayComparison comparison,
        ThreeWayMergeState state,
        MergeSide destination,
        string? targetPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>The merged three-way content as it would be saved, without writing anything.</summary>
    string PreviewThreeWay(ThreeWayComparison comparison, ThreeWayMergeState state, MergeSide destination);
}
