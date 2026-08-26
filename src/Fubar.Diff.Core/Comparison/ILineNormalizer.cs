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

    /// <summary>
    /// Pretty-prints <paramref name="lines"/> if they parse as JSON, unconditionally - independent of
    /// <see cref="ComparisonOptions.NormalizeStructure"/>, which stays an XML-focused, opt-in toggle.
    /// Returns the input unchanged when it is not valid JSON, exactly like <see cref="Canonicalize"/>.
    ///
    /// Exists so a semantic JSON comparison can put both sides into a common format before the text
    /// differ ever sees them. The semantic pass already treats formatting as insignificant, but that
    /// promise means nothing if alignment - which happens first, on the raw text - has nothing sane to
    /// line up: a minified file diffed against a pretty one has almost no matching lines, so the text
    /// differ marks nearly everything as one giant replacement before the semantic pass gets a say.
    /// </summary>
    IReadOnlyList<string> CanonicalizeJson(IReadOnlyList<string> lines);
}
