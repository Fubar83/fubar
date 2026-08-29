using System;
using System.Text;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Rendering;

/// <summary>
/// Flattens one column of a three-way merge into the text an editor shows, plus the per-line metadata
/// the renderers need.
///
/// Deliberately produces the SAME <see cref="AlignedDocument"/> the two-way view is built from. That is
/// the whole reason a third pane costs so little: <c>DiffEditorPane</c>, the character colouriser, the
/// line-number gutter and the tint renderer all take an <c>AlignedDocument</c> and none of them has to
/// learn what a merge is. It also means the filler discipline carries over unchanged - row <c>i</c> is
/// <c>ThreeWayResult.Lines[i]</c> in all THREE editors, so scroll sync stays a plain offset copy and a
/// region is one horizontal band across the window.
///
/// The same consequence applies as ever: this text is not any file's text. Saving goes through
/// <see cref="ThreeWayMergedDocument"/>, never through a pane.
/// </summary>
public static class ThreeWayAlignedText
{
    /// <summary>Builds the document text and per-line metadata for one column.</summary>
    public static AlignedDocument Build(ThreeWayResult result, MergeSide side)
    {
        var lines = result.Lines;
        var builder = new StringBuilder();
        var meta = new AlignedLine[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            var row = lines[i];

            if (i > 0)
            {
                builder.Append('\n');
            }

            builder.Append(row.TextOn(side) ?? string.Empty);

            meta[i] = new AlignedLine(row.NumberOn(side), KindFor(row, side), [])
            {
                IsConflict = row.Kind == MergeKind.Conflict,
            };
        }

        return new AlignedDocument(builder.ToString(), meta);
    }

    /// <summary>
    /// How a row should be tinted in one column.
    ///
    /// The reading this encodes: the ancestor column shows what a region WAS, and each edit column
    /// shows what it would become - so base rows in a region are tinted as removed and a side that
    /// actually moved is tinted as added. The important case is the third one: in a region only ONE
    /// side touched, the other side is untinted, because it still agrees with the ancestor and has
    /// nothing to answer for. Tinting all three columns of every region would turn the single question
    /// a merge asks - who moved? - back into something the reader has to work out for themselves.
    /// </summary>
    private static ChangeKind KindFor(ThreeWayLine row, MergeSide side)
    {
        if (row.TextOn(side) is null)
        {
            return ChangeKind.Filler;
        }

        if (row.Kind == MergeKind.Unchanged)
        {
            return ChangeKind.Unchanged;
        }

        return side == MergeSide.Base
            ? ChangeKind.Deleted
            : ChangedOn(row.Kind, side) ? ChangeKind.Inserted : ChangeKind.Unchanged;
    }

    /// <summary>Whether one side is among those that changed a region.</summary>
    private static bool ChangedOn(MergeKind kind, MergeSide side) => side switch
    {
        MergeSide.Left => kind is MergeKind.LeftOnly or MergeKind.BothSame or MergeKind.Conflict,
        MergeSide.Right => kind is MergeKind.RightOnly or MergeKind.BothSame or MergeKind.Conflict,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "the ancestor is not an edit"),
    };
}
