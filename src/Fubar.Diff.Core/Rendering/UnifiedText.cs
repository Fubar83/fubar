using System.Collections.Generic;
using System.Text;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Rendering;

/// <summary>
/// One side-by-side comparison flattened into a SINGLE document, patch style: removed lines followed
/// by added ones, with shared context between them.
///
/// <para>
/// This is the one place the codebase's central invariant does not hold, and it is worth being explicit
/// about why. Everywhere else, editor line <c>i</c> is <c>DiffResult.Lines[i]</c> on both sides - which
/// is what makes scroll sync an offset copy and lets every renderer address rows directly. A unified
/// view cannot have that: one modified row becomes TWO lines, a filler becomes none, so the mapping is
/// no longer the identity. Rather than weaken the invariant for everyone, this produces its own
/// document and carries the mapping back explicitly in <see cref="UnifiedDocument.SourceRows"/> and
/// <see cref="UnifiedDocument.Hunks"/>. Nothing else has to change, and the side-by-side view keeps the
/// guarantee it was built on.
/// </para>
/// </summary>
public static class UnifiedText
{
    /// <summary>
    /// Flattens a result into one document.
    ///
    /// Within a hunk, all removals come before all additions, which is what a patch looks like and what
    /// anyone who reads diffs is expecting. The alternative - alternating removed and added line by
    /// line - reads better for a one-line edit and much worse for a block of five, and the block is the
    /// case that needs the help.
    /// </summary>
    public static UnifiedDocument Build(DiffResult result)
    {
        var builder = new StringBuilder();
        var meta = new List<AlignedLine>(result.Lines.Count);
        var sourceRows = new List<int>(result.Lines.Count);
        var hunks = new List<DiffHunk>(result.Hunks.Count);

        var cursor = 0;
        var i = 0;

        while (i < result.Lines.Count)
        {
            while (cursor < result.Hunks.Count && result.Hunks[cursor].EndIndex < i)
            {
                cursor++;
            }

            if (cursor < result.Hunks.Count && i == result.Hunks[cursor].StartIndex)
            {
                var hunk = result.Hunks[cursor];
                var start = meta.Count;

                Emit(result, hunk, DiffSide.Left, builder, meta, sourceRows);
                Emit(result, hunk, DiffSide.Right, builder, meta, sourceRows);

                // A hunk always contributes at least one line - it is a run of changed rows, and a
                // changed row has content on at least one side - so the range is never empty.
                hunks.Add(new DiffHunk(start, meta.Count - 1));

                i = hunk.EndIndex + 1;
                continue;
            }

            AppendContext(result.Lines[i], i, builder, meta, sourceRows);
            i++;
        }

        return new UnifiedDocument(new AlignedDocument(builder.ToString(), meta), hunks, sourceRows);
    }

    /// <summary>
    /// Emits one side of a hunk: its removals, or its additions.
    ///
    /// A modified row contributes to BOTH passes - its left text as a removal and its right text as an
    /// addition - which is exactly what "modified" means in a patch. The character spans go with the
    /// side they were computed against, so a one-word change still highlights as one word.
    /// </summary>
    private static void Emit(
        DiffResult result,
        DiffHunk hunk,
        DiffSide side,
        StringBuilder builder,
        List<AlignedLine> meta,
        List<int> sourceRows)
    {
        var last = hunk.EndIndex < result.Lines.Count ? hunk.EndIndex : result.Lines.Count - 1;

        for (var i = hunk.StartIndex < 0 ? 0 : hunk.StartIndex; i <= last; i++)
        {
            var row = result.Lines[i];

            var text = side == DiffSide.Left ? row.LeftText : row.RightText;
            if (text is null)
            {
                continue;
            }

            Append(
                builder,
                meta,
                sourceRows,
                text,
                side == DiffSide.Left ? row.LeftNumber : row.RightNumber,
                side == DiffSide.Left ? ChangeKind.Deleted : ChangeKind.Inserted,
                side == DiffSide.Left ? row.LeftSpans : row.RightSpans,
                row.IsIgnored,
                i);
        }
    }

    /// <summary>
    /// Emits a row that is not part of any hunk - shared context, or a row an ignore rule downgraded.
    ///
    /// The right side is preferred where both exist because they are equal there by definition; where
    /// only one exists the row survived a downgrade (see <c>CodeLineFilter</c>) and that side is the
    /// only text there is. A row with neither is a pure filler, which exists to keep two columns
    /// aligned and has nothing to say in a single one.
    /// </summary>
    private static void AppendContext(
        DiffLine row,
        int sourceRow,
        StringBuilder builder,
        List<AlignedLine> meta,
        List<int> sourceRows)
    {
        if ((row.RightText ?? row.LeftText) is not { } text)
        {
            return;
        }

        Append(
            builder,
            meta,
            sourceRows,
            text,
            row.RightNumber ?? row.LeftNumber,
            ChangeKind.Unchanged,
            [],
            row.IsIgnored,
            sourceRow);
    }

    private static void Append(
        StringBuilder builder,
        List<AlignedLine> meta,
        List<int> sourceRows,
        string text,
        int? number,
        ChangeKind kind,
        IReadOnlyList<CharSpan> spans,
        bool isIgnored,
        int sourceRow)
    {
        if (meta.Count > 0)
        {
            builder.Append('\n');
        }

        builder.Append(text);
        meta.Add(new AlignedLine(number, kind, spans) { IsIgnored = isIgnored });
        sourceRows.Add(sourceRow);
    }
}

/// <summary>
/// A unified document plus the two things a caller needs to relate it back to the comparison it came
/// from, since its rows no longer line up with <c>DiffResult.Lines</c> one for one.
/// </summary>
/// <param name="Document">The text and per-line metadata, for the same renderers the panes use.</param>
/// <param name="Hunks">
/// The same hunks, in the same order, expressed in UNIFIED row indices - so navigation, the current-hunk
/// marker and the diff map keep working by index without knowing anything about the flattening.
/// </param>
/// <param name="SourceRows">
/// For each unified row, the <c>DiffResult.Lines</c> index it came from. Two unified rows share an
/// index when a modified row was split into a removal and an addition, which is what lets a click land
/// back on the right hunk.
/// </param>
public sealed record UnifiedDocument(
    AlignedDocument Document,
    IReadOnlyList<DiffHunk> Hunks,
    IReadOnlyList<int> SourceRows)
{
    /// <summary>Nothing loaded yet.</summary>
    public static UnifiedDocument Empty { get; } = new(new AlignedDocument(string.Empty, []), [], []);
}
