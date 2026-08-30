using System.Threading;
using System.Threading.Tasks;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;

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
    /// Compares two strings already in memory - no file system involved.
    ///
    /// This is what lets a host that is not a file-diff tool use the same engine and the same view:
    /// API Studio compares an existing request against the version an OpenAPI spec would import, and
    /// two HTTP response bodies, neither of which is on disk.
    /// </summary>
    /// <param name="leftText">Left-hand content.</param>
    /// <param name="rightText">Right-hand content.</param>
    /// <param name="options">Comparison options, including the text/semantic mode.</param>
    /// <param name="leftLabel">A name for the left side, shown where a file name would be.</param>
    /// <param name="rightLabel">A name for the right side.</param>
    Task<FileComparison> CompareTextAsync(
        string leftText,
        string rightText,
        ComparisonOptions options,
        string leftLabel = "left",
        string rightLabel = "right",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares two documents held in memory, keeping each one's path and format.
    ///
    /// This is what an EDIT re-runs through. <see cref="Recompare"/> cannot serve: it reuses the
    /// documents the comparison already holds, which is the whole point of it, and an edit's whole
    /// point is that one of them has changed. Reading from disk would be worse still - it would throw
    /// away what the user just typed.
    /// </summary>
    Task<FileComparison> CompareDocumentsAsync(
        TextDocument left,
        TextDocument right,
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
