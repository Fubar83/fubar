namespace Fubar.Diff.Core.Models;

/// <summary>
/// One row of a side-by-side diff: the text on each side plus what changed. Either side may be a
/// <see cref="ChangeKind.Filler"/> with no text and no number, which is what keeps the two panes
/// aligned across insertions and deletions.
/// </summary>
/// <param name="LeftNumber">1-based line number in the left document, or null for a filler.</param>
/// <param name="LeftText">Left-hand text, or null for a filler.</param>
/// <param name="RightNumber">1-based line number in the right document, or null for a filler.</param>
/// <param name="RightText">Right-hand text, or null for a filler.</param>
/// <param name="Kind">How this row differs.</param>
public sealed record DiffLine(
    int? LeftNumber,
    string? LeftText,
    int? RightNumber,
    string? RightText,
    ChangeKind Kind)
{
    /// <summary>True when this row represents a real difference rather than common context.</summary>
    public bool IsChange => Kind is ChangeKind.Inserted or ChangeKind.Deleted or ChangeKind.Modified;
}
