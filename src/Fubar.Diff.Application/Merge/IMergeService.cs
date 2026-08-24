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
}
