using System.Collections.Generic;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Comparison;

/// <summary>
/// PORT. Finds the character ranges that differ WITHIN a pair of lines, so a modified row can
/// highlight the words that actually changed instead of tinting the whole line.
///
/// Separate from <see cref="IDiffEngine"/> on purpose. That one aligns whole documents and is fed
/// normalised comparison keys; this one runs afterwards, on the DISPLAY text of an already-matched
/// pair of lines, so its offsets address what the user can actually see.
/// </summary>
public interface IInlineDiffEngine
{
    /// <summary>
    /// Compares two lines and returns the differing ranges on each side.
    ///
    /// Implementations must return offsets into the strings they were given, must not emit
    /// zero-length spans, and must return spans in ascending order of <see cref="CharSpan.Start"/>.
    /// </summary>
    (IReadOnlyList<CharSpan> Left, IReadOnlyList<CharSpan> Right) DiffWithinLine(string left, string right);
}
