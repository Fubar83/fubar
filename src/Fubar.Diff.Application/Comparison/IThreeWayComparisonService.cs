using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Comparison;

namespace Fubar.Diff.Application.Comparison;

/// <summary>
/// Loads three files and merges them. The three-way counterpart of
/// <see cref="IFileComparisonService"/>, and deliberately a separate port rather than an overload on
/// it: a three-way result is a different shape with different consumers, and folding it in would give
/// every two-way caller a nullable third document to think about.
/// </summary>
public interface IThreeWayComparisonService
{
    /// <summary>
    /// Reads and merges three files.
    /// </summary>
    /// <param name="ancestorPath">The common ancestor.</param>
    /// <param name="leftPath">One edit.</param>
    /// <param name="rightPath">The other edit.</param>
    /// <param name="options">How to compare - the same options the two-way view uses.</param>
    Task<ThreeWayComparison> CompareFilesAsync(
        string ancestorPath,
        string leftPath,
        string rightPath,
        ComparisonOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs against documents already in memory, for when an option changed rather than the files.
    /// </summary>
    Task<ThreeWayComparison> RecompareAsync(
        ThreeWayComparison comparison,
        ComparisonOptions options,
        CancellationToken cancellationToken = default);
}
