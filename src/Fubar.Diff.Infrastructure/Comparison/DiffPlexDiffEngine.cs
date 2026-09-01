using System.Collections.Generic;
using DiffPlex;
using DiffPlex.Chunkers;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Infrastructure.Comparison;

/// <summary>
/// <see cref="IDiffEngine"/> over DiffPlex's line differ.
///
/// DiffPlex reports edits as blocks - "delete 3 lines here, insert 2 there" - and this turns them
/// into the row alignment the panes need, including the blank placeholder rows opposite insertions
/// and deletions. Keeping that translation here keeps DiffPlex's types out of Core and the app, so
/// swapping the algorithm later is a change to this one file.
///
/// Note that the caller passes COMPARISON KEYS, not display text: whitespace and case folding have
/// already been applied by the normalizer, so there is nothing left for the engine to ignore.
/// </summary>
public sealed class DiffPlexDiffEngine : IDiffEngine
{
    private readonly IDiffer _differ = new Differ();

    /// <summary>
    /// Line count above which the pair is broken into segments first - see
    /// <see cref="SegmentedLineAligner"/>.
    ///
    /// Below it, nothing is trimmed or split and the engine sees the whole document exactly as it
    /// always has, so every ordinary comparison keeps byte-identical alignment. Above it, measured on
    /// a 1,000,000-line pair: 15.5 s -> 1.4 s where the changes are scattered (50,000 of them, which
    /// is what makes an LCS engine work hardest), and 1.3 s -> 0.12 s for a single localised change.
    /// The first number is the one that mattered - that is not a slow diff, it is a frozen window.
    ///
    /// Segmenting changes WHICH of several equally-minimal alignments comes back, in the same way
    /// (and the same cases) that `ChangeGroupSlider` already normalises afterwards. That is a real
    /// trade, and it is only made where the alternative is no usable diff at all.
    /// </summary>
    internal const int SegmentedFrom = 10_000;

    public IReadOnlyList<DiffLine> Align(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        ComparisonOptions options)
    {
        // A pairing the user made by hand is an anchor in exactly the sense the large-document path
        // already means: a point both sides agree on, with independent problems either side of it.
        // The difference is that this one is not a guess and is honoured at any size.
        var forced = AlignmentAnchors.Usable(options.Alignments, left.Count, right.Count);

        if (forced.Count > 0)
        {
            return SegmentedLineAligner.AlignAround(left, right, forced, AlignRegion);
        }

        return AlignRegion(left, right);
    }

    /// <summary>
    /// One region between - or outside - the user's anchors: either the whole thing, or split further
    /// when it is big enough to be worth it.
    /// </summary>
    private IReadOnlyList<DiffLine> AlignRegion(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count >= SegmentedFrom || right.Count >= SegmentedFrom
            ? SegmentedLineAligner.Align(left, right, AlignSegment)
            : AlignWhole(left, right);

    /// <summary>One segment, as spans into the caller's arrays - see <see cref="SegmentedLineAligner"/>.</summary>
    private IReadOnlyList<DiffLine> AlignSegment(ReadOnlyMemory<string> left, ReadOnlyMemory<string> right) =>
        AlignWhole(left.Span, right.Span);

    private IReadOnlyList<DiffLine> AlignWhole(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        AlignWhole(Join(left), Join(right));

    private IReadOnlyList<DiffLine> AlignWhole(ReadOnlySpan<string> left, ReadOnlySpan<string> right) =>
        AlignWhole(Join(left), Join(right));

    /// <summary>
    /// Turns DiffPlex's edit blocks into side-by-side rows.
    ///
    /// This used to call <c>SideBySideDiffBuilder</c>, which does the same thing and one more: for
    /// every modified line it also runs a WORD-level diff to fill in sub-pieces. This adapter never
    /// read those - character spans are computed later, by the inline engine, on the display text
    /// rather than on comparison keys - so the work was pure waste, and on one very long line it was
    /// catastrophic: two 1.8 MB minified documents took 68 seconds, essentially all of it inside a
    /// word diff whose output was discarded. Going straight to the line diff and pairing up the
    /// blocks here is the same alignment without that.
    ///
    /// The pairing rule is the builder's own, kept deliberately identical: within one block the first
    /// min(deleted, inserted) lines pair off as modified rows, and whatever is left over on the longer
    /// side becomes one-sided rows with a filler opposite.
    /// </summary>
    private IReadOnlyList<DiffLine> AlignWhole(string left, string right)
    {
        var diff = _differ.CreateDiffs(left, right, ignoreWhiteSpace: false, ignoreCase: false, LineChunker.Instance);

        var oldCount = diff.PiecesOld.Count;
        var newCount = diff.PiecesNew.Count;

        var rows = new List<DiffLine>(oldCount > newCount ? oldCount : newCount);

        // Next line not yet emitted, per side. Outside a block the two advance together - that is
        // what "unchanged" means - so one counter each is enough to number every row.
        var a = 0;
        var b = 0;

        foreach (var block in diff.DiffBlocks)
        {
            while (a < block.DeleteStartA)
            {
                rows.Add(Row(a++ + 1, b++ + 1, ChangeKind.Unchanged));
            }

            var paired = block.DeleteCountA < block.InsertCountB ? block.DeleteCountA : block.InsertCountB;
            for (var i = 0; i < paired; i++)
            {
                rows.Add(Row(a++ + 1, b++ + 1, ChangeKind.Modified));
            }

            for (var i = paired; i < block.DeleteCountA; i++)
            {
                rows.Add(Row(a++ + 1, null, ChangeKind.Deleted));
            }

            for (var i = paired; i < block.InsertCountB; i++)
            {
                rows.Add(Row(null, b++ + 1, ChangeKind.Inserted));
            }
        }

        while (a < oldCount)
        {
            rows.Add(Row(a++ + 1, b++ + 1, ChangeKind.Unchanged));
        }

        return rows;
    }

    /// <summary>
    /// Text is left null on purpose: <c>FileComparisonService</c> projects every row back onto the
    /// real document afterwards, and carrying the comparison key through here would leak the
    /// normalised form - a lower-cased copy of the user's own file - into the UI.
    /// </summary>
    private static DiffLine Row(int? leftNumber, int? rightNumber, ChangeKind kind) =>
        new(leftNumber, null, rightNumber, null, kind);

    private static string Join(IReadOnlyList<string> lines) => string.Join("\n", lines);

    /// <summary>
    /// DiffPlex takes one string and splits it itself, so a segment has to be joined back up. Built
    /// with the exact final length rather than through string.Join: this runs once per segment, and a
    /// large document has thousands of them.
    /// </summary>
    private static string Join(ReadOnlySpan<string> lines)
    {
        if (lines.Length == 1)
        {
            return lines[0];
        }

        var length = lines.Length - 1;
        foreach (var line in lines)
        {
            length += line.Length;
        }

        return string.Create(length, lines.ToArray(), static (span, source) =>
        {
            var at = 0;
            for (var i = 0; i < source.Length; i++)
            {
                if (i > 0)
                {
                    span[at++] = '\n';
                }

                source[i].AsSpan().CopyTo(span[at..]);
                at += source[i].Length;
            }
        });
    }
}
