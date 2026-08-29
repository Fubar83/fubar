using System.Collections.Generic;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Rendering;

/// <summary>A run of rows to hide behind one collapsed placeholder. Inclusive, 0-based row indices.</summary>
/// <param name="StartRow">First hidden row.</param>
/// <param name="EndRow">Last hidden row.</param>
public readonly record struct FoldRange(int StartRow, int EndRow)
{
    /// <summary>How many rows the fold hides.</summary>
    public int Length => EndRow - StartRow + 1;
}

/// <summary>
/// Works out which stretches of unchanged context are worth hiding.
///
/// A file of 3,000 lines with two changes in it is 3,000 lines of scrolling to read two. Every review
/// tool solves this the same way - keep a few lines either side of each change for context, fold the
/// rest behind something you can click - and this computes where those folds go.
///
/// Pure, and expressed as ROW ranges rather than as anything the editor understands, for the usual
/// reason: the rule about how much context is enough is domain policy and should be testable without a
/// window. It also has to produce the SAME answer for both panes, which it does for free by working
/// from row indices - both sides have identical row counts, so identical folds, which is what keeps
/// scroll sync a plain offset copy even with half the document hidden.
/// </summary>
public static class CollapsedRegions
{
    /// <summary>
    /// How few rows a fold may hide before it stops being worth it. Hiding two lines behind a
    /// placeholder that itself occupies one saves nothing and costs a click.
    /// </summary>
    private const int MinimumWorthHiding = 3;

    /// <summary>
    /// The stretches to fold, in document order.
    /// </summary>
    /// <param name="lines">The aligned rows.</param>
    /// <param name="contextLines">
    /// How many unchanged rows to keep visible either side of a change. Zero is allowed and means
    /// "show only the changes", which is legible for a diff being skim-read and useless for one being
    /// understood - hence a default of three rather than none.
    /// </param>
    public static IReadOnlyList<FoldRange> Compute(IReadOnlyList<DiffLine> lines, int contextLines) =>
        Compute(lines.Count, i => IsCollapsible(lines[i]), contextLines);

    /// <summary>
    /// The same, for a three-way merge. A merge benefits from this MORE than a diff does, not less:
    /// most of its regions resolve themselves, so the reader is looking for the few that do not, in
    /// the same thousands of unchanged lines.
    ///
    /// There is no ignored-row case here - a merge has no ignore rules of its own - so context is
    /// simply everything outside a region.
    /// </summary>
    public static IReadOnlyList<FoldRange> Compute(Merge.ThreeWayResult result, int contextLines) =>
        Compute(result.Lines.Count, i => !result.Lines[i].IsChange, contextLines);

    private static IReadOnlyList<FoldRange> Compute(int rowCount, System.Func<int, bool> isCollapsible, int contextLines)
    {
        var folds = new List<FoldRange>();
        var context = contextLines < 0 ? 0 : contextLines;
        var runStart = -1;

        for (var i = 0; i <= rowCount; i++)
        {
            // One past the end closes any run still open, so the tail of the file needs no special case
            // beyond the boundary rule below.
            var isContext = i < rowCount && isCollapsible(i);

            if (isContext)
            {
                if (runStart < 0)
                {
                    runStart = i;
                }

                continue;
            }

            if (runStart >= 0)
            {
                AddFold(folds, runStart, i - 1, rowCount, context);
                runStart = -1;
            }
        }

        return folds;
    }

    /// <summary>
    /// Whether a row is context that may be hidden.
    ///
    /// An IGNORED row is deliberately not collapsible, the same call <c>ChangeGroupSlider</c> makes
    /// about sliding across one. Its faint band exists precisely so the reader can see that something
    /// differs there and is being hidden by a rule they chose; folding it away would hide the evidence
    /// that the rule is doing anything, which is the one thing they want to check after adding it.
    /// </summary>
    private static bool IsCollapsible(DiffLine row) => !row.IsChange && !row.IsIgnored;

    /// <summary>
    /// Trims a run of context down to the part that is far enough from any change to hide, and keeps
    /// it only if enough is left to be worth a fold.
    ///
    /// The boundary rule: a run touching the start or end of the document has no change on that side to
    /// give context TO, so it keeps none there. Without it, every file would open showing three
    /// arbitrary lines of its header before the first fold.
    /// </summary>
    private static void AddFold(List<FoldRange> folds, int runStart, int runEnd, int rowCount, int context)
    {
        var from = runStart == 0 ? 0 : runStart + context;
        var to = runEnd == rowCount - 1 ? runEnd : runEnd - context;

        if (to - from + 1 >= MinimumWorthHiding)
        {
            folds.Add(new FoldRange(from, to));
        }
    }
}
