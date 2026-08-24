using System.Collections.Generic;

namespace Fubar.Diff.Core.Comparison;

/// <summary>
/// PORT. Produces the text used to decide whether two lines MATCH. What the user sees always comes
/// from the document itself - normalisation only ever affects comparison, never display, which is why
/// this returns keys rather than rewriting the document in place.
/// </summary>
public interface ILineNormalizer
{
    /// <summary>
    /// Returns the comparison key for <paramref name="line"/> under <paramref name="options"/>.
    /// Must be deterministic, side-effect free, and one key per input line - the caller maps keys back
    /// to original lines positionally.
    /// </summary>
    string ToComparisonKey(string line, ComparisonOptions options);

    /// <summary>
    /// Canonicalises a whole document when <see cref="ComparisonOptions.NormalizeStructure"/> is set
    /// (e.g. re-indenting JSON, so a pure reformat is not a difference; property order is preserved).
    /// Unlike <see cref="ToComparisonKey"/> this MAY change the line count, and its output IS shown to
    /// the user - comparing canonical forms only makes sense if you can see them. Returns the input
    /// unchanged when the option is off or the content does not parse as a structured format.
    /// </summary>
    IReadOnlyList<string> Canonicalize(IReadOnlyList<string> lines, ComparisonOptions options);
}
