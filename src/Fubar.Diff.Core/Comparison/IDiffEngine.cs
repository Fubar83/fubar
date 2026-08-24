using System.Collections.Generic;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Comparison;

/// <summary>
/// PORT. Aligns two sequences of lines into side-by-side rows. The implementation owns the diff
/// algorithm; Core owns the shape of the answer, which is what keeps the algorithm swappable.
/// </summary>
public interface IDiffEngine
{
    /// <summary>
    /// Aligns <paramref name="left"/> against <paramref name="right"/>. Implementations must emit a
    /// <see cref="ChangeKind.Filler"/> opposite every insertion and deletion so the two sides stay
    /// row-aligned, and must preserve the original text - normalisation is a comparison concern
    /// applied by <see cref="ILineNormalizer"/>, not something the engine bakes into its output.
    /// </summary>
    IReadOnlyList<DiffLine> Align(IReadOnlyList<string> left, IReadOnlyList<string> right, ComparisonOptions options);
}
