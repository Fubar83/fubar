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

    /// <summary>
    /// Identifies the block <see cref="LeftText"/> belongs to when this line left the file here and
    /// turned up somewhere else on the right - both halves of one move share an id. Null otherwise.
    ///
    /// Like <see cref="IsIgnored"/>, a mark rather than a <see cref="ChangeKind"/>: the row really is
    /// deleted or modified, and a move is only ever extra information about WHY.
    ///
    /// Per side because the two halves of a move are rarely on the same row, and on a swap they are
    /// both - a row pairing `void Helper()` against `void Run()` has a left half that moved DOWN and a
    /// right half that moved UP, and one flag on the row could only describe one of them.
    /// </summary>
    public int? LeftMoveId { get; init; }

    /// <summary>Identifies the block <see cref="RightText"/> arrived from. See <see cref="LeftMoveId"/>.</summary>
    public int? RightMoveId { get; init; }

    /// <summary>
    /// True when either side of this row is part of a block that moved rather than changed.
    ///
    /// Conditioned on <see cref="IsChange"/> because a later pass can downgrade a row to unchanged -
    /// a comment-only insertion under the code rules, a formatting-only one under the semantic
    /// filter - and a row that is no longer reported as a difference must not be reported as a move
    /// either.
    /// </summary>
    public bool IsMoved => (LeftMoveId ?? RightMoveId) is not null && IsChange;

    /// <summary>Whether the given side of this row is part of a moved block.</summary>
    public bool IsMovedOn(DiffSide side) =>
        (side == DiffSide.Left ? LeftMoveId : RightMoveId) is not null && IsChange;

    /// <summary>True when this row represents a real difference rather than common context.</summary>
    public bool IsChange => Kind is ChangeKind.Inserted or ChangeKind.Deleted or ChangeKind.Modified;
}
