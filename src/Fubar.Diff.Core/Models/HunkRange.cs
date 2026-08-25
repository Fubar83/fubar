namespace Fubar.Diff.Core.Models;

/// <summary>
/// The lines of the two original files that a hunk covers, 1-based and inclusive.
///
/// A side is null when the hunk contributes nothing there - a wholly inserted block covers no
/// left-hand lines. Callers must render that as "added" rather than inventing a range, which is the
/// whole reason this is nullable rather than defaulting to zero.
/// </summary>
public sealed record HunkRange(int? LeftStart, int? LeftEnd, int? RightStart, int? RightEnd);
