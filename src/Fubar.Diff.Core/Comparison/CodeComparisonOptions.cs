namespace Fubar.Diff.Core.Comparison;

/// <summary>
/// Rules that only make sense once the comparison knows it is looking at source code.
///
/// Kept as its own record rather than more flags on <see cref="ComparisonOptions"/> for the same reason
/// <see cref="Json.JsonComparisonOptions"/> is: these apply to a subset of files, and a reader of the
/// options should be able to see at a glance which ones can possibly be in play for the pair in front
/// of them. Both are off by default - a diff tool's first duty is to report what actually differs, and
/// a changed comment IS a change until someone says otherwise.
///
/// Which language the rules are read with is NOT here: it comes from the files' own extensions via
/// <see cref="Languages.LanguageDetector"/>. It is a fact about the documents, not a preference about
/// the comparison, and putting it here would invite a stored setting to outlive the files it was
/// chosen for.
/// </summary>
public sealed record CodeComparisonOptions
{
    /// <summary>Report everything, including comments and blank lines.</summary>
    public static CodeComparisonOptions Default { get; } = new();

    /// <summary>
    /// Treat comments as absent: a line whose only change is in a comment is not a difference, and a
    /// comment-only line that was added or removed is shown as ignored rather than as a change.
    ///
    /// The code on a line survives - <c>foo(); // note</c> against <c>foo();</c> compares equal, but
    /// <c>foo(); // note</c> against <c>bar(); // note</c> is still a difference.
    /// </summary>
    public bool IgnoreComments { get; init; }

    /// <summary>
    /// Treat added or removed blank lines as noise. Useful against a reformatted file, where vertical
    /// spacing moved everywhere and nothing else did.
    /// </summary>
    public bool IgnoreBlankLines { get; init; }

    /// <summary>Whether either rule is actually in play - the cheap check before doing any scanning.</summary>
    public bool Any => IgnoreComments || IgnoreBlankLines;
}
