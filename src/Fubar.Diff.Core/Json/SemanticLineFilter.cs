using System.Collections.Generic;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Core.Json;

/// <summary>
/// Reconciles a text-level alignment with a semantic comparison.
///
/// The two differs answer different questions, and this uses each for what it is good at. The text
/// differ decides how the two documents LINE UP - it is well tested, handles insertions and deletions,
/// and produces the filler rows the editors need. The semantic differ decides which lines actually
/// MATTER. A row whose text differs but which the semantic pass did not flag - a reordered property, a
/// reformatted block - is downgraded to unchanged.
///
/// The alternative, building an alignment from the AST directly, means reimplementing the alignment
/// half from scratch and getting fillers, ordering and hunk grouping right a second time.
/// </summary>
public static class SemanticLineFilter
{
    /// <summary>
    /// Returns a result in which only semantically significant rows are still marked as changes.
    ///
    /// Line numbers in the sets are 1-based, matching <see cref="DiffLine.LeftNumber"/>.
    /// </summary>
    public static DiffResult Apply(
        DiffResult textResult,
        IReadOnlySet<int> significantLeftLines,
        IReadOnlySet<int> significantRightLines)
    {
        var filtered = new List<DiffLine>(textResult.Lines.Count);

        foreach (var row in textResult.Lines)
        {
            filtered.Add(IsSignificant(row, significantLeftLines, significantRightLines)
                ? row
                : Downgrade(row));
        }

        return DiffResult.Create(filtered);
    }

    /// <summary>
    /// Whether a row touches any line the semantic pass flagged. Either side counts: a deletion only
    /// has a left line, an insertion only a right one.
    /// </summary>
    private static bool IsSignificant(
        DiffLine row,
        IReadOnlySet<int> significantLeftLines,
        IReadOnlySet<int> significantRightLines)
    {
        if (!row.IsChange)
        {
            return false;
        }

        return (row.LeftNumber is { } left && significantLeftLines.Contains(left))
               || (row.RightNumber is { } right && significantRightLines.Contains(right));
    }

    /// <summary>
    /// Turns an insignificant change back into context.
    ///
    /// A row where only one side has a line keeps its filler on the other side - dropping it would
    /// shorten one document and break the alignment invariant that both sides have the same number of
    /// rows. So the row survives, just untinted, and the character spans go with the tint.
    /// </summary>
    private static DiffLine Downgrade(DiffLine row) => row.Kind == ChangeKind.Filler
        ? row
        : row with { Kind = ChangeKind.Unchanged, LeftSpans = [], RightSpans = [] };
}
