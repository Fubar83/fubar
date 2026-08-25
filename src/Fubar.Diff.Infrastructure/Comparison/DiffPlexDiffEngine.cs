using System.Collections.Generic;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Infrastructure.Comparison;

/// <summary>
/// <see cref="IDiffEngine"/> over DiffPlex's side-by-side builder.
///
/// DiffPlex already emits the row alignment we want, including the blank placeholder rows opposite
/// insertions and deletions, so this is mostly a translation of its model into ours. It is a
/// translation worth having: it keeps DiffPlex's types out of Core and the app, so swapping the
/// algorithm later is a change to this one file.
///
/// Note that the caller passes COMPARISON KEYS, not display text: whitespace and case folding have
/// already been applied by the normalizer, so there is nothing left for the engine to ignore.
/// </summary>
public sealed class DiffPlexDiffEngine : IDiffEngine
{
    private readonly ISideBySideDiffBuilder _builder = new SideBySideDiffBuilder(new Differ());

    public IReadOnlyList<DiffLine> Align(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        ComparisonOptions options)
    {
        var model = _builder.BuildDiffModel(Join(left), Join(right));

        var leftLines = model.OldText.Lines;
        var rightLines = model.NewText.Lines;

        // The two sides always come back the same length - that is the point of the side-by-side
        // builder - but take the longer one rather than assuming, so a future DiffPlex change
        // degrades into a missing row instead of an IndexOutOfRangeException.
        var count = leftLines.Count > rightLines.Count ? leftLines.Count : rightLines.Count;

        var rows = new List<DiffLine>(count);
        for (var i = 0; i < count; i++)
        {
            var l = i < leftLines.Count ? leftLines[i] : null;
            var r = i < rightLines.Count ? rightLines[i] : null;

            rows.Add(new DiffLine(
                LeftNumber: NumberOf(l),
                // Text is projected back onto the real document by FileComparisonService; carrying
                // the key through here would leak the normalised form into the UI.
                LeftText: null,
                RightNumber: NumberOf(r),
                RightText: null,
                Kind: ToChangeKind(l?.Type, r?.Type)));
        }

        return rows;
    }

    private static string Join(IReadOnlyList<string> lines) => string.Join("\n", lines);

    /// <summary>
    /// DiffPlex leaves Position null on placeholder ("imaginary") rows, which is exactly the filler
    /// case, so this maps cleanly onto our nullable line numbers.
    /// </summary>
    private static int? NumberOf(DiffPiece? piece) =>
        piece is null || piece.Type == ChangeType.Imaginary ? null : piece.Position;

    /// <summary>
    /// Collapses DiffPlex's per-side change types into one row-level verdict. A row is Modified when
    /// both sides carry content that differs; Inserted/Deleted when only one side has a line at all.
    /// </summary>
    private static ChangeKind ToChangeKind(ChangeType? left, ChangeType? right)
    {
        var leftMissing = left is null or ChangeType.Imaginary;
        var rightMissing = right is null or ChangeType.Imaginary;

        if (leftMissing && rightMissing)
        {
            return ChangeKind.Filler;
        }

        if (leftMissing)
        {
            return ChangeKind.Inserted;
        }

        if (rightMissing)
        {
            return ChangeKind.Deleted;
        }

        return left == ChangeType.Modified || right == ChangeType.Modified
            ? ChangeKind.Modified
            : ChangeKind.Unchanged;
    }
}
