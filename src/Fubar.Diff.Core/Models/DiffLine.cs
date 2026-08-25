using System.Collections.Generic;

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
    /// <summary>
    /// Character ranges within <see cref="LeftText"/> that differ from the right side. Only populated
    /// for <see cref="ChangeKind.Modified"/> rows - on a wholly inserted or deleted line the entire
    /// row is the change, so picking out characters within it would be noise.
    /// </summary>
    public IReadOnlyList<CharSpan> LeftSpans { get; init; } = [];

    /// <summary>Character ranges within <see cref="RightText"/> that differ from the left side.</summary>
    public IReadOnlyList<CharSpan> RightSpans { get; init; } = [];

    /// <summary>
    /// True when this row differs, but only at paths an ignore rule covers.
    ///
    /// Deliberately NOT part of <see cref="IsChange"/>: an ignored row forms no hunk, is not counted,
    /// and navigation steps past it. It exists only so a renderer can draw a faint band there -
    /// showing nothing at all would leave the reader unable to tell "these are the same" from "this
    /// is being ignored", which is exactly what they want to check after adding a rule.
    /// </summary>
    public bool IsIgnored { get; init; }

    /// <summary>True when this row represents a real difference rather than common context.</summary>
    public bool IsChange => Kind is ChangeKind.Inserted or ChangeKind.Deleted or ChangeKind.Modified;
}
