using System;
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
    public static AlignedDocument Build(DiffResult result, DiffSide side) =>
        Build(result, side, 0, result.Lines.Count);

    /// <summary>
    /// Builds just <paramref name="count"/> rows starting at <paramref name="startIndex"/> - the
    /// detail pane showing one hunk in isolation.
    ///
    /// Filler rows inside the range are kept, exactly as in the full document. Dropping them would
    /// make the two sides of the excerpt different lengths and stop them lining up, which is the one
    /// thing a close-up of a single difference has to get right.
    ///
    /// The range is clamped rather than validated: it comes from a hunk, and a hunk can outlive the
    /// result it was computed from for a frame while a new comparison is being applied.
    /// </summary>
    public static AlignedDocument Build(DiffResult result, DiffSide side, int startIndex, int count)
    {
        var lines = result.Lines;
        var from = Math.Clamp(startIndex, 0, lines.Count);
        var to = Math.Clamp(from + Math.Max(count, 0), from, lines.Count);

        var builder = new StringBuilder();

        for (var i = from; i < to; i++)
        {
            var row = lines[i];

            if (i > from)
            {
                // Always '\n': this is a view document, and AvaloniaEdit normalises anyway. The file's
                // real terminator is preserved separately on TextDocument.LineEnding and reapplied on save.
                builder.Append('\n');
            }

            builder.Append((side == DiffSide.Left ? row.LeftText : row.RightText) ?? string.Empty);
        }

        // The text has to be built - an editor needs a document - but the per-line METADATA does not.
        // It is a pure function of the row it describes, so it is computed on access instead of stored:
        // the renderers ask for the fifty lines actually on screen, and a million-line comparison stops
        // paying for two million AlignedLines nobody looks at (about 110 MB of them, measured).
        return new AlignedDocument(builder.ToString(), new AlignedLineWindow(lines, side, from, to - from));
    }

    /// <summary>
    /// One side's metadata for one row: what <see cref="Build(DiffResult,DiffSide,int,int)"/> would
    /// have stored, derived on demand.
    /// </summary>
    internal static AlignedLine Project(DiffLine row, DiffSide side)
    {
        var kind = KindFor(row, side);

        return new AlignedLine(
            side == DiffSide.Left ? row.LeftNumber : row.RightNumber,
            kind,
            side == DiffSide.Left ? row.LeftSpans : row.RightSpans)
        {
            IsIgnored = row.IsIgnored,

            // This side's own answer. A row can be a move on one side and an ordinary change on
            // the other - two methods swapping places is exactly that - and the filler half of a
            // one-sided row has no text to have moved at all.
            IsMoved = kind != ChangeKind.Filler && row.IsMovedOn(side),
        };
    }

    /// <summary>
    /// Builds just the REAL rows a side has within the range, dropping filler entirely - for a
    /// vertically stacked close-up (old block, then new block) rather than a side-by-side one.
    ///
    /// <see cref="Build(DiffResult,DiffSide,int,int)"/> keeps fillers because side-by-side alignment
    /// depends on both columns having the same row count; stacking one side above the other has no
    /// such dependency; keeping fillers there would only insert pointless blank lines into an
    /// otherwise-compact block of text. A row missing on this side (an insertion has nothing on the
    /// left, a deletion nothing on the right) is simply not part of this side's block at all.
    /// </summary>
    public static AlignedDocument BuildCompact(DiffResult result, DiffSide side, int startIndex, int count)
    {
        var lines = result.Lines;
        var from = Math.Clamp(startIndex, 0, lines.Count);
        var to = Math.Clamp(from + Math.Max(count, 0), from, lines.Count);

        var builder = new StringBuilder();
        var meta = new List<AlignedLine>();

        for (var i = from; i < to; i++)
        {
            var row = lines[i];
            var isLeft = side == DiffSide.Left;

            var text = isLeft ? row.LeftText : row.RightText;
            if (text is null)
            {
                continue;
            }

            var number = isLeft ? row.LeftNumber : row.RightNumber;
            var spans = isLeft ? row.LeftSpans : row.RightSpans;

            if (meta.Count > 0)
            {
                builder.Append('\n');
            }

            builder.Append(text);

            // No KindFor remapping needed here: that exists purely to produce Filler on the side with
            // no content, which this method skips instead of keeping. A row that reaches this point
            // has real text on this side, so its own Kind is already correct as-is.
            meta.Add(new AlignedLine(number, row.Kind, spans)
            {
                IsIgnored = row.IsIgnored,
                IsMoved = row.IsMovedOn(side),
            });
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
/// <param name="Lines">
/// Per-line metadata, indexed by 0-based display line. Usually a lazy window over the comparison
/// rather than a stored array - see <see cref="AlignedLineWindow"/> - so indexing it is cheap and
/// holding it costs nothing per row.
/// </param>
public sealed record AlignedDocument(string Text, IReadOnlyList<AlignedLine> Lines);

/// <summary>
/// One side's per-line metadata, derived from the comparison on access rather than stored.
///
/// The rows are already in memory as <see cref="DiffLine"/>s and the projection to an
/// <see cref="AlignedLine"/> is pure, so storing the result as well was two more arrays the size of
/// the document, for data that is read fifty lines at a time. <see cref="AlignedLine"/> is a struct,
/// so indexing this allocates nothing at all.
///
/// The comparison it reads must not change underneath it, which is the rule everywhere else too: a
/// <see cref="DiffResult"/> is finished when it is built, and an edit produces a new one.
/// </summary>
public sealed class AlignedLineWindow(IReadOnlyList<DiffLine> rows, DiffSide side, int start, int count)
    : IReadOnlyList<AlignedLine>
{
    public int Count => count;

    public AlignedLine this[int index] => index >= 0 && index < count
        ? AlignedText.Project(rows[start + index], side)
        : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<AlignedLine> GetEnumerator()
    {
        for (var i = 0; i < count; i++)
        {
            yield return AlignedText.Project(rows[start + i], side);
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

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
public readonly record struct AlignedLine(int? SourceNumber, ChangeKind Kind, IReadOnlyList<CharSpan> Spans)
{
    /// <summary>True when this row differs only at ignored paths - drawn as a faint band, nothing more.</summary>
    public bool IsIgnored { get; init; }

    /// <summary>
    /// True when this row belongs to a three-way merge region both sides changed differently.
    ///
    /// A flag rather than a <see cref="ChangeKind"/> of its own, for the same reason
    /// <see cref="IsIgnored"/> is one: a conflicting row is still an ordinary changed row to every
    /// renderer, hunk-grouper and navigator in the two-way path, and adding a fifth kind would land it
    /// in every exhaustive switch over the four that exist. What it needs is one more thing DRAWN over
    /// it, which is exactly what a flag buys.
    /// </summary>
    public bool IsConflict { get; init; }

    /// <summary>
    /// True when this row is one half of a block that moved rather than being written or removed.
    ///
    /// A flag for the same reason as the two above, and drawn instead of the ordinary change tint
    /// rather than over it: the point of marking a move is that the reader can stop reading it.
    /// </summary>
    public bool IsMoved { get; init; }
}
