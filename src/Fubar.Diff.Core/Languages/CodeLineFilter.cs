using System.Collections.Generic;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Languages;

/// <summary>
/// Downgrades rows that only exist because of a comment or a blank line, after the alignment is done.
///
/// Why a second pass instead of just leaving it to the keys: a comment CHANGED is already handled by
/// keying (both sides reduce to the same code, so the row matches), but a comment-only line ADDED has
/// no counterpart to match against - its key is empty and the other side has no row at all, so the
/// aligner correctly reports an insertion. Only something looking at the finished rows can say "that
/// insertion is a comment, and you asked not to see those."
///
/// The downgrade follows the rule the JSON pass already established: an ignored row becomes
/// <see cref="ChangeKind.Unchanged"/> with <c>IsIgnored</c> set, never a <see cref="ChangeKind"/> of
/// its own. That keeps it out of hunks, counts, the diff map and F7/F8 while still letting a renderer
/// draw a faint band, so the user can tell "these are the same" from "this is being hidden" - which is
/// exactly what they want to check right after ticking the box.
/// </summary>
public static class CodeLineFilter
{
    /// <summary>
    /// Returns a result with comment-only and blank rows downgraded, or the input untouched when
    /// neither side had anything to filter.
    /// </summary>
    public static DiffResult Apply(DiffResult result, CodeLines? left, CodeLines? right)
    {
        if (left is null && right is null)
        {
            return result;
        }

        List<DiffLine>? filtered = null;

        for (var i = 0; i < result.Lines.Count; i++)
        {
            var row = result.Lines[i];

            if (!row.IsChange || !IsNoise(row, left, right))
            {
                filtered?.Add(row);
                continue;
            }

            // First row worth changing: copy what came before it, so an unfiltered document keeps its
            // original list rather than paying for a full copy to produce the same rows back.
            if (filtered is null)
            {
                filtered = new List<DiffLine>(result.Lines.Count);
                for (var j = 0; j < i; j++)
                {
                    filtered.Add(result.Lines[j]);
                }
            }

            // The filler on the other side stays: dropping the row would shorten one document and
            // break the invariant that both sides have the same number of rows.
            filtered.Add(row with
            {
                Kind = ChangeKind.Unchanged,
                LeftSpans = [],
                RightSpans = [],
                IsIgnored = true,
            });
        }

        return filtered is null ? result : DiffResult.Create(filtered);
    }

    /// <summary>
    /// Whether every side this row has content on is ignorable. A modified row needs BOTH sides to
    /// qualify - a comment that was replaced by real code is a change, however the reader feels about
    /// comments.
    /// </summary>
    private static bool IsNoise(DiffLine row, CodeLines? left, CodeLines? right)
    {
        var sides = 0;

        if (row.LeftNumber is { } leftNumber)
        {
            if (left?.IsIgnorable(leftNumber) != true)
            {
                return false;
            }

            sides++;
        }

        if (row.RightNumber is { } rightNumber)
        {
            if (right?.IsIgnorable(rightNumber) != true)
            {
                return false;
            }

            sides++;
        }

        return sides > 0;
    }
}
