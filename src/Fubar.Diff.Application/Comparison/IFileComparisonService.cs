using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Comparison;

namespace Fubar.Diff.Application.Comparison;

/// <summary>
/// The one use case the app is built around: given two files and a set of options, produce a diff
/// ready to render. View models depend on this, not on <see cref="IDiffEngine"/> directly.
/// </summary>
public interface IFileComparisonService
{
    /// <summary>Reads both files and compares them.</summary>
    Task<FileComparison> CompareFilesAsync(
        string leftPath,
        string rightPath,
        ComparisonOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs the comparison over already-loaded documents. Toggling "ignore whitespace" should not
    /// re-read from disk - that would be slower and would silently pick up edits made meanwhile.
    /// </summary>
    FileComparison Recompare(FileComparison comparison, ComparisonOptions options);

    /// <summary>
    /// <see cref="Recompare"/>, off the calling thread. Diffing is CPU-bound and grows with file size,
    /// so a UI caller should use this and keep the window responsive.
    /// </summary>
    Task<FileComparison> RecompareAsync(
        FileComparison comparison,
        ComparisonOptions options,
        CancellationToken cancellationToken = default);
}
