using System.Collections.Generic;
using System.Text;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Rendering;

/// <summary>
/// Flattens one side of a <see cref="DiffResult"/> into the exact text an editor should display, plus
/// the per-line metadata the renderers need.
///
/// The central invariant of the two-editor view lives here: **editor line i corresponds to
/// <c>DiffResult.Lines[i]</c>**, on BOTH sides. Filler rows become empty lines rather than being
/// skipped, which is what keeps the panes aligned - and, because both sides then have identical line
/// counts, lets scroll sync be a plain vertical-offset copy instead of a line-mapping problem.
///
/// The consequence to respect everywhere else: the editor's text is NOT the file's text. Saving and
/// merging must go through <c>MergedDocument</c>, never read the editor back.
/// </summary>
public static class AlignedText
{
    /// <summary>Builds the document text and per-line metadata for one side.</summary>
    public static AlignedDocument Build(DiffResult result, DiffSide side)
    {
        var lines = result.Lines;
        var builder = new StringBuilder();
        var meta = new AlignedLine[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            var row = lines[i];
            var isLeft = side == DiffSide.Left;

            var text = (isLeft ? row.LeftText : row.RightText) ?? string.Empty;
            var number = isLeft ? row.LeftNumber : row.RightNumber;
            var spans = isLeft ? row.LeftSpans : row.RightSpans;

            if (i > 0)
            {
                // Always '\n': this is a view document, and AvaloniaEdit normalises anyway. The file's
                // real terminator is preserved separately on TextDocument.LineEnding and reapplied on save.
                builder.Append('\n');
            }

            builder.Append(text);
            meta[i] = new AlignedLine(number, KindFor(row, side), spans);
        }

        return new AlignedDocument(builder.ToString(), meta);
    }

    /// <summary>
    /// How this row should be tinted on the given side. A row is styled per SIDE, not once: a deleted
    /// row is tinted on the left and shows an inert filler on the right, and vice versa.
    /// </summary>
    private static ChangeKind KindFor(DiffLine row, DiffSide side) => row.Kind switch
    {
        ChangeKind.Deleted => side == DiffSide.Left ? ChangeKind.Deleted : ChangeKind.Filler,
        ChangeKind.Inserted => side == DiffSide.Right ? ChangeKind.Inserted : ChangeKind.Filler,
        var kind => kind,
    };
}

/// <summary>One side's flattened document: the text an editor shows, and metadata per display line.</summary>
/// <param name="Text">The full document text, lines joined with '\n'.</param>
/// <param name="Lines">Per-line metadata, indexed by 0-based display line.</param>
public sealed record AlignedDocument(string Text, IReadOnlyList<AlignedLine> Lines);

/// <summary>
/// What the renderers need to know about one display line.
/// </summary>
/// <param name="SourceNumber">
/// The line's 1-based number in the ORIGINAL file, or null for a filler. The editor must show this
/// rather than its own line numbering - fillers would otherwise shift every number after the first
/// insertion, and the numbers would stop matching the file on disk.
/// </param>
/// <param name="Kind">How to tint the line on this side.</param>
/// <param name="Spans">Character ranges to highlight within the line; empty unless modified.</param>
public sealed record AlignedLine(int? SourceNumber, ChangeKind Kind, IReadOnlyList<CharSpan> Spans);
