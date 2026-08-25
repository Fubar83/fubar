namespace Fubar.Diff.Core.Models;

/// <summary>
/// A range of characters within a single line that differs from the other side, so a modified line can
/// highlight the words that actually changed instead of tinting the whole row.
///
/// Offsets address the line's DISPLAY text - the document line as the user sees it - not the
/// normalised comparison key. With "ignore whitespace" on, trimming shifts every offset, so spans
/// computed against a key would highlight the wrong characters. See
/// <c>FileComparisonService.WithInlineSpans</c>.
/// </summary>
/// <param name="Start">0-based index of the first character, into the line's display text.</param>
/// <param name="Length">Number of characters. Never zero - an empty span has nothing to draw.</param>
/// <param name="Kind">
/// What happened to these characters: <see cref="ChangeKind.Inserted"/> or
/// <see cref="ChangeKind.Deleted"/>. Unchanged runs are not emitted.
/// </param>
public sealed record CharSpan(int Start, int Length, ChangeKind Kind)
{
    /// <summary>Index just past the last character in the span.</summary>
    public int End => Start + Length;
}
