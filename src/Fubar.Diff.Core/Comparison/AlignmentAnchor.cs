using System;
using System.Collections.Generic;

namespace Fubar.Diff.Core.Comparison;

/// <summary>
/// A pairing the user insisted on: this line on the left IS this line on the right.
///
/// There is a class of comparison no heuristic gets right, because the answer is not in the text. Two
/// files whose common content was rewritten, a config whose sections were reordered and edited, a
/// generated file whose boilerplate matches everywhere - the aligner picks a pairing that is
/// defensible and not the one the reader means, and every "ignore whitespace, ignore case" option in
/// the app is powerless, because none of them is about which lines correspond. Being able to say so
/// directly is the only fix, and it is what every serious diff tool eventually grows.
/// </summary>
/// <param name="LeftLine">1-based line number in the left file.</param>
/// <param name="RightLine">1-based line number in the right file.</param>
public readonly record struct AlignmentAnchor(int LeftLine, int RightLine)
{
    /// <summary>True when both sides name a real line. A zero or negative number is not one.</summary>
    public bool IsValid => LeftLine > 0 && RightLine > 0;
}

/// <summary>
/// Keeps a set of anchors usable as a set: sorted, non-overlapping, and each side strictly
/// increasing.
/// </summary>
public static class AlignmentAnchors
{
    /// <summary>
    /// Adds an anchor, replacing anything it contradicts.
    ///
    /// Two anchors conflict when honouring both would need the documents to cross over - if line 10
    /// pairs with line 20, then line 12 cannot pair with line 15, because the lines between them
    /// would have to run backwards on one side. Rather than refuse the new one (leaving the user to
    /// work out which old decision is in the way, having probably forgotten it), the ones it crosses
    /// are dropped: the newest instruction is the one they are looking at.
    ///
    /// An anchor naming a line that already appears on either side replaces that one outright, for
    /// the same reason - "no, THIS is what line 10 lines up with".
    /// </summary>
    public static IReadOnlyList<AlignmentAnchor> Add(
        IReadOnlyList<AlignmentAnchor> existing,
        AlignmentAnchor anchor)
    {
        if (!anchor.IsValid)
        {
            return existing;
        }

        var kept = new List<AlignmentAnchor>(existing.Count + 1);

        foreach (var other in existing)
        {
            var crosses = (other.LeftLine < anchor.LeftLine) != (other.RightLine < anchor.RightLine);
            var reuses = other.LeftLine == anchor.LeftLine || other.RightLine == anchor.RightLine;

            if (!crosses && !reuses)
            {
                kept.Add(other);
            }
        }

        kept.Add(anchor);
        kept.Sort(static (a, b) => a.LeftLine.CompareTo(b.LeftLine));

        return kept;
    }

    /// <summary>
    /// The anchors that can actually be honoured for a pair of documents this size, in order.
    ///
    /// A stale anchor - one pointing past the end of a file that has since been edited or replaced -
    /// is dropped rather than clamped: it named a line that is not there, and moving it to the nearest
    /// one that is would be inventing an instruction the user never gave.
    /// </summary>
    public static List<AlignmentAnchor> Usable(
        IReadOnlyList<AlignmentAnchor> anchors,
        int leftLines,
        int rightLines)
    {
        var usable = new List<AlignmentAnchor>(anchors.Count);

        foreach (var anchor in anchors)
        {
            if (anchor.IsValid && anchor.LeftLine <= leftLines && anchor.RightLine <= rightLines)
            {
                usable.Add(anchor);
            }
        }

        usable.Sort(static (a, b) => a.LeftLine.CompareTo(b.LeftLine));

        // Anything that would run backwards on the right after sorting by the left is dropped, so a
        // set that arrived out of order (from a settings file, say) cannot make the aligner emit rows
        // in an impossible order.
        var result = new List<AlignmentAnchor>(usable.Count);
        var lastRight = 0;

        foreach (var anchor in usable)
        {
            if (anchor.RightLine > lastRight)
            {
                result.Add(anchor);
                lastRight = anchor.RightLine;
            }
        }

        return result;
    }
}
